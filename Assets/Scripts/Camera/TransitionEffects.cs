using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 설계서의 트랜지션 VFX/SFX를 <see cref="CameraTransitionDirector"/>에 얹는다.
///
///   시공간 파티클 워프  — 이동하는 동안 파티클을 재생하고 진행률에 맞춰 세기를 올린다
///   세포막 통과 모션 블러 — 통과 지점에서 블러를 한 번 크게 밀어 올린다
///   음향                — 저주파 울림 -> 하이피치 전자음 (줌인) / 반대 (줌아웃)
///
/// 디렉터와 분리해 둔 이유: 연출은 자주 바뀌고 카메라 이동은 잘 바뀌지 않는다.
/// 한 클래스에 같이 두면 파티클 하나 교체하려다 이동 코드를 건드리게 된다.
/// 이 컴포넌트를 통째로 꺼도 카메라는 정상 동작한다.
/// </summary>
[RequireComponent(typeof(CameraTransitionDirector))]
public class TransitionEffects : MonoBehaviour
{
    [Serializable]
    public class StyleSound
    {
        public CameraMotionStyle style = CameraMotionStyle.DollyIn;
        public AudioClip clip;
        [Tooltip("시작 피치. 저주파 울림에서 출발하려면 1보다 작게.")]
        public float pitchFrom = 0.55f;
        [Tooltip("끝 피치. 미시 세계로 들어갈수록 높아진다.")]
        public float pitchTo = 1.7f;
    }

    [Header("파티클 워프")]
    [Tooltip("이동 중 재생할 파티클. 보통 카메라 자식으로 두고 앞으로 흐르게 만든다.")]
    public ParticleSystem warpParticles;
    [Tooltip("진행률 최고점에서의 방출량 배수")]
    public float warpEmissionScale = 60f;

    [Header("모션 블러 (URP)")]
    [Tooltip("Motion Blur / Vignette 오버라이드가 들어 있는 Global Volume")]
    public Volume postProcessVolume;
    [Tooltip("세포막을 통과하는 지점(0~1). 여기서 블러가 가장 세다.")]
    [Range(0f, 1f)] public float blurPeakAt = 0.55f;
    [Range(0f, 1f)] public float maxMotionBlur = 0.85f;
    [Range(0f, 1f)] public float maxVignette = 0.5f;

    [Header("음향")]
    public AudioSource audioSource;
    public StyleSound[] sounds = Array.Empty<StyleSound>();

    private CameraTransitionDirector _director;
    private MotionBlur _motionBlur;
    private Vignette _vignette;
    private StyleSound _activeSound;
    private float _baseMotionBlur;
    private float _baseVignette;
    private bool _warnedAboutOverrides;

    private void Awake()
    {
        _director = GetComponent<CameraTransitionDirector>();
        ResolvePostProcessOverrides();
    }

    private void OnEnable()
    {
        _director.OnTransitionStarted += HandleStarted;
        _director.OnTransitionProgress += HandleProgress;
        _director.OnTransitionCompleted += HandleCompleted;
    }

    private void OnDisable()
    {
        _director.OnTransitionStarted -= HandleStarted;
        _director.OnTransitionProgress -= HandleProgress;
        _director.OnTransitionCompleted -= HandleCompleted;

        RestorePostProcess();
    }

    /// <summary>
    /// Volume.profile은 실행 중 접근하면 사본을 만들어 준다(sharedProfile과 달리 에셋을 더럽히지 않는다).
    /// 오버라이드가 프로파일에 없으면 아무것도 하지 않는다 — 없는 걸 런타임에 추가하면
    /// 프로젝트마다 결과가 달라져 원인을 찾기 어려워진다. 대신 한 번만 경고한다.
    /// </summary>
    private void ResolvePostProcessOverrides()
    {
        if (postProcessVolume == null || postProcessVolume.profile == null) return;

        postProcessVolume.profile.TryGet(out _motionBlur);
        postProcessVolume.profile.TryGet(out _vignette);

        if (_motionBlur != null) _baseMotionBlur = _motionBlur.intensity.value;
        if (_vignette != null) _baseVignette = _vignette.intensity.value;
    }

    private void HandleStarted(QuestLevel from, QuestLevel to, LevelTransition settings)
    {
        _activeSound = FindSound(settings.style);
        PlaySound(_activeSound);

        if (warpParticles != null) warpParticles.Play();

        if ((_motionBlur == null || _vignette == null) && postProcessVolume != null && !_warnedAboutOverrides)
        {
            _warnedAboutOverrides = true;
            Debug.LogWarning("[TransitionEffects] Volume 프로파일에 Motion Blur 또는 Vignette 오버라이드가 없어 " +
                             "블러 연출을 건너뜁니다. Global Volume의 프로파일에 두 오버라이드를 추가하세요.", this);
        }
    }

    private void HandleProgress(float t)
    {
        // 통과 지점에서 최대가 되고 양끝에서 0이 되는 종 모양.
        // 선형으로 올리면 도착한 뒤에도 화면이 뿌옇게 남는다.
        float peak = BellCurve(t, blurPeakAt);

        if (_motionBlur != null)
            _motionBlur.intensity.value = Mathf.Max(_baseMotionBlur, peak * maxMotionBlur);

        if (_vignette != null)
            _vignette.intensity.value = Mathf.Max(_baseVignette, peak * maxVignette);

        if (warpParticles != null)
        {
            ParticleSystem.EmissionModule emission = warpParticles.emission;
            emission.rateOverTimeMultiplier = peak * warpEmissionScale;
        }

        if (_activeSound != null && audioSource != null)
            audioSource.pitch = Mathf.Lerp(_activeSound.pitchFrom, _activeSound.pitchTo, t);
    }

    private void HandleCompleted(QuestLevel level)
    {
        RestorePostProcess();

        if (warpParticles != null) warpParticles.Stop();
        _activeSound = null;
    }

    private void RestorePostProcess()
    {
        if (_motionBlur != null) _motionBlur.intensity.value = _baseMotionBlur;
        if (_vignette != null) _vignette.intensity.value = _baseVignette;

        if (audioSource != null) audioSource.pitch = 1f;
    }

    /// <summary>peak 지점에서 1, 양끝에서 0이 되는 곡선.</summary>
    private static float BellCurve(float t, float peak)
    {
        peak = Mathf.Clamp(peak, 0.01f, 0.99f);

        float k = t < peak
            ? t / peak
            : 1f - (t - peak) / (1f - peak);

        return Mathf.Clamp01(k * k * (3f - 2f * k)); // smoothstep으로 모서리를 없앤다
    }

    private StyleSound FindSound(CameraMotionStyle style)
    {
        if (sounds == null) return null;

        foreach (StyleSound sound in sounds)
            if (sound != null && sound.style == style && sound.clip != null) return sound;

        return null;
    }

    private void PlaySound(StyleSound sound)
    {
        if (sound == null || audioSource == null) return;

        audioSource.pitch = sound.pitchFrom;
        audioSource.PlayOneShot(sound.clip);
    }
}
