"""One bounded process per job; never shell-interpolate molecular input."""
import json
from pathlib import Path
import sys
from chemistry import run_docking

if __name__ == '__main__':
    directory = Path(sys.argv[1])
    try:
        result = run_docking(json.loads((directory / 'input.json').read_text(encoding='utf-8')), directory)
    except Exception as error:
        # Detailed preparation diagnostics stay in the local job log.
        import traceback
        traceback.print_exc()
        result = {'error': str(error)[:300] if isinstance(error, ValueError) else
                  '분자 구조를 계산용으로 처리하지 못했어요. 상세 원인을 계산 서버 로그에 기록했습니다.'}
    (directory / 'result.json').write_text(json.dumps(result, allow_nan=False), encoding='utf-8')
