using System;
using UnityEngine;

/// <summary>
/// 사용자의 말을 글로 옮기는 백엔드의 공통 계약.
///
/// <see cref="AIChatBackend"/>와 같은 이유로 추상 클래스로 둔다 — 제공자를 바꿀 때
/// 컴포넌트만 갈아끼우면 되고, 이걸 쓰는 쪽(<see cref="VoiceInputController"/>)은 고치지 않는다.
///
/// 백엔드가 없어도 게임은 끝까지 돌아가야 한다. <see cref="IsConfigured"/>가 false면
/// 마이크 버튼 자체를 띄우지 않는다 — 눌러도 아무 일 없는 버튼을 보여주는 것보다 낫다.
/// </summary>
public abstract class SpeechToTextBackend : MonoBehaviour
{
    /// <summary>말이 글로 옮겨졌을 때. 빈 문자열은 넘기지 않는다.</summary>
    public event Action<string> OnTranscribed;

    /// <summary>녹음이나 전송이 실패했을 때.</summary>
    public event Action<string> OnError;

    /// <summary>지금 녹음 중인지.</summary>
    public bool IsListening { get; protected set; }

    /// <summary>변환 요청을 보내고 답을 기다리는 중인지.</summary>
    public bool IsTranscribing { get; protected set; }

    /// <summary>
    /// 지금 마이크로 들어오는 소리 크기(0~1). 녹음 중이 아니면 0.
    ///
    /// 이 값이 있어야 "내 목소리가 들어가고 있는지"를 눈으로 확인할 수 있다.
    /// 없으면 마이크가 음소거이거나 엉뚱한 장치가 잡혀 있어도 똑같이 녹음 중으로 보이고,
    /// 변환 결과가 비어 돌아온 뒤에야 알게 된다.
    /// </summary>
    public float InputLevel { get; protected set; }

    /// <summary>녹음이 시작된 뒤 들어온 소리의 최대 크기(0~1). 통째로 조용했는지 판단하는 데 쓴다.</summary>
    public float PeakLevel { get; protected set; }

    /// <summary>실제로 쓰고 있는 마이크 이름. 장치가 여러 개일 때 어느 것이 잡혔는지 확인용.</summary>
    public virtual string ActiveDeviceName => null;

    /// <summary>실제로 쓸 수 있는 상태인지 (키와 마이크가 모두 있는지).</summary>
    public abstract bool IsConfigured { get; }

    /// <summary>녹음을 시작한다.</summary>
    public abstract void StartListening();

    /// <summary>녹음을 멈추고 변환을 요청한다. 결과는 <see cref="OnTranscribed"/>로 온다.</summary>
    public abstract void StopListening();

    /// <summary>녹음을 버린다. 변환 요청을 보내지 않는다.</summary>
    public abstract void Cancel();

    protected void RaiseTranscribed(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        OnTranscribed?.Invoke(text.Trim());
    }

    protected void RaiseError(string reason) => OnError?.Invoke(reason);
}

/// <summary>
/// 비서의 대사를 소리로 읽어주는 백엔드의 공통 계약.
///
/// <see cref="AIAssistantSpeechBubble"/>이 대사를 띄울 때마다 <see cref="Speak"/>를 부르고,
/// 소리가 끝날 때까지 말풍선을 띄워둔다. 백엔드가 없으면 예전처럼 글자 수로 유지 시간을
/// 계산하므로, 음성이 없다고 진행이 막히지는 않는다.
/// </summary>
public abstract class TextToSpeechBackend : MonoBehaviour
{
    /// <summary>합성이나 재생이 실패했을 때.</summary>
    public event Action<string> OnError;

    /// <summary>지금 소리를 내고 있거나 합성을 기다리는 중인지.</summary>
    public bool IsSpeaking { get; protected set; }

    /// <summary>실제로 쓸 수 있는 상태인지.</summary>
    public abstract bool IsConfigured { get; }

    /// <summary>
    /// 문장을 읽는다. 재생이 끝나면(또는 실패하면) <paramref name="onComplete"/>가 불린다.
    ///
    /// 실패해도 콜백을 불러야 한다. 성공했을 때만 부르면 말풍선이 소리를 기다리다
    /// 영영 넘어가지 않는다.
    /// </summary>
    public abstract void Speak(string text, Action onComplete = null);

    /// <summary>재생 중인 소리를 즉시 멈춘다. 대기 중인 콜백은 부르지 않는다.</summary>
    public abstract void Stop();

    /// <summary>
    /// 곧 읽을 문장을 미리 합성해 둔다. 재생은 하지 않는다.
    ///
    /// 긴 대사는 여러 말풍선으로 나뉘는데, 조각마다 그때 가서 합성하면 조각 사이에
    /// 요청 왕복만큼(1~2초) 침묵이 생긴다. 사람이 말하다 끊긴 것처럼 들려서
    /// 문장 하나하나는 자연스러워도 전체는 기계적으로 느껴진다.
    /// 미리 받아두면 앞 조각이 재생되는 동안 뒤 조각이 준비된다.
    ///
    /// 캐시가 없는 구현은 그냥 무시해도 된다.
    /// </summary>
    public virtual void Prewarm(string text) { }

    protected void RaiseError(string reason) => OnError?.Invoke(reason);
}
