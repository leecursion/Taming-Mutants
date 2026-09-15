"""Single-process CPU service. Run one Uvicorn worker; jobs have a one-hour TTL."""
import asyncio
from concurrent.futures import ThreadPoolExecutor
import hmac
import json
import logging
import httpx
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import tempfile
import threading
import time

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from chemistry import prepare_ligand, receptor_selection, validate_box, StereoSelectionRequired

app = FastAPI()
jobs = {}
lock = threading.Lock()
executor = ThreadPoolExecutor(max_workers=1)
ID = re.compile(r'^[a-f0-9]{32}$')
MAX_BODY = 26 * 1024 * 1024


def stop_process(process):
    if process.poll() is not None:
        return
    if os.name == 'nt':
        subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass


def execute(job, body):
    try:
        execute_inner(job, body)
    except Exception:
        with lock:
            process = job.pop('process', None)
            if process:
                stop_process(process)
            if job['status'] != 'cancelled':
                job['status'] = 'failed'
                job['result'] = {'error': '도킹 계산 프로세스를 실행하지 못했습니다.'}
                job['updated'] = time.monotonic()


def execute_inner(job, body):
    with tempfile.TemporaryDirectory(prefix='mutants-docking-') as directory:
        root = Path(directory)
        (root / 'input.json').write_text(json.dumps(body), encoding='utf-8')
        with (root / 'worker.log').open('w', encoding='utf-8') as log:
            with lock:
                if job['status'] == 'cancelled':
                    return
                job['status'] = 'running'
                process = subprocess.Popen([sys.executable, str(Path(__file__).with_name('worker.py')), directory],
                                           stdout=log, stderr=log, start_new_session=os.name != 'nt',
                                           creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
                job['process'] = process
            try:
                process.wait(timeout=390)
            except subprocess.TimeoutExpired:
                stop_process(process)
                process.wait()
            result_file = root / 'result.json'
            try:
                result = json.loads(result_file.read_text(encoding='utf-8')) if result_file.exists() else {'error': '도킹 계산 시간이 초과되었거나 계산 프로세스가 종료됐어요.'}
            except (ValueError, OSError):
                result = {'error': '도킹 결과를 읽지 못했습니다.'}
            if 'error' in result:
                log.flush()
                print((root / 'worker.log').read_text(encoding='utf-8', errors='replace')[-6000:], flush=True)
            with lock:
                job.pop('process', None)
                if job['status'] != 'cancelled':
                    job['status'] = 'failed' if 'error' in result else 'completed'
                    job['result'] = result
                job['updated'] = time.monotonic()


def public_job(job):
    return dict(jobId=job['id'], status=job['status'], **job.get('result', {}))


def dispatch(body):
    session = body.get('sessionId', '')
    if not isinstance(session, str) or not ID.fullmatch(session):
        raise ValueError('실험 세션이 올바르지 않습니다.')
    op = body.get('op')
    if op == 'ligand':
        query, sdf = body.get('query', ''), body.get('sdf', '')
        if not isinstance(query, str) or len(query) > 2000 or not isinstance(sdf, str) or len(sdf) > 200000:
            raise ValueError('후보 분자 입력이 너무 크거나 올바르지 않습니다.')
        try:
            return {'ligand': prepare_ligand(query, sdf)}
        except StereoSelectionRequired as error:
            return dict(status='needs_selection', error=str(error), choices=error.choices)
    job_id = body.get('jobId', '')
    if not isinstance(job_id, str) or not ID.fullmatch(job_id):
        raise ValueError('실험 ID가 올바르지 않습니다.')
    if op == 'start':
        # Validate before accepting any job. Never run executable names/paths supplied by clients.
        receptor_selection(body.get('pdb'), body.get('chains'))
        validate_box(body.get('center'), body.get('size'))
        if not isinstance(body.get('sdf'), str) or not 20 < len(body['sdf']) <= 200000:
            raise ValueError('먼저 후보 분자를 불러와 주세요.')
        with lock:
            for key in list(jobs):
                old = jobs[key]
                if old['status'] not in ('queued', 'running') and time.monotonic() - old['updated'] > 3600:
                    del jobs[key]
            if job_id in jobs:
                if jobs[job_id]['session'] != session:
                    raise ValueError('실험을 찾지 못했습니다.')
                return public_job(jobs[job_id])  # idempotent retry after a lost start response
            if len(jobs) >= 32 or sum(j['status'] in ('queued', 'running') for j in jobs.values()) >= 4:
                raise ValueError('계산 대기열이 가득 찼어요. 잠시 후 다시 시도해 주세요.')
            if any(j['session'] == session and j['status'] in ('queued', 'running') for j in jobs.values()):
                raise ValueError('진행 중인 실험을 완료하거나 취소한 뒤 다시 실행해 주세요.')
            job = dict(id=job_id, session=session, status='queued', updated=time.monotonic())
            jobs[job_id] = job
            executor.submit(execute, job, body)
            return public_job(job)
    with lock:
        job = jobs.get(job_id)
        # Cancellation can arrive before a large start upload. Keep a tombstone so
        # a subsequently accepted start with that ID can never consume CPU.
        if job is None and op == 'cancel' and len(jobs) < 32:
            job = dict(id=job_id, session=session, status='cancelled', updated=time.monotonic())
            jobs[job_id] = job
        if not job or job['session'] != session:
            raise ValueError('실험이 만료되었거나 존재하지 않습니다. 다시 실행해 주세요.')
        if op == 'cancel':
            if job['status'] in ('queued', 'running'):
                job['status'] = 'cancelled'
                job['updated'] = time.monotonic()
                process = job.get('process')
                if process:
                    stop_process(process)
        elif op != 'poll':
            raise ValueError('지원하지 않는 실험 동작입니다.')
        return public_job(job)


@app.post('/api/docking')
async def docking(request: Request):
    token = os.environ.get('DOCKING_SERVICE_TOKEN', '')
    if token and not hmac.compare_digest(request.headers.get('X-App-Token', ''), token):
        return JSONResponse({'error': '토큰이 올바르지 않습니다.'}, status_code=401)
    # Streaming bound works even when the caller omits Content-Length.
    raw = bytearray()
    async for chunk in request.stream():
        raw.extend(chunk)
        if len(raw) > MAX_BODY:
            return JSONResponse({'error': '구조 파일이 너무 큽니다.'}, status_code=413)
    try:
        body = json.loads(raw)
        if not isinstance(body, dict):
            raise ValueError('잘못된 요청입니다.')
        return await asyncio.to_thread(dispatch, body)
    except ValueError as error:
        return JSONResponse({'error': str(error)[:300]}, status_code=400)
    except httpx.RequestError:
        logging.exception('Molecular structure download failed')
        return JSONResponse({'error': '외부 분자 데이터베이스에 연결하지 못했어요. 잠시 후 후보 불러오기를 다시 시도해 주세요.'}, status_code=502)
    except Exception:
        logging.exception('Molecular request processing failed')
        return JSONResponse({'error': '분자 구조 처리에 실패했어요. 상세 원인을 계산 서버 로그에 기록했습니다.'}, status_code=502)
