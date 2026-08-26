using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>설계서 "카메라 설계 요약 표"의 모션 종류.</summary>
public enum CameraMotionStyle
{
    /// <summary>연출 없이 즉시 이동. 디버그와 초기 배치에 쓴다.</summary>
    Cut,

    /// <summary>Rapid Dolly-In — 가속하며 파고든다. 인체 → 세포막 통과 → DNA.</summary>
    DollyIn,

    /// <summary>Pan &amp; Focus Shift — 경유점을 따라 훑다가 초점을 옮긴다. DNA → 단백질.</summary>
    PanAndFocus,

    /// <summary>Micro Zoom-In — 표면을 뚫고 천천히 들어간다. 단백질 → 포켓 내부.</summary>
    MicroZoomIn,

    /// <summary>Spatial Zoom-Out — 빠르게 후퇴한다. 원자 → 연구실.</summary>
    SpatialZoomOut,
}

/// <summary>레벨 사이를 어떻게 건널지.</summary>
[Serializable]
public class LevelTransition
{
    public QuestLevel from;
    public QuestLevel to;
    public CameraMotionStyle style = CameraMotionStyle.DollyIn;
    [Tooltip("이동에 걸리는 시간(초)")]
    public float duration = 2.4f;

    [Tooltip("경유점. 직선으로 가면 안 되는 경로(Pan & Focus 등)에 지정한다. " +
             "비워두면 출발점과 도착점을 잇는 직선.")]
    public Transform via;

    [Tooltip("이동 중 시야각을 이만큼 더 벌린다. 화면 가장자리가 늘어나며 속도감이 생긴다. " +
             "후퇴 모션에서는 음수를 넣어 좁히면 빨려 나가는 느낌이 난다.")]
    public float fovPunch = 18f;

    [Tooltip("이 지점(0~1)에서 도착 레벨의 내용을 켠다. 통과 연출 중간에 다음 무대가 나타나게 한다.")]
    [Range(0f, 1f)] public float revealAt = 0.55f;
    [Tooltip("이 지점(0~1)에서 출발 레벨의 내용을 끈다. revealAt보다 뒤에 두면 두 무대가 잠깐 겹친다.")]
    [Range(0f, 1f)] public float hidePreviousAt = 0.65f;
}

/// <summary>
/// 설계서(KRAS G12C MR Quest.pdf)의 Level 0~5 카메라 트랜지션을 재생한다.
///
/// 이 스크립트는 <b>카메라의 움직임만</b> 책임진다. 파티클 워프, 모션 블러, 음향은
/// <see cref="OnTransitionStarted"/> / <see cref="OnTransitionProgress"/> 이벤트를 구독해
/// 별도 컴포넌트(<see cref="TransitionEffects"/>)가 붙인다. 한 클래스가 이동과 연출을
/// 모두 들고 있으면 연출을 바꿀 때마다 이동 로직을 건드리게 된다.
///
/// DOTween을 쓰지 않는다 — 이 프로젝트의 <c>SceneTransitionManager.cs</c>가 DOTween 의존
/// 때문에 파일 전체가 주석 처리된 상태다. 같은 이유로 막히지 않도록 코루틴으로 짰다.
/// </summary>
public class CameraTransitionDirector : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("비워두면 Camera.main")]
    public Camera targetCamera;
    [Tooltip("씬의 모든 LevelStage. 비워두면 시작할 때 자동으로 모은다.")]
    public LevelStage[] stages;

    [Header("전환 설정")]
    [Tooltip("from -> to 조합별 설정. 목록에 없는 조합은 아래 기본값을 쓴다.")]
    public LevelTransition[] transitions = Array.Empty<LevelTransition>();
    public LevelTransition fallback = new LevelTransition
    {
        style = CameraMotionStyle.DollyIn,
        duration = 2f,
        fovPunch = 12f,
    };

    [Header("가감속")]
    [Tooltip("파고드는 모션. 뒤로 갈수록 빨라져야 '빨려 들어가는' 느낌이 난다.")]
    public AnimationCurve accelerate = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.5f, 0f));
    [Tooltip("후퇴 모션. 처음에 빠르고 끝에서 느려진다.")]
    public AnimationCurve decelerate = new AnimationCurve(
        new Keyframe(0f, 0f, 2.5f, 2.5f), new Keyframe(1f, 1f, 0f, 0f));
    [Tooltip("훑다가 초점을 옮기는 모션. 양끝이 부드럽다.")]
    public AnimationCurve smooth = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("전환 중 잠글 것")]
    [Tooltip("이동하는 동안 꺼둘 컴포넌트. 마우스 회전/줌/선택을 여기에 넣는다. " +
             "잠그지 않으면 사용자가 트랜지션 도중 대상을 돌려버려 도착 그림이 흐트러진다.")]
    public MonoBehaviour[] disableDuringTransition = Array.Empty<MonoBehaviour>();

    public QuestLevel CurrentLevel { get; private set; } = QuestLevel.Level0_Body;
    public bool IsTransitioning { get; private set; }

    /// <summary>(출발, 도착, 설정) — 연출 컴포넌트가 이걸 받아 워프/음향을 시작한다.</summary>
    public event Action<QuestLevel, QuestLevel, LevelTransition> OnTransitionStarted;
    /// <summary>진행률 0~1. 매 프레임 발생한다.</summary>
    public event Action<float> OnTransitionProgress;
    public event Action<QuestLevel> OnTransitionCompleted;

    private readonly Dictionary<QuestLevel, LevelStage> _stages = new Dictionary<QuestLevel, LevelStage>();
    private Coroutine _running;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (stages == null || stages.Length == 0)
            stages = FindObjectsByType<LevelStage>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        _stages.Clear();
        foreach (LevelStage stage in stages)
        {
            if (stage == null) continue;
            if (!_stages.ContainsKey(stage.level)) _stages.Add(stage.level, stage);
            else Debug.LogWarning($"[CameraTransitionDirector] {stage.level} 무대가 둘 이상입니다. " +
                                  $"'{stage.name}'는 무시합니다.", stage);
        }
    }

    /// <summary>지정한 레벨로 즉시 이동한다(연출 없음). 시작 배치나 디버그에 쓴다.</summary>
    public void SnapTo(QuestLevel level)
    {
        StopRunning();

        LevelStage target = Find(level);

        // 목표 무대를 마지막에 켠다. 예: Level2~4가 같은 ProteinAnchor를 contentRoot로 공유하는
        // 경우, 다른 레벨을 먼저 꺼버리면(그 안의 렌더러가 공유 오브젝트 소속이라) 목표 무대를
        // 먼저 켜도 나중에 도는 다른 레벨의 SetActive(false)가 그걸 도로 꺼버린다.
        foreach (KeyValuePair<QuestLevel, LevelStage> pair in _stages)
            if (pair.Key != level) pair.Value.SetActive(false, invokeEvents: false);

        target?.SetActive(true, invokeEvents: false);
        if (target != null) ApplyPose(target, target.Anchor.position, target.Anchor.rotation);

        CurrentLevel = level;
        target?.onEnter?.Invoke();
        OnTransitionCompleted?.Invoke(level);
    }

    /// <summary>연출과 함께 지정한 레벨로 이동한다. 이미 그 레벨이면 아무 일도 하지 않는다.</summary>
    public void GoTo(QuestLevel level)
    {
        if (level == CurrentLevel && !IsTransitioning) return;

        StopRunning();
        _running = StartCoroutine(TransitionRoutine(CurrentLevel, level));
    }

    private IEnumerator TransitionRoutine(QuestLevel from, QuestLevel to)
    {
        LevelStage target = Find(to);
        if (target == null)
        {
            Debug.LogError($"[CameraTransitionDirector] {to} 무대를 찾지 못해 이동하지 못했습니다.", this);
            yield break;
        }

        LevelStage previous = Find(from);
        LevelTransition settings = ResolveTransition(from, to);

        IsTransitioning = true;
        SetInputLocked(true);
        OnTransitionStarted?.Invoke(from, to, settings);

        if (settings.style == CameraMotionStyle.Cut || settings.duration <= 0f)
        {
            if (!SharesContent(previous, target)) previous?.SetActive(false);
            target.SetActive(true);
            ApplyPose(target, target.Anchor.position, target.Anchor.rotation);
            FinishTransition(to);
            yield break;
        }

        Transform cam = targetCamera.transform;
        Vector3 startPos = cam.position;
        Quaternion startRot = cam.rotation;
        float startFov = targetCamera.fieldOfView;

        AnimationCurve ease = ResolveEase(settings.style);
        bool revealed = false;
        bool hidden = false;
        float elapsed = 0f;

        while (elapsed < settings.duration)
        {
            elapsed += Time.deltaTime;
            float raw = Mathf.Clamp01(elapsed / settings.duration);
            float t = ease.Evaluate(raw);

            Vector3 position = settings.via != null
                ? QuadraticBezier(startPos, settings.via.position, target.Anchor.position, t)
                : Vector3.LerpUnclamped(startPos, target.Anchor.position, t);

            Quaternion rotation = ResolveRotation(settings, startRot, target, position, t);

            // 이동 중에만 시야각을 벌렸다가 되돌린다. 중간에서 가장 크다.
            float punch = settings.fovPunch * Mathf.Sin(raw * Mathf.PI);
            float baseFov = target.fieldOfView > 0f
                ? Mathf.LerpUnclamped(startFov, target.fieldOfView, t)
                : startFov;

            cam.SetPositionAndRotation(position, rotation);
            targetCamera.fieldOfView = Mathf.Max(baseFov + punch, 1f);

            // 도착 무대를 먼저 켜고 출발 무대를 나중에 끄면 둘이 잠깐 겹치면서
            // 통과하는 느낌이 난다. 반대로 하면 중간에 빈 화면이 보인다.
            if (!revealed && raw >= settings.revealAt)
            {
                revealed = true;
                target.SetActive(true);
            }

            if (!hidden && raw >= settings.hidePreviousAt)
            {
                hidden = true;
                if (previous != null && previous != target && !SharesContent(previous, target))
                    previous.SetActive(false);
            }

            OnTransitionProgress?.Invoke(raw);
            yield return null;
        }

        if (!revealed) target.SetActive(true);
        if (!hidden && previous != null && previous != target && !SharesContent(previous, target))
            previous.SetActive(false);

        ApplyPose(target, target.Anchor.position, target.Anchor.rotation);
        FinishTransition(to);
    }

    private void FinishTransition(QuestLevel level)
    {
        CurrentLevel = level;
        IsTransitioning = false;
        SetInputLocked(false);
        _running = null;

        OnTransitionProgress?.Invoke(1f);
        OnTransitionCompleted?.Invoke(level);
    }

    // --- 모션 세부 ---

    /// <summary>
    /// 회전 규칙은 모션마다 다르다.
    ///
    /// Pan &amp; Focus는 "DNA를 훑다가 리보솜으로 초점을 옮긴다"는 연출이라, 도착 회전으로
    /// 곧장 도는 대신 <b>경유점을 바라보다가</b> 후반에 도착 회전으로 넘어가야 한다.
    /// 나머지는 출발 회전에서 도착 회전으로 바로 도는 편이 안정적이다 —
    /// 파고드는 모션에서 시선이 흔들리면 방향 감각이 무너진다.
    /// </summary>
    private Quaternion ResolveRotation(LevelTransition settings, Quaternion startRot,
                                       LevelStage target, Vector3 position, float t)
    {
        Quaternion endRot = target.Anchor.rotation;

        if (settings.style != CameraMotionStyle.PanAndFocus || settings.via == null)
            return Quaternion.SlerpUnclamped(startRot, endRot, t);

        Vector3 toVia = settings.via.position - position;
        Quaternion lookVia = toVia.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(toVia.normalized, Vector3.up)
            : startRot;

        // 앞 60%는 경유점을 훑고, 뒤 40%에서 도착 시선으로 넘어간다.
        const float focusShiftStart = 0.6f;
        if (t < focusShiftStart) return Quaternion.SlerpUnclamped(startRot, lookVia, t / focusShiftStart);

        float k = (t - focusShiftStart) / (1f - focusShiftStart);
        return Quaternion.SlerpUnclamped(lookVia, endRot, Mathf.Clamp01(k));
    }

    private AnimationCurve ResolveEase(CameraMotionStyle style)
    {
        switch (style)
        {
            case CameraMotionStyle.DollyIn:
            case CameraMotionStyle.MicroZoomIn:
                return accelerate;
            case CameraMotionStyle.SpatialZoomOut:
                return decelerate;
            default:
                return smooth;
        }
    }

    private static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private void ApplyPose(LevelStage stage, Vector3 position, Quaternion rotation)
    {
        targetCamera.transform.SetPositionAndRotation(position, rotation);
        if (stage.fieldOfView > 0f) targetCamera.fieldOfView = stage.fieldOfView;
    }

    // --- 보조 ---

    private LevelTransition ResolveTransition(QuestLevel from, QuestLevel to)
    {
        if (transitions != null)
        {
            foreach (LevelTransition t in transitions)
                if (t != null && t.from == from && t.to == to) return t;
        }

        return fallback;
    }

    private LevelStage Find(QuestLevel level)
    {
        return _stages.TryGetValue(level, out LevelStage stage) ? stage : null;
    }

    /// <summary>
    /// 두 무대가 같은 contentRoot를 가리키는지(예: Level2~4가 모두 ProteinAnchor_Main을 쓰는 경우).
    ///
    /// 도착 무대를 켠(revealAt) 뒤 출발 무대를 끄는(hidePreviousAt) 두 시점이 같은 오브젝트를
    /// 가리키면, 나중에 도는 "끄기"가 방금 켠 상태를 그대로 덮어써 버린다. 겹치는 콘텐츠는
    /// 애초에 끌 대상이 아니다 — 계속 보여야 할 대상을 잠깐 보여줬다 도로 숨기는 셈이 된다.
    /// </summary>
    private static bool SharesContent(LevelStage a, LevelStage b)
    {
        return a != null && b != null && a.contentRoot != null && a.contentRoot == b.contentRoot;
    }

    private void SetInputLocked(bool locked)
    {
        if (disableDuringTransition == null) return;

        foreach (MonoBehaviour behaviour in disableDuringTransition)
            if (behaviour != null) behaviour.enabled = !locked;
    }

    private void StopRunning()
    {
        if (_running == null) return;

        StopCoroutine(_running);
        _running = null;
        IsTransitioning = false;
        SetInputLocked(false);
    }
}
