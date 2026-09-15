import test from 'node:test';
import assert from 'node:assert/strict';
import worker from './src/index.js';
import {validateIntent} from './src/molecule-resolve.js';

const request=body=>new Request('https://proxy.test/api/docking',{method:'POST',headers:{'X-App-Token':'client'},body:JSON.stringify(body)});
test('docking auth and absent service do not create invented results',async()=>{
  const old=globalThis.fetch; let calls=0; globalThis.fetch=async()=>{ calls++; throw Error(); };
  try {
    assert.equal((await worker.fetch(request({op:'start'}),{APP_TOKEN:'wrong'})).status,401);
    const unavailable=await worker.fetch(request({op:'start'}),{APP_TOKEN:'client'});
    assert.equal(unavailable.status,503); assert.match((await unavailable.json()).error,/서버/); assert.equal(calls,0);
  } finally { globalThis.fetch=old; }
});
test('job operations forward only to configured service with separate token',async()=>{
  const old=globalThis.fetch; const seen=[];
  globalThis.fetch=async(url,init)=>{seen.push({url:String(url),init});return Response.json({jobId:'job',status:'running'});};
  try {
    const env={APP_TOKEN:'client',DOCKING_SERVICE_URL:'https://cpu.test',DOCKING_SERVICE_TOKEN:'server-secret'};
    for(const op of ['start','poll','cancel']) {
      const response=await worker.fetch(request({op,sessionId:'session',jobId:'job'}),env);
      assert.equal((await response.json()).status,'running');
    }
    assert.equal(seen.length,3);
    assert.ok(seen.every(s=>s.url==='https://cpu.test/api/docking'&&s.init.headers['X-App-Token']==='server-secret'));
    assert.equal((await worker.fetch(request({op:'shell'}),env)).status,400);
  } finally { globalThis.fetch=old; }
});
test('container binding is preferred over the URL and receives the same request',async()=>{
  const old=globalThis.fetch; let external=0; globalThis.fetch=async()=>{ external++; throw Error(); };
  const seen=[];
  const DOCKING={idFromName:name=>({name}),get:id=>({fetch:async(url,init)=>{seen.push({id,url,init});return Response.json({jobId:'job',status:'running'});}})};
  try {
    const env={APP_TOKEN:'client',DOCKING,DOCKING_SERVICE_URL:'https://cpu.test',DOCKING_SERVICE_TOKEN:'server-secret'};
    const response=await worker.fetch(request({op:'poll',sessionId:'session',jobId:'job'}),env);
    assert.equal((await response.json()).status,'running');
    assert.equal(external,0); assert.equal(seen.length,1);
    assert.equal(seen[0].id.name,'vina'); assert.equal(seen[0].url,'http://docking/api/docking');
    assert.equal(seen[0].init.headers['X-App-Token'],'server-secret'); assert.equal(JSON.parse(seen[0].init.body).op,'poll');
  } finally { globalThis.fetch=old; }
});
test('Korean candidate name is resolved before actual chemistry lookup',async()=>{
  const old=globalThis.fetch; let count=0;
  globalThis.fetch=async(url,init)=>{
    count++;
    if(String(url).includes('upstage')) return Response.json({choices:[{message:{content:'{"name":"aspirin"}'}}]});
    assert.equal(JSON.parse(init.body).query,'aspirin');
    return Response.json({ligand:{cid:'2244'}});
  };
  try {
    const response=await worker.fetch(request({op:'ligand',query:'아스피린'}),{APP_TOKEN:'client',UPSTAGE_API_KEY:'test',DOCKING_SERVICE_URL:'https://cpu.test'});
    assert.equal((await response.json()).ligand.cid,'2244'); assert.equal(count,2);
  } finally { globalThis.fetch=old; }
});
test('docking commands require explicit candidate/site and never contain model coordinates',()=>{
  assert.equal(validateIntent({action:'load_ligand',ligandQuery:'aspirin'}).ligandQuery,'aspirin');
  assert.equal(validateIntent({action:'docking_site',siteSelection:'A:123-130'}).siteSelection,'A:123-130');
  assert.throws(()=>validateIntent({action:'load_ligand'}));
  assert.throws(()=>validateIntent({action:'docking_site',siteSelection:'ATP pocket'}));
  assert.equal(validateIntent({action:'dock'}).action,'dock');
});
