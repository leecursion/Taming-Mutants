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

## 설계 메모

- **모델은 서버가 정한다.** 클라이언트가 보낸 `model` 값은 무시한다. URL이 알려졌을 때
  비싼 모델을 대신 호출당하지 않게 하기 위해서다.
- **시스템 프롬프트도 서버에 있다.** 클라이언트에 두면 씬 파일과 빌드에서 그대로
  꺼내 볼 수 있다. 여기 있으면 문구를 고칠 때 빌드를 다시 만들 필요도 없다.
- **오류 본문은 클라이언트로 흘리지 않는다.** 상류 응답에 키가 섞여 나올 수 있어
  상태 코드만 넘기고 자세한 내용은 `wrangler tail`로 본다.
- 공유 토큰은 인증이 아니라 문턱이다. 빌드에서 추출은 가능하지만, 그때도 유출되는 것은
  토큰뿐이라 서버에서 값만 바꾸면 차단된다 — API 키를 회수하는 것과는 다르다.
