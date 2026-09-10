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
