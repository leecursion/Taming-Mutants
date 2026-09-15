// Uses the local proxy and real configured Solar. Never prints credentials.
import {readFileSync,writeFileSync} from 'node:fs';
import assert from 'node:assert/strict';
const config=JSON.parse(readFileSync(new URL('../../Temp/DockingChecks/editor-service.json',import.meta.url),'utf8'));
if(!config.resolverEndpoint) throw Error('A local UPSTAGE_API_KEY is required for live command validation.');
const live=JSON.parse(readFileSync(new URL('../../Temp/DockingChecks/live-results.json',import.meta.url),'utf8'));
const current={pdbId:'1IEP',title:'ABL kinase',chains:'A',view:'ribbon',availableChains:'A',residueRanges:'A:225..498',ligands:'STI',
  docking:'현재 후보=caffeine; 현재 부위=A:300-305; '+live.trials.map(t=>`후보=${t.ligand.title}; Vina 점수(kcal/mol)=${t.result.poses[0].score}; 비교키=${t.result.comparisonKey}`).join('\n')};
const results=[];
for(const [query,expected] of [['아스피린을 도킹할 후보로 불러줘','load_ligand'],['이번에는 카페인으로 해보자','load_ligand'],
  ['현재 후보로 도킹 실행해줘','dock'],['A 체인 300번부터 305번을 도킹 부위로 선택해줘','docking_site'],['두 후보의 도킹 결과를 비교해줘','explain']]) {
  const response=await fetch(config.resolverEndpoint,{method:'POST',headers:{'Content-Type':'application/json','X-App-Token':config.token},
    body:JSON.stringify({query,current}),signal:AbortSignal.timeout(65000)});
  const result=await response.json();
  assert.equal(response.status,200,JSON.stringify(result)); assert.equal(result.action,expected,JSON.stringify(result));
  results.push({query,result}); console.log(query,'->',result.action,result.ligandQuery||result.siteSelection||'');
}
// Real recommendation produces clickable ligand choices; ordinal selection retains the receptor.
const recommendations=await fetch(config.resolverEndpoint,{method:'POST',headers:{'Content-Type':'application/json','X-App-Token':config.token},
  body:JSON.stringify({query:'현재 단백질의 도킹 실험에서 비교할 소분자 후보를 3개 추천해줘. 이미 시험한 후보가 있다면 다른 후보를 제안해줘.',current}),signal:AbortSignal.timeout(65000)});
const offered=await recommendations.json();
assert.equal(offered.action,'candidate_choices',JSON.stringify(offered)); assert.ok(offered.choices.length>=2);
const choiceResponse=await fetch(config.resolverEndpoint,{method:'POST',headers:{'Content-Type':'application/json','X-App-Token':config.token},
  body:JSON.stringify({query:'2번 것으로 보여줘',current,pendingChoices:offered.choices,choiceKind:'ligand'}),signal:AbortSignal.timeout(65000)});
const chosen=await choiceResponse.json(); assert.equal(chosen.action,'load_ligand'); assert.equal(chosen.ligandQuery,offered.choices[1]);
results.push({recommendations:offered,selection:chosen});
console.log('Candidate cards:',offered.choices.join(', '),'-> selected',chosen.ligandQuery);
// Verify Korean text in the candidate panel resolves to a real PubChem identity.
const response=await fetch(config.dockingEndpoint,{method:'POST',headers:{'Content-Type':'application/json','X-App-Token':config.token},
  body:JSON.stringify({op:'ligand',sessionId:'12345678901234567890123456789012',query:'아스피린'}),signal:AbortSignal.timeout(65000)});
const candidate=await response.json(); assert.equal(response.status,200,JSON.stringify(candidate)); assert.equal(candidate.ligand.cid,'2244');
writeFileSync(new URL('../../Temp/DockingChecks/live-commands.json',import.meta.url),JSON.stringify({results,koreanCandidate:candidate.ligand.title},null,2));
console.log('PASS: live Korean commands, result discussion and Korean candidate -> PubChem CID 2244');
