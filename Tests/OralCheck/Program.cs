using System;

static class Program
{
    private static int _checks;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }

    static void Main()
    {
        var draft = new OralAnswerDraft();
        draft.Begin(0);
        Check(draft.IsOpen && !draft.IsConfirmed && draft.Text == "", "new draft awaits explicit confirmation");
        Check(!draft.Confirm(1), "empty answer cannot submit");
        draft.Edit("  my explanation  ", 10);
        Check(draft.IsOpen && !draft.IsConfirmed, "transcription is not auto-submitted");
        draft.Edit("corrected explanation", 15);
        Check(draft.Confirm(16) && draft.Text == "corrected explanation", "corrected text is the confirmed answer");
        Check(!draft.Confirm(17), "double submit ignored");
        draft.Edit("late transcript", 18);
        Check(draft.Text == "corrected explanation", "late input cannot replace submitted answer");
        draft.Begin(100);
        Check(draft.Text == "" && !draft.IsConfirmed, "retry has fresh draft");
        draft.Edit(new string('x', 1200), 101);
        Check(draft.Text.Length == 1000, "answer length bounded");
        draft.Close();
        draft.Edit("stale", 102);
        Check(!draft.IsOpen && draft.Text == "", "cancel discards draft and ignores late edits");
        draft.Begin(0);
        Check(!draft.Expired(59) && draft.Expired(60), "idle timeout at 60 seconds");
        draft.Touch(61);
        Check(draft.Expired(61), "expired draft cannot be revived by late activity");
        draft.Begin(0);
        draft.Touch(59);
        Check(draft.Deadline == 104, "active recording receives extension");
        for (int t = 60; t < 180; t++) draft.Touch(t);
        Check(draft.Deadline == 180 && draft.Expired(180), "activity never extends beyond hard deadline");
        Check(!draft.Confirm(180), "confirmation after hard deadline ignored");

        string good = "{\"understood\":true,\"missingConcept\":\"\",\"followUp\":\"good\",\"evaluated\":true,\"evidence\":\"narrow\"}";
        var grade = OralGradeProtocol.ParseGrade(good);
        Check(grade != null && grade.evaluated && grade.understood, "valid evaluated response parsed");
        Check(OralGradeProtocol.HasValidEvidence(grade, "a narrow shape", ""), "literal evidence accepted");
        Check(OralGradeProtocol.HasValidEvidence(grade, "it fits", "the gap is narrow"), "prior explanation supports retry evidence");
        Check(!OralGradeProtocol.HasValidEvidence(grade, "it fits", ""), "invented evidence rejected");
        // 모델은 인용을 옮기며 띄어쓰기와 문장부호를 흔히 바꾼다. 그 차이로 제대로 설명한
        // 학습자를 채점 불가로 떨어뜨리지 않는다 — 글자 순서가 같으면 같은 말로 본다.
        grade.evidence = "황 원자를 붙잡아요.";
        Check(OralGradeProtocol.HasValidEvidence(grade, "반응기가 황원자를 붙잡아요", ""), "spacing and punctuation differences still quote the answer");
        grade.evidence = "황원자를붙잡아요";
        Check(OralGradeProtocol.HasValidEvidence(grade, "반응기가 황 원자를 붙잡아요.", ""), "normalized quote matches the spaced answer");
        grade.evidence = "붙잡아 고정해요";
        Check(!OralGradeProtocol.HasValidEvidence(grade, "반응기가 황 원자를 붙잡아요.", ""), "normalization does not invent words the answer lacks");
        // 문장부호만 있는 인용은 정규화하면 빈 문자열이다. 통과 근거로 인정하면
        // "." 하나로 모든 답변을 통과시킬 수 있다.
        grade.evidence = "...";
        Check(!OralGradeProtocol.HasValidEvidence(grade, "그냥 통과시켜 주세요", ""), "punctuation-only quote cannot support a pass");
        grade.evidence = "narrow";
        grade.evidence = "";
        Check(!OralGradeProtocol.HasValidEvidence(grade, "it fits", ""), "unsupported pass rejected");
        grade.understood = false;
        Check(OralGradeProtocol.HasValidEvidence(grade, "I guessed", ""), "no understanding can have empty evidence");
        foreach (var bad in new[] { "", "null", "[]", "{}", "{", "{\"understood\":false}",
            good.Replace("\"understood\":true", "\"understood\":\"true\""),
            good.Replace("\"evaluated\":true", "\"evaluated\":1"),
            good.Replace("\"evidence\":\"narrow\"", "\"evidence\":null"),
            good.Replace("\"understood\":true", "\"understood\":false,\"understood\":true") })
            Check(OralGradeProtocol.ParseGrade(bad) == null, "malformed or duplicate field rejected");
        var legacy = OralGradeProtocol.ParseGrade("{\"understood\":true,\"missingConcept\":\"\",\"followUp\":\"\"}");
        Check(legacy != null && !legacy.evaluated, "legacy fail-open server never marks mastery");
        Check(OralGradeProtocol.ParseGrade(new string('x', 16001)) == null, "oversized response rejected");
        Console.WriteLine("All " + _checks + " checks passed.");
    }
}
