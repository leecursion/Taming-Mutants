using UnityEngine;

/// <summary>
/// 구조(리본/Helix/아미노산)를 향해 전용 스포트라이트를 비춘다.
/// 배경(실험실 glb 모델)이 디테일이 많고 밝아 구조가 묻히는 문제를,
/// 배경을 건드리지 않고 구조 쪽 밝기만 끌어올려 대비로 보완한다.
///
/// 구조의 실제 렌더러 경계를 주기적으로 재측정해 위치/스팟 폭/거리를 다시 잡으므로
/// 리본 -> Helix -> 아미노산 레벨 전환이나 다른 단백질 로딩에도 자동으로 따라간다.
/// </summary>
public class StructureSpotlight : MonoBehaviour
{
    [Tooltip("비워두면 씬에서 자동 탐색")]
    public ProteinLoader proteinLoader;
    [Tooltip("비워두면 Camera.main 사용")]
    public Camera targetCamera;

    [Header("배치 (구조 반지름 기준 배율)")]
    [Tooltip("구조 위 얼마나 높은 곳에서 비출지")]
    public float heightFactor = 1.6f;
    [Tooltip("카메라 쪽으로 당기는 정도. 0이면 정수리 바로 위, 클수록 카메라 쪽으로 기울어 역광을 피한다")]
    public float towardCameraFactor = 0.6f;

    [Header("조명")]
    public Color lightColor = new Color(0.85f, 0.93f, 1f);
    public float intensity = 3.5f;
    [Tooltip("스팟 콘이 구조를 얼마나 여유 있게 감싸는지")]
    public float coneFitMargin = 1.35f;
    public float minSpotAngle = 15f;
    public float maxSpotAngle = 150f;
    public float minRange = 2f;

    [Header("갱신")]
    [Tooltip("경계 재측정 주기(초). 레벨 전환/로딩 시에는 즉시 강제 갱신된다")]
    public float boundsRefreshInterval = 1f;

    private Light _light;
    private Bounds _lastBounds;
    private bool _hasBounds;
    private float _nextRefreshAt;
    private int _lastRendererCount = -1;

    private void Awake()
    {
        _light = GetComponent<Light>();
        if (_light == null) _light = gameObject.AddComponent<Light>();

        _light.type = LightType.Spot;
        _light.color = lightColor;
        _light.intensity = intensity;
        // 원자가 수천 개인 씬에 그림자 광원을 더하지 않는다 (다른 조명들과 같은 관례).
        _light.shadows = LightShadows.None;
    }

    private void OnEnable()
    {
        if (proteinLoader == null) proteinLoader = FindFirstObjectByType<ProteinLoader>();
        if (targetCamera == null) targetCamera = Camera.main;
        if (proteinLoader != null) proteinLoader.OnLoaded += HandleLoaded;
    }

    private void OnDisable()
    {
        if (proteinLoader != null) proteinLoader.OnLoaded -= HandleLoaded;
    }

    private void HandleLoaded(ProteinLoader.ProteinData data) => RefreshBounds(force: true);

    private void LateUpdate()
    {
        if (proteinLoader == null) return;

        if (!_hasBounds || Time.time >= _nextRefreshAt) RefreshBounds(force: false);
        if (!_hasBounds) return;

        ApplyTransform();
    }

    // 리본/Helix/아미노산 레벨 전환마다 활성 렌더러 구성이 통째로 바뀌므로,
    // 렌더러 개수 변화를 신호로 삼아 불필요한 재측정을 건너뛴다.
    private void RefreshBounds(bool force)
    {
        _nextRefreshAt = Time.time + Mathf.Max(boundsRefreshInterval, 0.1f);

        var renderers = proteinLoader.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return;
        if (!force && renderers.Length == _lastRendererCount) return;
        _lastRendererCount = renderers.Length;

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers)
            if (r.enabled) b.Encapsulate(r.bounds);

        _lastBounds = b;
        _hasBounds = true;
    }

    private void ApplyTransform()
    {
        float radius = Mathf.Max(_lastBounds.extents.magnitude, 0.05f);
        Vector3 center = _lastBounds.center;

        // 카메라 반대편(정수리 위)에서만 비추면 카메라를 보는 면이 역광으로 어두워진다.
        // 카메라 쪽으로 살짝 기울여야 사용자가 보는 면이 밝게 나온다.
        Vector3 towardCamera = targetCamera != null
            ? (targetCamera.transform.position - center).normalized
            : Vector3.back;

        Vector3 pos = center + Vector3.up * (radius * heightFactor) + towardCamera * (radius * towardCameraFactor);
        transform.position = pos;

        Vector3 aimDir = center - pos;
        if (aimDir.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(aimDir.normalized, Vector3.up);

        float distance = aimDir.magnitude;
        _light.range = Mathf.Max(minRange, distance + radius * 1.5f);

        float angle = Mathf.Atan2(radius * coneFitMargin, Mathf.Max(distance, 0.01f)) * Mathf.Rad2Deg * 2f;
        _light.spotAngle = Mathf.Clamp(angle, minSpotAngle, maxSpotAngle);
    }
}
