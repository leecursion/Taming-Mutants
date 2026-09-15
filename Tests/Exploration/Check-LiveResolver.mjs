// Read the existing scene connection in memory; never print or persist credentials.
import fs from 'node:fs';
import {handleMoleculeResolve} from '../../server/src/molecule-resolve.js';
const local=process.argv.includes('--local');
if(local) process.loadEnvFile(new URL('../../server/.dev.vars',import.meta.url));
if(local) {
 const realFetch=globalThis.fetch;
 globalThis.fetch=async(...args)=>{
  const response=await realFetch(...args);
  if(String(args[0]).includes('api.upstage.ai')) {
   const body=await response.clone().json();
   console.log('MODEL '+(body.choices?.[0]?.message?.content??'No model content'));
  }
  return response;
 };
}
const scene=fs.readFileSync(new URL('../../Assets/Scenes/Lab_Desktop.unity',import.meta.url),'utf8');
const block=scene.split(/^--- !u!/m).find(b=>/^  backendEndpoint: /m.test(b));
const field=name=>block?.match(new RegExp('^  '+name+': ([^\\r\\n]*)','m'))?.[1]?.trim().replace(/^"|"$/g,'');
const endpoint=field('backendEndpoint'), token=field('proxyToken');
if(!endpoint || !token) throw new Error('Scene proxy configuration missing');
const url=new URL('/api/molecule-resolve',endpoint);
const report=[];
const recommendations=process.argv.includes('--recommendations');
const diverse=process.argv.includes('--diverse');
const scenarios=diverse ? [['어떤 분자를 추천하나요?','그것들 말고 인슐린, 헤모글로빈, GFP도 제외하고 소화에 관련된 다른 단백질을 추천해줘','첫 번째를 왜 추천했어?','첫 번째 것으로 보여줘']] :
 recommendations ? [['어떤 분자를 추천하나요?','첫 번째 것으로 보여줘'],['처음 보는 사람인데 뭘 보면 재미있을까요?']] :
 [['혈당을 낮춰주는 물질을 보고 싶어'],['백혈구 보여줘','첫 번째 것으로 보여줘']];
for(const queries of scenarios) {
 const history=[];
 let pendingChoices=[];
 for(const query of queries) {
  const request=new Request(url,{method:'POST',headers:{'Content-Type':'application/json','X-App-Token':token},
   body:JSON.stringify({query,history,pendingChoices}),signal:AbortSignal.timeout(70000)});
  const response=local?await handleMoleculeResolve(request,{UPSTAGE_API_KEY:process.env.UPSTAGE_API_KEY}):await fetch(request);
  const result=await response.json();
  report.push({query,status:response.status,result});
  console.log(JSON.stringify(report.at(-1)));
  history.push({role:'user',content:query},{role:'assistant',content:result.message??''});
  if(result.action==='clarify') pendingChoices=result.choices??[];
  else if(result.action!=='explain') pendingChoices=[];
 }
}
fs.mkdirSync(new URL('../../Temp/ExplorationChecks/',import.meta.url),{recursive:true});
fs.writeFileSync(new URL('../../Temp/ExplorationChecks/'+(diverse?(local?'diverse-local.json':'diverse-deployed.json'):recommendations?(local?'recommendations-local.json':'recommendations-deployed.json'):(local?'live-local-resolver.json':'live-resolver.json')),import.meta.url),JSON.stringify(report,null,2));
