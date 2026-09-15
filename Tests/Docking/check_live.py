"""Actual PubChem -> Meeko -> Vina -> second candidate, through the HTTP API.
Requires local docking service on port 8000 and downloaded 1IEP PDB in Temp/DockingChecks.
"""
import json
import os
from pathlib import Path
import time
import uuid
import httpx

root = Path(__file__).resolve().parents[2]
directory = root / 'Temp/DockingChecks'
session = uuid.uuid4().hex
pdb = (directory / '1IEP.pdb').read_text()

with httpx.Client(timeout=65, headers={'X-App-Token': os.environ.get('DOCKING_SERVICE_TOKEN', '')}) as client:
    def call(**body):
        response = client.post('http://127.0.0.1:8000/api/docking', json=dict(sessionId=session, **body))
        response.raise_for_status()
        return response.json()

    results = []
    for query in ['aspirin', 'caffeine']:
        ligand = call(op='ligand', query=query)['ligand']
        print('Loaded', ligand['title'], len(ligand['atoms']), flush=True)
        job = uuid.uuid4().hex
        result = call(op='start', jobId=job, pdb=pdb, chains='A', sdf=ligand['sdf'],
                      center=[15.19, 53.903, 16.917], size=[20, 20, 20])
        deadline = time.monotonic() + 420
        while result['status'] in ('queued', 'running'):
            assert time.monotonic() < deadline, 'job timeout'
            time.sleep(2)
            result = call(op='poll', jobId=job)
        assert result['status'] == 'completed', result
        print('Docked', ligand['title'], [p['score'] for p in result['poses']], flush=True)
        results.append(dict(ligand=ligand, result=result))
    assert results[0]['result']['comparisonKey'] == results[1]['result']['comparisonKey']
    assert results[0]['ligand']['cid'] != results[1]['ligand']['cid']
    # The first completed job survives the second candidate and is still retrievable.
    first = call(op='poll', jobId=results[0]['result']['jobId'])
    assert first['poses'] == results[0]['result']['poses']
    (directory / 'live-results.json').write_text(json.dumps({'trials': results}, ensure_ascii=False), encoding='utf-8')
    print('PASS: two real candidates, same receptor/site, first result retained', flush=True)
