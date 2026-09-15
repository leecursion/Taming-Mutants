// Local development only: the same Worker code, bound to loopback, with an ephemeral app token.
import http from 'node:http';
import {randomBytes} from 'node:crypto';
import {readFileSync,writeFileSync,mkdirSync,existsSync,unlinkSync} from 'node:fs';
import worker from './src/index.js';

const env={...process.env};
const vars=new URL('./.dev.vars',import.meta.url);
if(existsSync(vars)) for(const line of readFileSync(vars,'utf8').split(/\r?\n/)) {
  const match=/^\s*([A-Z_][A-Z_0-9]*)\s*=\s*(.*?)\s*$/.exec(line);
  if(!match) continue;
  let value=match[2];
  if(value.startsWith('"')&&value.endsWith('"')) { try { value=JSON.parse(value); } catch { continue; } }
  else if(value.startsWith("'")&&value.endsWith("'")) value=value.slice(1,-1);
  env[match[1]]=value;
}
env.APP_TOKEN=randomBytes(24).toString('hex');
env.DOCKING_SERVICE_URL='http://127.0.0.1:8000';
env.DOCKING_SERVICE_TOKEN=process.env.DOCKING_SERVICE_TOKEN||'';
const config=new URL('../Temp/DockingChecks/editor-service.json',import.meta.url);
const server=http.createServer(async(incoming,outgoing)=>{
  let size=0; const chunks=[];
  try {
    for await(const chunk of incoming) {
      size+=chunk.length;
      if(size>26*1024*1024) { outgoing.writeHead(413); outgoing.end(); return; }
      chunks.push(chunk);
    }
    const request=new Request('http://127.0.0.1:8791'+incoming.url,{method:incoming.method,
      headers:incoming.headers,body:incoming.method==='GET'?undefined:Buffer.concat(chunks)});
    const response=await worker.fetch(request,env);
    outgoing.writeHead(response.status,Object.fromEntries(response.headers)); outgoing.end(Buffer.from(await response.arrayBuffer()));
  } catch { outgoing.writeHead(502,{'Content-Type':'application/json'}); outgoing.end(JSON.stringify({error:'로컬 서버 요청에 실패했습니다.'})); }
});
server.listen(8791,'127.0.0.1',()=>{
  mkdirSync(new URL('../Temp/DockingChecks/',import.meta.url),{recursive:true});
  writeFileSync(config,JSON.stringify({resolverEndpoint:env.UPSTAGE_API_KEY?'http://127.0.0.1:8791/api/molecule-resolve':'',
    dockingEndpoint:'http://127.0.0.1:8791/api/docking',token:env.APP_TOKEN}));
  console.log('Local exploration and docking proxy listening on 127.0.0.1:8791. Unity Editor configuration written.');
  if(!env.UPSTAGE_API_KEY) console.log('UPSTAGE_API_KEY is absent: English/CID/SMILES ligand input works; Unity keeps its existing protein resolver. Local docking voice commands require server/.dev.vars.');
});
function shutdown() { if(existsSync(config)) unlinkSync(config); server.close(()=>process.exit()); }
process.on('SIGINT',shutdown); process.on('SIGTERM',shutdown);
