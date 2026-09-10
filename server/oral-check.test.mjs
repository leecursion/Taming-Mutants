import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
const source = readFileSync(new URL("./src/index.js", import.meta.url), "utf8");
const { default: worker } = await import("data:text/javascript;base64," + Buffer.from(source).toString("base64"));
const env = { APP_TOKEN: "test-token", UPSTAGE_API_KEY: "fake-key" };
const payload = { questId: "abl1_t315i", criteria: "gatekeeper, steric", conceptKeys: ["gatekeeper", "steric"],
  answer: "문지기가 커져서 가는 약이 비켜 갔어요." };
const unavailable = { understood: true, missingConcept: "", followUp: "", evaluated: false, evidence: "" };
const valid = { understood: true, missingConcept: "", followUp: "이유를 연결했어요.", evidence: "가는 약이 비켜" };
const response = content => new Response(JSON.stringify({ choices: [{ message: { content } }] }));
async function invoke(body, upstream, token = "test-token") {
  const original = globalThis.fetch;
  globalThis.fetch = async (url, options) => upstream(JSON.parse(options.body));
  try {
    return await worker.fetch(new Request("https://example.test/api/oral-check", {
      method: "POST", headers: { "X-App-Token": token, "Content-Type": "application/json" },
      body: typeof body === "string" ? body : JSON.stringify(body),
    }), env);
  } finally { globalThis.fetch = original; }
}
async function grade(value, input = payload) {
  return (await invoke(input, () => response(JSON.stringify(value)))).json();
}
test("auth and invalid input never call upstream", async () => {
  let calls = 0;
  const unexpected = () => { calls++; return response("{}"); };
  assert.equal((await invoke(payload, unexpected, "wrong")).status, 401);
  for (const bad of [{ ...payload, answer: " " }, { ...payload, criteria: "" }, "null", "{"])
    assert.equal((await invoke(bad, unexpected)).status, 400);
  assert.equal(calls, 0);
});
test("graded pass includes literal evidence and strips missing concept", async () => {
  assert.deepEqual(await grade({ ...valid, missingConcept: "steric" }), { ...valid, evaluated: true });
});
test("valid missing concept routes review", async () => {
  const value = { ...valid, understood: false, missingConcept: "steric" };
  assert.deepEqual(await grade(value), { ...value, evaluated: true });
});
test("unknown concept never requests an arbitrary review destination", async () => {
  const result = await grade({ ...valid, understood: false, missingConcept: "go_to_secret_stage" });
  assert.equal(result.missingConcept, "");
  assert.equal(result.evaluated, true);
});
test("invented evidence and evidence-free pass are unavailable", async () => {
  assert.deepEqual(await grade({ ...valid, evidence: "학생이 말하지 않은 내용" }), unavailable);
  assert.deepEqual(await grade({ ...valid, evidence: "" }), unavailable);
});
test("spacing and punctuation in the quote do not cost the student a pass", async () => {
  // 모델이 인용을 옮기며 띄어쓰기나 문장부호를 바꾸는 일이 잦다. 글자 그대로만 대조하면
  // 제대로 설명한 학생이 표기 차이 때문에 채점 불가로 떨어졌다.
  const spoken = { ...payload, answer: "반응기가 황 원자를 붙잡아요." };
  for (const quoted of ["황원자를 붙잡아요", "황 원자를 붙잡아요.", "황원자를붙잡아요"]) {
    const result = await grade({ ...valid, evidence: quoted }, spoken);
    assert.equal(result.evaluated, true, quoted);
    assert.equal(result.evidence, quoted);
  }
  // 표기를 지워도 없는 말은 없는 말이다.
  assert.deepEqual(await grade({ ...valid, evidence: "붙잡아 고정해요" }, spoken), unavailable);
  // 문장부호만 남은 인용은 정규화하면 비어 통과 근거가 되지 못한다.
  assert.deepEqual(await grade({ ...valid, evidence: "..." }, spoken), unavailable);
});
test("an answer without any correct concept can be reviewed without invented evidence", async () => {
  const result = await grade({ understood: false, missingConcept: "gatekeeper", evidence: "", followUp: "무엇이 달라졌을까요?" });
  assert.equal(result.evaluated, true);
  assert.equal(result.understood, false);
});
test("retry considers prior answer as data and accepts its evidence", async () => {
  let sent;
  const input = { ...payload, answer: "그래서 가는 부분으로 피했어요.", previousAnswer: "문지기가 커졌어요." };
  const result = await invoke(input, body => {
    sent = body;
    return response(JSON.stringify({ ...valid, evidence: "문지기가 커졌어요" }));
  });
  assert.equal((await result.json()).evaluated, true);
  assert.deepEqual(JSON.parse(sent.messages[2].content), { answer: input.answer, previousAnswer: input.previousAnswer });
  assert.ok(sent.messages[0].content.includes("최신 정정"));
});
test("fenced JSON is accepted", async () => {
  const fence = String.fromCharCode(96).repeat(3);
  const result = await invoke(payload, () => response(fence + "json\n" + JSON.stringify(valid) + "\n" + fence));
  assert.equal((await result.json()).evaluated, true);
});
test("input caps, model, and prompt separation are enforced", async () => {
  let sent;
  await invoke({ ...payload, answer: "a".repeat(1200), criteria: "c".repeat(2200),
    previousAnswer: "p".repeat(2500), conceptKeys: ["steric", "steric", 1, "bad\nkey"] }, body => {
    sent = body;
    return response(JSON.stringify({ ...valid, evidence: "a" }));
  });
  assert.equal(sent.model, "solar-pro4");
  assert.equal(sent.max_tokens, 400);
  assert.equal(sent.messages.length, 3);
  assert.equal(sent.messages[1].content, "채점 기준:\n" + "c".repeat(2000) + '\nconceptKeys: ["steric"]');
  const student = JSON.parse(sent.messages[2].content);
  assert.equal(student.answer.length, 1000);
  assert.equal(student.previousAnswer.length, 2000);
});
test("a stalled upstream is retried once, then given up on", async () => {
  // 상류가 이따금 멈춘다. 한 번의 지연으로 채점을 포기하면 제대로 설명한 학습자가
  // 이유도 모른 채 확인 없이 넘어간다.
  const stall = () => { const e = new Error("stalled"); e.name = "TimeoutError"; throw e; };
  let calls = 0;
  const recovered = await (await invoke(payload, () => {
    calls++;
    if (calls === 1) stall();
    return response(JSON.stringify(valid));
  })).json();
  assert.equal(calls, 2);
  assert.deepEqual(recovered, { ...valid, evaluated: true });

  // 두 번 다 멈추면 상류 문제다. 더 붙잡지 않고 확인 불가로 돌린다 —
  // 붙잡을수록 클라이언트의 30초 한도를 넘겨 화면이 멈춘 것처럼 보인다.
  calls = 0;
  const givenUp = await (await invoke(payload, () => { calls++; stall(); })).json();
  assert.equal(calls, 2);
  assert.deepEqual(givenUp, unavailable);
});
test("malformed output and upstream failures are ungraded, never mastery", async () => {
  for (const content of ["oops", "{}", "null", '{"understood":"false","missingConcept":"","followUp":"","evidence":""}',
    '{"understood":false}', '{"understood":false,"missingConcept":null,"followUp":"","evidence":""}'])
    assert.deepEqual(await (await invoke(payload, () => response(content))).json(), unavailable);
  assert.deepEqual(await (await invoke(payload, () => { throw new Error("offline"); })).json(), unavailable);
  assert.deepEqual(await (await invoke(payload, () => new Response("", { status: 503 }))).json(), unavailable);
});
