import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import worker from './src/index.js';
import {catalog,validateIntent} from './src/molecule-resolve.js';
const env={UPSTAGE_API_KEY:'test-only',APP_TOKEN:'test'};
const llm=value=>new Response(JSON.stringify({choices:[{message:{content:JSON.stringify(value)}}]}));
const data=value=>new Response(JSON.stringify(value));
const current={pdbId:'4HHB',title:'헤모글로빈',chains:'A',availableChains:'A,B,C,D',view:'ribbon',
 residueRanges:'A:1..141; B:1..146',ligands:'HEM,PO4',description:'산소 비결합',displayScope:'A 141 residues'};
test('chain formatting is normalized without changing case or accepting invalid identifiers',()=>{
 assert.equal(validateIntent({chains:' A, B,A '}).chains,'A,B');
 assert.equal(validateIntent({chains:'a,B'}).chains,'a,B');
 assert.throws(()=>validateIntent({chains:'AA'}));
 assert.throws(()=>validateIntent({chains:'A,,B'}));
});
async function invoke(query,fetcher,options={}) {
 const old=globalThis.fetch; globalThis.fetch=fetcher;
 try { return await worker.fetch(new Request('https://test/api/molecule-resolve',{
  method:'POST',headers:{'Content-Type':'application/json','X-App-Token':options.token??'test'},
  body:JSON.stringify({query,current:options.current,history:options.history,pendingChoices:options.pendingChoices})}),options.env??env); }
 finally { globalThis.fetch=old; }
}
function mockSearch(intent,candidates,choice,events=[]) {
 return async(url,options)=>{
  if(url.includes('upstage')) {
   const request=JSON.parse(options.body), prompt=request.messages[0].content;
   events.push({kind:prompt.startsWith('Select')?'rank':'intent',request});
   if(prompt.startsWith('Select')) return llm(choice);
   return llm(intent);
  }
  if(url.includes('search.rcsb')) { events.push({kind:'search',query:JSON.parse(options.body)}); return data({result_set:candidates.map(c=>({identifier:c.id}))}); }
  const candidate=candidates.find(c=>url.includes(c.id.split('_')[0]));
  if(url.includes('/core/entry/')) return data({struct:{title:candidate.entryTitle??''},rcsb_entry_info:{nonpolymer_bound_components:candidate.ligands??[]}});
  return data({entity_poly:{type:'polypeptide(L)',rcsb_sample_sequence_length:candidate.length??130},
   rcsb_entity_source_organism:[{ncbi_taxonomy_id:candidate.species??9606,taxonomy_lineage:candidate.lineage??[]}],
   rcsb_polymer_entity:{pdbx_description:candidate.title??'Lysozyme C',pdbx_mutation:candidate.mutation??''},
   rcsb_polymer_entity_container_identifiers:{auth_asym_ids:['A']}});
 };
}
test('auth, missing key, and bad input never call LLM',async()=>{
 let calls=0; const fail=()=>{calls++;throw new Error();};
 assert.equal((await invoke('인슐린',fail,{token:'wrong'})).status,401);
 assert.equal((await invoke('',fail)).status,400);
 assert.equal((await invoke('a'.repeat(301),fail)).status,400);
 assert.equal((await invoke('인슐린',fail,{env:{APP_TOKEN:'test'}})).status,503);
 assert.equal(calls,0);
});
test('function-based description can resolve to a representative via LLM',async()=>{
 const result=await (await invoke('혈당을 조절하는 그 단백질 좀 보여줄래?',()=>llm({catalogId:'insulin',taxonomyId:9606}))).json();
 assert.equal(result.action,'load'); assert.equal(result.spec.pdbId,'1TRZ'); assert.equal(result.spec.chains,'A,B');
});
test('broad cell request asks for renderable proteins and the next answer keeps clarification context',async()=>{
 const question='백혈구는 하나의 PDB 분자가 아니에요. CD4, T세포 수용체, 항체 중 무엇을 볼까요?';
 const first=await (await invoke('백혈구 보여줘',(url,options)=>{
  const request=JSON.parse(options.body);
  assert.match(request.messages[0].content,/Whole cells, tissues, organs/);
  return llm({action:'clarify',choices:['CD4','T세포 수용체','항체'],message:question});
 })).json();
 assert.equal(first.action,'clarify'); assert.match(first.message,/CD4/);
 const history=[{role:'user',content:'백혈구 보여줘'},{role:'assistant',content:question}];
 let searches=0;
 const second=await (await invoke('CD4로 보여줘',(url,options)=>{
  if(url.includes('upstage')) {
   const input=JSON.parse(JSON.parse(options.body).messages[1].content);
   assert.deepEqual(input.history,history);
   return llm({action:'search',proteinName:'T-cell surface glycoprotein CD4',aliases:['CD4'],taxonomyId:9606});
  }
  searches++; return data({result_set:[]});
 },{history})).json();
 assert.ok(searches>0); assert.equal(second.action,'clarify');
});
test('clarification always reaches Unity as a question and includes structured choices',async()=>{
 const result=await (await invoke('면역에 관련된 것',()=>llm({action:'clarify',choices:['CD4','CD8'],message:'대상이 넓습니다'}))).json();
 assert.equal(result.action,'clarify'); assert.match(result.message,/CD4/); assert.match(result.message,/CD8/); assert.match(result.message,/\?$/);
});

test('real-model empty view still asks a question and can load a function-based target',async()=>{
 let calls=0;
 const first=await (await invoke('백혈구 보여줘',()=>{
  calls++; return llm({action:'clarify',view:'',choices:['CD4','항체'],message:'어느 쪽을 볼까요?'});
 })).json();
 assert.equal(calls,1); assert.equal(first.action,'clarify'); assert.match(first.message,/CD4/);
 const second=await (await invoke('혈당을 낮추는 물질',()=>llm({action:'search',view:'',catalogId:'insulin'}))).json();
 assert.equal(second.action,'load'); assert.equal(second.spec.view,'ribbon');
 assert.throws(()=>validateIntent({view:'invalid'}));
});

test('recommendations without a current molecule offer roles and accept an ordinal selection',async()=>{
 const choices=['인슐린 — 혈당 조절','헤모글로빈 — 산소 운반','GFP — 형광'];
 const first=await (await invoke('어떤 분자를 추천하나요?',()=>llm({action:'clarify',view:'',choices,message:'관심 있는 기능을 골라주세요.'}))).json();
 assert.equal(first.action,'clarify'); assert.deepEqual(first.choices,choices);
 assert.ok(first.message.indexOf('1) 인슐린')<first.message.indexOf('2) 헤모글로빈'));
 const history=[{role:'user',content:'어떤 분자를 추천하나요?'},{role:'assistant',content:first.message}];
 const second=await (await invoke('두 번째',(_url,options)=>{
  const data=JSON.parse(JSON.parse(options.body).messages[1].content);
  assert.deepEqual(data.pendingChoices,choices); assert.deepEqual(data.history,history);
  return llm({action:'search',catalogId:'hemoglobin',view:''});
 },{history,pendingChoices:choices})).json();
 assert.equal(second.action,'load'); assert.equal(second.spec.pdbId,'4HHB');
});

test('new recommendations outside the cache can be discussed and searched without a current structure',async()=>{
 const choices=['아밀레이스 — 녹말 분해','트립신 — 단백질 분해','리파아제 — 지방 분해'];
 const first=await (await invoke('그 셋 말고 소화에 관련된 것을 추천해줘',()=>llm({action:'clarify',choices,message:'소화 효소를 비교해 볼까요?'}))).json();
 assert.deepEqual(first.choices,choices);
 const history=[{role:'user',content:'그 셋 말고 소화에 관련된 것을 추천해줘'},{role:'assistant',content:first.message}];
 const explanation=await (await invoke('첫 번째를 왜 추천했어?',()=>llm({action:'explain',message:'아밀레이스는 녹말을 분해하는 효소라 기질과 효소의 관계를 살펴보기 좋아요.'}),{history,pendingChoices:choices})).json();
 assert.equal(explanation.action,'explain'); assert.match(explanation.message,/아밀레이스/);
 history.push({role:'user',content:'첫 번째를 왜 추천했어?'},{role:'assistant',content:explanation.message});
 const events=[];
 const result=await (await invoke('첫 번째 것으로 보여줘',mockSearch(
  {action:'search',proteinName:'alpha-amylase',aliases:['amylase'],catalogId:''},
  [{id:'1HNY_1',title:'ALPHA-AMYLASE',length:496}],{candidateId:'1HNY_1'},events),
  {history,pendingChoices:choices})).json();
 assert.equal(result.action,'load'); assert.equal(result.spec.pdbId,'1HNY');
 assert.ok(events.some(e=>e.kind==='search'));
 assert.deepEqual(JSON.parse(events.find(e=>e.kind==='rank').request.messages[1].content).pendingChoices,choices);
});

test('an explicit choice repairs redundant confirmation without treating a why question as selection',async()=>{
 let calls=0;
 const mock=mockSearch({proteinName:'pepsin'},[{id:'1PSO_1',title:'PEPSIN'}],{candidateId:'1PSO_1'});
 const result=await (await invoke('첫 번째 것으로 보여줘',async(url,options)=>{
  if(url.includes('upstage') && !JSON.parse(options.body).messages[0].content.startsWith('Select')) {
   const request=JSON.parse(JSON.parse(options.body).messages[1].content);
   assert.equal(request.selectedChoice,'펩신');
   if(++calls===1) return llm({action:'clarify',message:'펩신을 볼까요?'});
  }
  return mock(url,options);
 },{pendingChoices:['펩신','리파아제']})).json();
 assert.equal(calls,2); assert.equal(result.action,'load');
 await invoke('첫 번째를 왜 추천했어?',(_url,options)=>{
  assert.equal(JSON.parse(JSON.parse(options.body).messages[1].content).selectedChoice,'');
  return llm({action:'explain',message:'단백질 소화를 살펴보기 좋아요.'});
 },{pendingChoices:['펩신','리파아제']});
});

test('an ordinal answer keeps the resolved target and conversation through ranking to load',async()=>{
 const history=[{role:'user',content:'백혈구 보여줘'},
  {role:'assistant',content:'CD4와 CD8 중 어느 것을 볼까요?'}];
 const events=[];
 const result=await (await invoke('첫 번째 것으로 보여줘',mockSearch(
  {action:'search',proteinName:'CD4',aliases:['T-cell surface glycoprotein CD4'],taxonomyId:9606},
  [{id:'1WIO_1',title:'T-cell surface glycoprotein CD4'}],
  {candidateId:'1WIO_1',message:'CD4 구조를 선택했어요.'},events),{history,pendingChoices:['CD4','CD8']})).json();
 const ranked=JSON.parse(events.find(e=>e.kind==='rank').request.messages[1].content);
 assert.deepEqual(ranked.history,history);
 assert.deepEqual(ranked.pendingChoices,['CD4','CD8']);
 assert.deepEqual(JSON.parse(events.find(e=>e.kind==='intent').request.messages[1].content).pendingChoices,['CD4','CD8']);
 assert.equal(ranked.proteinName,'CD4');
 assert.equal(ranked.taxonomyId,9606);
 assert.equal(result.action,'load'); assert.equal(result.spec.pdbId,'1WIO');
});

test('malformed real-model candidate JSON is repaired with the same target and candidates',async()=>{
 let rankCalls=0;
 const mock=mockSearch({proteinName:'CD4'},[{id:'1WIO_1',title:'CD4'}],{candidateId:'1WIO_1',evidence:['CD4']});
 const result=await (await invoke('첫 번째 것으로 보여줘',async(url,options)=>{
  if(url.includes('upstage') && JSON.parse(options.body).messages[0].content.startsWith('Select')) {
   rankCalls++;
   if(rankCalls===1) return data({choices:[{message:{content:'{"candidateId":"1WIO_1","evidence":["title":"CD4"]}'}}]});
   assert.equal(JSON.parse(JSON.parse(options.body).messages[1].content).proteinName,'CD4');
  }
  return mock(url,options);
 })).json();
 assert.equal(rankCalls,2); assert.equal(result.action,'load'); assert.equal(result.spec.pdbId,'1WIO');
});
test('schemas permit ranges and aliases but reject arbitrary commands and invalid identifiers',()=>{
 assert.deepEqual(validateIntent({proteinName:'β-lactoglobulin',aliases:['BLG'],residueStart:50,residueEnd:80}).aliases,['BLG']);
 for(const value of [{catalogId:'fake'},{catalogId:'insulin',taxonomyId:9823},{taxonomyId:-1},
  {proteinName:'<script>'},{action:'execute_code'},{chains:'A;DROP'}, {residueStart:80,residueEnd:50}, {secondaryStructure:'whatever'},[],null])
  assert.throws(()=>validateIntent(value));
});
test('LLM compares actual candidates, rejects fragment, does not choose first match',async()=>{
 const events=[];
 const result=await (await invoke('라이소자임 보여줘',mockSearch({proteinName:'Lysozyme C',aliases:['lysozyme']},
  [{id:'5ABC_3',length:14},{id:'6ABC_1',length:130}],{candidateId:'6ABC_1',message:'온전한 단백질을 선택했어요.'},events))).json();
 assert.equal(result.spec.pdbId,'6ABC');
 const rank=events.find(e=>e.kind==='rank'); assert.ok(rank);
 const offered=JSON.parse(rank.request.messages[1].content).candidates;
 assert.equal(offered.length,2); assert.equal(offered[0].length,14);
 const search=events.find(e=>e.kind==='search'); assert.equal(search.query.query.nodes[0].logical_operator,'or');
});
test('a genuinely short peptide is selectable rather than banned by 25-residue threshold',async()=>{
 const result=await (await invoke('옥시토신 같은 짧은 펩타이드',mockSearch({proteinName:'peptide'},
  [{id:'1ABC_1',length:9,title:'Short peptide'}],{candidateId:'1ABC_1'}))).json();
 assert.equal(result.spec.pdbId,'1ABC');
});
test('species uses searchable lineage and metadata validation before LLM ranking',async()=>{
 const events=[];
 const result=await (await invoke('설치류 라이소자임',mockSearch({proteinName:'lysozyme',taxonomyId:9989},
  [{id:'1ABC_1',species:9823},{id:'2ABC_1',species:10090,lineage:[{id:'9989'}]}],{candidateId:'2ABC_1'},events))).json();
 assert.equal(result.spec.pdbId,'2ABC');
 const filter=events.find(e=>e.kind==='search').query.query.nodes[1].parameters;
 assert.equal(filter.attribute,'rcsb_entity_source_organism.taxonomy_lineage.id'); assert.equal(filter.value,'9989');
 assert.equal(JSON.parse(events.find(e=>e.kind==='rank').request.messages[1].content).candidates.length,1);
});
test('empty exact search retries full text, retaining species condition',async()=>{
 let searches=0;
 const mock=mockSearch({proteinName:'albumin',taxonomyId:9606},[{id:'1ABC_1'}],{candidateId:'1ABC_1'});
 const result=await (await invoke('사람 알부민',async(url,options)=>{
  if(url.includes('search.rcsb')) {
   searches++; if(searches===1) return new Response(null,{status:204});
   const query=JSON.parse(options.body).query;
   assert.equal(query.nodes[0].service,'full_text'); assert.equal(query.nodes[1].parameters.value,'9606');
  }
  return mock(url,options);
 })).json();
 assert.equal(searches,2); assert.equal(result.spec.pdbId,'1ABC');
});
test('specific state is searched and selected using supplied evidence instead of blanket refusal',async()=>{
 const events=[];
 const result=await (await invoke('산소가 붙어 있는 헤모글로빈',mockSearch(
  {proteinName:'hemoglobin',conditions:['oxygenated'],taxonomyId:9606},
  [{id:'1ABC_1',entryTitle:'OXYGENATED HUMAN HEMOGLOBIN',ligands:['OXY','HEM']}],
  {candidateId:'1ABC_1',evidence:['OXYGENATED HUMAN HEMOGLOBIN'],message:'산소 결합 구조를 골랐어요.'},events))).json();
 assert.equal(result.spec.pdbId,'1ABC'); assert.ok(events.find(e=>e.kind==='rank'));
});
test('unverified condition or fabricated candidate cannot silently become success',async()=>{
 for(const choice of [{candidateId:'FAKE_1'},{candidateId:'1ABC_1',evidence:['invented oxygenated state']}]) {
  const result=await (await invoke('산소 결합 헤모글로빈',mockSearch({proteinName:'hemoglobin',conditions:['oxygenated']},
   [{id:'1ABC_1',entryTitle:'Deoxy hemoglobin'}],choice))).json();
  assert.equal(result.action,'clarify'); assert.equal(result.spec,undefined);
 }
});
test('follow-up requests use current structure and produce view/zoom/rotation actions',async()=>{
 const pairs=[
  ['이거 원자 하나하나 보이게 해줘',{action:'view',view:'atoms'}],
  ['나선 부분만 볼래',{action:'view',view:'ribbon',secondaryStructure:'helix'}],
  ['50에서 80번만',{action:'view',residueStart:50,residueEnd:80}],
  ['헴만 남겨줘',{action:'view',view:'ligand',ligand:'HEM'}],
  ['조금 더 크게',{action:'zoom',amount:1.3}],
  ['오른쪽으로 돌려줘',{action:'rotate',amount:40}]
 ];
 for(const [query,intent] of pairs) {
  const result=await (await invoke(query,async(url,options)=>{
   assert.ok(url.includes('upstage'));
   const input=JSON.parse(JSON.parse(options.body).messages[1].content);
   assert.equal(input.current.pdbId,'4HHB'); assert.equal(input.current.ligands,'HEM,PO4');
   return llm(intent);
  },{current})).json();
  assert.equal(result.action,intent.action);
  if(intent.secondaryStructure) assert.equal(result.secondaryStructure,'helix');
  if(intent.residueEnd) assert.equal(result.residueEnd,80);
 }
});
test('missing current structure/chain/ligand is handled explicitly',async()=>{
 for(const [intent,context] of [[{action:'zoom'},null],[{action:'view',chains:'Z'},current],[{action:'view',ligand:'ATP'},current]]) {
  const result=await (await invoke('이것 바꿔줘',()=>llm(intent),{current:context})).json();
  assert.equal(result.action,'clarify'); assert.equal(result.spec,undefined);
 }
});
test('reset selection preserves client rendering budget and supplies existing chains',async()=>{
 const result=await (await invoke('전체로 돌아가줘',()=>llm({action:'view',resetSelection:true}),{current})).json();
 assert.equal(result.resetSelection,true); assert.equal(result.chains,'A,B,C,D');
});
test('malformed output is repaired once; transport failures are not blamed on phrasing',async()=>{
 let calls=0;
 const result=await (await invoke('인슐린',()=>{calls++;return calls===1?data({choices:[{message:{content:'broken'}}]}):llm({catalogId:'insulin'});})).json();
 assert.equal(calls,2); assert.equal(result.spec.pdbId,'1TRZ');
 assert.equal((await invoke('anything',()=>{throw new Error('offline');})).status,502);
});
test('clarification explains a real limitation without creating wrong geometry',async()=>{
 const result=await (await invoke('카페인',()=>llm({action:'clarify',message:'자유 소분자 렌더러는 아직 연결되지 않았어요. 결합 단백질을 찾아볼 수 있어요.'}))).json();
 assert.equal(result.action,'clarify'); assert.equal(result.spec,undefined);
});
test('bundled representative coordinates and catalog remain in sync',()=>{
 const cs=fs.readFileSync(new URL('../Assets/Scripts/Protein/MoleculeCatalog.cs',import.meta.url),'utf8');
 for(const entry of catalog) {
  const pdb=fs.readFileSync(new URL('../Assets/StreamingAssets/exploration/'+entry.pdbId+'.pdb',import.meta.url),'utf8');
  assert.ok(pdb.length<8*1024*1024);
  for(const chain of entry.chains.split(',')) assert.ok(pdb.split('\n').some(l=>l.startsWith('ATOM  ')&&l[21]===chain));
  assert.ok(cs.includes(`pdbId="${entry.pdbId}", chains="${entry.chains}"`));
 }
});
