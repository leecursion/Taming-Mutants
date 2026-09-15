"""Regression for the user's 8VCX chain D failure through the running proxy."""
import json
from pathlib import Path
import time
import uuid
import httpx

checks = Path(__file__).resolve().parents[2] / 'Temp' / 'DockingChecks'
config = json.loads((checks / 'editor-service.json').read_text(encoding='utf-8'))
pdb = checks / '8VCX.pdb'
if not pdb.exists():
    pdb.write_text(httpx.get('https://files.rcsb.org/download/8VCX.pdb', timeout=30).raise_for_status().text)
session, job = uuid.uuid4().hex, uuid.uuid4().hex
with httpx.Client(timeout=60, headers={'X-App-Token': config['token']}) as client:
    def post(body):
        return client.post(config['dockingEndpoint'], json=body).raise_for_status().json()
    ligand = post(dict(op='ligand', sessionId=session, query='CCD:PO4'))['ligand']
    result = post(dict(op='start', sessionId=session, jobId=job, pdb=pdb.read_text(), chains='D',
                       sdf=ligand['sdf'], center=[67.4585, 214.8845, 22.786], size=[20, 20, 20]))
    deadline = time.monotonic() + 120
    while result['status'] in ('queued', 'running') and time.monotonic() < deadline:
        time.sleep(1)
        result = post(dict(op='poll', sessionId=session, jobId=job))
    assert result['status'] == 'failed', result
    assert 'D:2' in result['error'] and '원자 누락' in result['error'], result
    assert '서버 설정' not in result['error'] and '연결' not in result['error'], result
    (checks / 'preparation-failure.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: live 8VCX / D / PO4 reports incomplete receptor residues, not a server connection failure.')
    print(result['error'])
