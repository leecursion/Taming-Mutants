using System;

/// <summary>Unity 시간과 분리한 답변 확인 규칙. 음성 오인식을 학습 오답으로 세지 않는다.</summary>
public sealed class OralAnswerDraft
{
    public const int MaxLength = 1000;
    public bool IsOpen { get; private set; }
    public bool IsConfirmed { get; private set; }
    public string Text { get; private set; } = "";
    public int Revision { get; private set; }
    public float Deadline { get; private set; }
    private float _hardDeadline;

    public void Begin(float now)
    {
        Text = "";
        IsOpen = true;
        IsConfirmed = false;
        Deadline = now + 60f;
        _hardDeadline = now + 180f;
        Revision++;
    }

    public void Edit(string text, float now)
    {
        if (!IsOpen || now >= Deadline) return;
        Text = text ?? "";
        if (Text.Length > MaxLength) Text = Text.Substring(0, MaxLength);
        Revision++;
        Touch(now);
    }

    public void Touch(float now)
    {
        // 읽고 고치거나 녹음하는 학생의 시간을 보장하되 무한 대기는 하지 않는다.
        if (IsOpen && now < Deadline) Deadline = Math.Min(_hardDeadline, Math.Max(Deadline, now + 45f));
    }

    public bool Confirm(float now)
    {
        if (!IsOpen || now >= Deadline || string.IsNullOrWhiteSpace(Text)) return false;
        Text = Text.Trim();
        IsOpen = false;
        IsConfirmed = true;
        return true;
    }

    public bool Expired(float now) => IsOpen && now >= Deadline;
    public void Close() { IsOpen = false; IsConfirmed = false; Text = ""; Revision++; }
}

public enum OralCheckPhase { Hidden, Explaining, Answering, Grading, Reviewing, Completed }
public enum OralCheckOutcome { None, Understood, Improved, Explained, Skipped, Unavailable }
