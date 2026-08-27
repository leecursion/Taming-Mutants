/**
 * 돌연변이 길들이기 — LLM 프록시 (Cloudflare Workers)
 *
 * Unity 클라이언트는 이 서버만 부른다. API 키는 여기(Workers secret)에만 있고
 * 빌드에는 이 서버의 URL과 공유 토큰만 들어간다.
 *
 *   Unity -> 이 Worker -> Upstage / OpenAI
 *
 * 라우트:
 *   POST /api/co-scientist  { sessionId, userMessage, context, questId, stage } -> { reply }
 *   POST /api/stt           multipart(file=speech.wav)                          -> { text }
 *   POST /api/tts           { model, voice, input, response_format, ... }       -> audio/wav
 */

/**
 * 시스템 프롬프트는 서버가 쥔다.
 *
 * 클라이언트에 두면 씬 파일과 빌드에 그대로 박혀서, 게임 설계와 프롬프트를
 * 통째로 꺼내 볼 수 있다. 여기로 옮기면 키와 함께 감춰지고, 문구를 고칠 때
 * 빌드를 다시 만들 필요도 없다.
 */
const SYSTEM_PROMPT = `당신은 VR 과학 교육 게임 '돌연변이 길들이기'의 AI 도우미입니다.
배경: 플레이어는 학교 과학실에 새로 온 신입 연구원입니다. 세포 안에서 이상한 신호가 감지되어,
그 원인이 되는 단백질을 함께 조사하고 문제를 해결하는 탐정 놀이 같은 퀘스트를 진행합니다.
플레이어는 중학생입니다. 반드시 이 눈높이에 맞춰 설명하세요.

규칙:
- 반드시 한국어로, 친근하면서도 정중한 존댓말(해요체)로 답합니다. 반말은 절대 쓰지 않습니다.
- 어려운 전문 용어(GTP, 알로스테릭, 가수분해 등)는 되도록 쓰지 말고, 꼭 필요하면 '이건 ~라는 뜻이에요'처럼 쉬운 말로 바로 풀어줍니다.
- 스위치, 열쇠와 자물쇠, 안테나 같은 일상적인 비유를 적극 활용합니다.
- 말풍선에 들어가야 하므로 2~3문장, 200자 이내로 짧게 답합니다.
- 마크다운(**, #, 목록 기호)을 쓰지 않습니다. 평문으로만 씁니다.
- 함께 주어지는 '현재 상황'을 벗어난 단계를 미리 설명하지 않습니다.
- 정답을 통째로 알려주지 말고, 플레이어가 스스로 찾도록 한 걸음만 이끕니다.
- 확실하지 않은 수치나 사실은 지어내지 말고 모른다고 말합니다.`;

// 모델은 서버가 정한다. 클라이언트가 보낸 model 값은 신뢰하지 않는다 —
// URL이 알려졌을 때 비싼 모델을 대신 호출당하는 것을 막는다.
const CHAT_MODEL = "solar-pro4";
const STT_MODEL = "whisper-1";
const TTS_MODEL = "gpt-4o-mini-tts";

// 한 요청에 실릴 수 있는 상한. 요금이 무한정 늘어나지 않게 하는 안전장치다.
const MAX_USER_MESSAGE = 1000;
const MAX_CONTEXT = 4000;
const MAX_TTS_INPUT = 500;
const MAX_AUDIO_BYTES = 8 * 1024 * 1024;

const TTS_VOICES = new Set([
  "alloy", "ash", "ballad", "coral", "echo", "fable", "onyx", "nova", "sage", "shimmer", "verse",
]);

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method !== "POST") {
      return json({ error: "POST만 받습니다." }, 405);
    }

    // 공유 토큰 검사. 키를 대신하는 인증이 아니라, URL이 알려졌을 때
    // 아무나 바로 호출하지 못하게 막는 최소한의 문턱이다.
    if (env.APP_TOKEN && request.headers.get("X-App-Token") !== env.APP_TOKEN) {
      return json({ error: "토큰이 올바르지 않습니다." }, 401);
    }

    try {
      switch (url.pathname) {
        case "/api/co-scientist": return await handleChat(request, env);
        case "/api/stt":          return await handleStt(request, env);
        case "/api/tts":          return await handleTts(request, env);
        default:                  return json({ error: "없는 경로입니다." }, 404);
      }
    } catch (e) {
      // 예외 내용을 그대로 흘리면 상류 응답에 키가 섞여 나올 수 있다. 로그에만 남긴다.
      console.error(url.pathname, e);
      return json({ error: "서버 내부 오류" }, 500);
    }
  },
};

/** F-06 AI Co-Scientist — Upstage Solar 중계. */
async function handleChat(request, env) {
  const body = await request.json();

  const userMessage = clip(body.userMessage, MAX_USER_MESSAGE);
  if (!userMessage) return json({ error: "userMessage가 비어 있습니다." }, 400);

  const messages = [{ role: "system", content: SYSTEM_PROMPT }];

  // 상황 설명은 사용자 발화와 섞지 않고 별도 system 메시지로 넣는다.
  // 사용자 질문에 붙여 보내면 모델이 그것까지 "사용자가 한 말"로 취급해
  // 배경 정보를 되읊는 답이 나온다.
  const context = clip(body.context, MAX_CONTEXT);
  if (context) messages.push({ role: "system", content: "현재 상황:\n" + context });

  messages.push({ role: "user", content: userMessage });

  const upstream = await fetch("https://api.upstage.ai/v1/chat/completions", {
    method: "POST",
    headers: {
      "Authorization": `Bearer ${env.UPSTAGE_API_KEY}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      model: CHAT_MODEL,
      messages,
      max_tokens: 1024,
      stream: false,
    }),
  });

  if (!upstream.ok) {
    console.error("upstage", upstream.status, await upstream.text());
    return json({ error: `LLM 오류 (${upstream.status})` }, 502);
  }

  const data = await upstream.json();
  const reply = data?.choices?.[0]?.message?.content?.trim();
  if (!reply) return json({ error: "빈 응답을 받았습니다." }, 502);

  // Unity의 AICoScientistClient가 기대하는 형태로 맞춰 돌려준다.
  return json({ reply, quizChoices: [], correctChoiceIndex: -1 });
}

/** 음성 인식 — 받은 wav를 Whisper로 중계한다. */
async function handleStt(request, env) {
  const incoming = await request.formData();
  const file = incoming.get("file");

  if (!file || typeof file === "string") {
    return json({ error: "file 파트가 없습니다." }, 400);
  }
  if (file.size > MAX_AUDIO_BYTES) {
    return json({ error: "녹음이 너무 깁니다." }, 413);
  }

  const form = new FormData();
  form.append("file", file, "speech.wav");
  form.append("model", STT_MODEL);
  form.append("response_format", "json");
  form.append("language", "ko");

  const upstream = await fetch("https://api.openai.com/v1/audio/transcriptions", {
    method: "POST",
    headers: { "Authorization": `Bearer ${env.OPENAI_API_KEY}` },
    body: form,
  });

  if (!upstream.ok) {
    console.error("whisper", upstream.status, await upstream.text());
    return json({ error: `음성 인식 오류 (${upstream.status})` }, 502);
  }

  // OpenAI가 주는 { "text": "..." } 형태 그대로 돌려준다 —
  // 클라이언트의 파싱 코드를 고치지 않아도 되도록.
  return json(await upstream.json());
}

/** 음성 합성 — 문장을 받아 wav 바이트를 그대로 흘려보낸다. */
async function handleTts(request, env) {
  const body = await request.json();

  const input = clip(body.input, MAX_TTS_INPUT);
  if (!input) return json({ error: "input이 비어 있습니다." }, 400);

  const voice = TTS_VOICES.has(String(body.voice || "").toLowerCase())
    ? String(body.voice).toLowerCase()
    : "coral";

  const payload = {
    model: TTS_MODEL,
    voice,
    input,
    // wav로 받아야 Unity의 WavCodec이 그대로 읽는다. mp3는 플랫폼별 디코딩 지원이 갈린다.
    response_format: "wav",
  };

  // 말투 지시와 속도는 클라이언트가 보낸 값을 살린다. 인스펙터에서 톤을 조절하고
  // 서버를 다시 배포하지 않아도 되도록.
  if (typeof body.instructions === "string" && body.instructions.trim()) {
    payload.instructions = clip(body.instructions, 1000);
  }
  const speed = Number(body.speed);
  if (Number.isFinite(speed) && speed >= 0.25 && speed <= 4) payload.speed = speed;

  const upstream = await fetch("https://api.openai.com/v1/audio/speech", {
    method: "POST",
    headers: {
      "Authorization": `Bearer ${env.OPENAI_API_KEY}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });

  if (!upstream.ok) {
    console.error("tts", upstream.status, await upstream.text());
    return json({ error: `음성 합성 오류 (${upstream.status})` }, 502);
  }

  return new Response(upstream.body, {
    status: 200,
    headers: { "Content-Type": "audio/wav" },
  });
}

function clip(value, limit) {
  if (typeof value !== "string") return "";
  const trimmed = value.trim();
  return trimmed.length > limit ? trimmed.slice(0, limit) : trimmed;
}

function json(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8" },
  });
}
