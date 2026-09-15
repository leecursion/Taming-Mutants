# LLM 프록시 서버

Unity 빌드에 API 키를 넣지 않기 위한 중계 서버다. 키는 Cloudflare에만 저장되고,
빌드에는 이 서버의 URL과 공유 토큰만 들어간다.

```
Unity  ──►  이 Worker  ──►  Upstage Solar / OpenAI
          (키 보관, 시스템 프롬프트 주입)
```

> 아래 명령은 **Windows PowerShell** 기준이다. PowerShell 5.1에는 `&&`가 없으므로
> 명령을 한 줄로 이어 붙이지 말고 한 줄씩 실행한다. 이어 붙여야 하면 `;`를 쓴다.

## 배포

```powershell
cd server
npm install
npx wrangler login
```

키를 등록한다. 입력한 값은 Cloudflare에만 저장되고 저장소에는 남지 않는다.

```powershell
npx wrangler secret put UPSTAGE_API_KEY
npx wrangler secret put OPENAI_API_KEY
npx wrangler secret put APP_TOKEN
```

`APP_TOKEN`은 아무 긴 무작위 문자열이면 된다. 만들기 귀찮으면:

```powershell
[guid]::NewGuid().ToString('N')
```

배포한다.

```powershell
npx wrangler deploy
```

끝나면 `https://taming-mutants-proxy.<계정>.workers.dev` 형태의 URL이 출력된다.

도킹 계산 서비스(`docking/`)는 같은 `wrangler deploy`로 Cloudflare Containers에 함께 올라간다.
Workers Paid 플랜과 실행 중인 로컬 Docker가 필요하다. 자세한 내용은
`docs/AI-Docking-Experiments.md`의 "서버 배포"를 본다. Worker만 다시 올릴 때는
`npx wrangler deploy --containers-rollout=none`.

## Unity 배선

`Lab_Desktop` 씬에서:

| 컴포넌트 | 할 일 |
|---|---|
| `GameFlow`의 `SolarChatClient` | 제거하고 `AICoScientistClient`를 대신 붙인다 |
| `AICoScientistClient` | `backendEndpoint` = `<URL>/api/co-scientist`, `proxyToken` = APP_TOKEN |
| `OpenAiWhisperClient` | `proxyEndpoint` = `<URL>/api/stt`, `proxyToken` = APP_TOKEN |
| `OpenAiTtsClient` | `proxyEndpoint` = `<URL>/api/tts`, `proxyToken` = APP_TOKEN |

`AIAssistantBrain`은 `AIChatBackend` 추상 타입만 알고 있어서 컴포넌트를 갈아끼워도
코드 수정이 필요 없다. 음성 쪽은 `proxyEndpoint`가 채워지면 `apiKey`와
`OPENAI_API_KEY` 환경변수를 아예 보지 않는다.

**빌드 전 확인**: 세 컴포넌트의 `apiKey` 칸이 모두 비어 있어야 한다. 값이 남아 있으면
씬 파일과 빌드에 그대로 실려 나간다.

## 로컬 테스트

`server/.dev.vars` 파일을 만든다. (`.gitignore`에 들어 있어 커밋되지 않는다.)

```powershell
@'
UPSTAGE_API_KEY=...
OPENAI_API_KEY=...
APP_TOKEN=devtoken
'@ | Out-File -FilePath .dev.vars -Encoding utf8
```

로컬 서버를 띄운다.

```powershell
npx wrangler dev
```

다른 터미널에서 호출해 본다. **`curl`이 아니라 `curl.exe`를 써야 한다** —
PowerShell에서 `curl`은 `Invoke-WebRequest`의 별칭이라 `-X`, `-H`, `-d`를 알아듣지 못한다.

```powershell
curl.exe -X POST http://localhost:8787/api/co-scientist `
  -H "Content-Type: application/json" `
  -H "X-App-Token: devtoken" `
  -d '{\"userMessage\":\"이 단백질이 뭐야?\",\"context\":\"1단계: EGFR 관찰 중\"}'
```

따옴표 escape가 번거로우면 PowerShell 네이티브로 보내도 된다.

```powershell
$body = @{ userMessage = "이 단백질이 뭐야?"; context = "1단계: EGFR 관찰 중" } | ConvertTo-Json
Invoke-RestMethod -Uri http://localhost:8787/api/co-scientist -Method Post `
  -ContentType "application/json; charset=utf-8" `
  -Headers @{ "X-App-Token" = "devtoken" } `
  -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
```

배포된 서버의 로그를 실시간으로 보려면:

```powershell
npx wrangler tail
```

## 운영

Worker와 도킹 컨테이너는 한 덩어리로 배포·관리한다. 아래 명령은 모두 `server/`에서 실행한다.

```
Unity 실행파일 ──► Worker (taming-mutants-proxy) ──┬─► Upstage Solar / OpenAI
                                                    └─► 도킹 컨테이너 (필요할 때만 켜짐)
```

### 코드를 고쳤을 때

```powershell
npm test                                        # 테스트 통과 확인
npx wrangler deploy                             # Worker + 컨테이너 이미지 빌드·배포 (Docker Desktop 실행 중이어야 함)
npx wrangler deploy --containers-rollout=none   # src/*.js만 고쳤을 때 — Docker 불필요, 수 초
```

- `docking/*.py`나 Dockerfile을 건드렸으면 첫 번째, Worker 코드·프롬프트만 고쳤으면 두 번째.
- Docker Desktop은 배포할 때만 켜면 된다.
- 배포가 잘못됐으면 `npx wrangler rollback`으로 직전 버전으로 되돌린다
  (`npx wrangler versions list`로 목록 확인).

### 살아 있는지 확인 / 로그

```powershell
npx wrangler tail                        # 실시간 요청 로그. 오류 본문은 클라이언트에 안 보내니 여기서 본다
npx wrangler containers list             # 컨테이너 앱 상태
npx wrangler containers info <app-id>    # 인스턴스 상태·이미지
```

대시보드(dash.cloudflare.com → Workers & Pages → `taming-mutants-proxy`)의 **Containers** 탭에서
컨테이너 인스턴스 상태와 stdout 로그(uvicorn/Vina 출력)를, **Metrics** 탭에서 요청 수·오류율을 본다.

도킹이 안 될 때:

| 증상 | 뜻 |
|---|---|
| `도킹 계산 서버가 연결되지 않았어요` (503) | Worker에 `DOCKING` 바인딩이 없다 — `wrangler.toml`이 잘못됐거나 컨테이너를 한 번도 올리지 않은 채 `--containers-rollout=none`으로 배포한 경우 |
| `도킹 계산 서버에 연결하지 못했어요` (502) | 컨테이너가 안 뜬다 — Containers 탭 로그 확인. 이미지 문제면 `wrangler deploy` 다시 |
| 첫 요청만 10~20초 느림 | 정상. 15분 유휴 후 잠들었다 깨는 시간 |
| 401 | 클라이언트 `APP_TOKEN` 불일치 |

### Secrets

```powershell
npx wrangler secret list
npx wrangler secret put UPSTAGE_API_KEY    # 값 교체 — 재배포 없이 즉시 반영
```

사용하는 secret: `APP_TOKEN`, `UPSTAGE_API_KEY`, `OPENAI_API_KEY`, `DOCKING_SERVICE_TOKEN`.
`APP_TOKEN`은 배포된 실행파일에 박혀 있으므로 바꾸는 순간 이미 나간 빌드의 AI 기능이 끊긴다 —
새 빌드를 배포할 때만 함께 바꾼다.

### 비용

- Workers Paid(월 $5)에 컨테이너 vCPU 375분·메모리 25 GiB-시간이 포함된다. 2 vCPU 컨테이너가
  **실제 깨어 있는 시간**만 세므로 시연 며칠이면 포함분 안팎, 넘어도 시간당 $0.2 수준.
  대시보드 → Billing → Usage에서 확인.
- 도킹을 내리려면 `wrangler.toml`에서 `[[containers]]`·`[[durable_objects.bindings]]`·`[[migrations]]`
  블록을 지우고 `wrangler deploy`한다. 컨테이너 과금이 끝나고 Worker(대화·음성)는 그대로 동작한다.
  다시 켜려면 블록을 되살려 재배포한다.

### 로컬에서 테스트

```powershell
npx wrangler dev                          # Worker만 로컬(8787), .dev.vars의 키 사용
.\Tests\Docking\Start-LocalDocking.ps1    # 도킹 서비스 + 로컬 프록시 (Editor 전용)
```

로컬에는 `DOCKING` 바인딩이 없어서 `DOCKING_SERVICE_URL` 경로로 자동 전환된다.
배포 코드와 로컬 코드는 같은 파일이다.

## 설계 메모

- **모델은 서버가 정한다.** 클라이언트가 보낸 `model` 값은 무시한다. URL이 알려졌을 때
  비싼 모델을 대신 호출당하지 않게 하기 위해서다.
- **시스템 프롬프트도 서버에 있다.** 클라이언트에 두면 씬 파일과 빌드에서 그대로
  꺼내 볼 수 있다. 여기 있으면 문구를 고칠 때 빌드를 다시 만들 필요도 없다.
- **오류 본문은 클라이언트로 흘리지 않는다.** 상류 응답에 키가 섞여 나올 수 있어
  상태 코드만 넘기고 자세한 내용은 `wrangler tail`로 본다.
- 공유 토큰은 인증이 아니라 문턱이다. 빌드에서 추출은 가능하지만, 그때도 유출되는 것은
  토큰뿐이라 서버에서 값만 바꾸면 차단된다 — API 키를 회수하는 것과는 다르다.
