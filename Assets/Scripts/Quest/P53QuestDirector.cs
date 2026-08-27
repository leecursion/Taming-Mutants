using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// p53 Y220C 퀘스트의 마무리 연출 — 안정화제(Rezatapopt-like) 도킹에 성공하면:
///   1) 37°C Before/After 재검증 — 같은 구조를 안정화 끄고/켜서 Stability·Wobble을 비교한다
///      (구조를 두 벌 복제하지 않는다 — DockingQuestController가 이미 만든 "안정화됨" 상태를
///       잠깐 껐다 켜는 것만으로 Before/After를 보여줄 수 있어 원자 1500여 개짜리 구조를
///       또 하나 스폰하는 비용을 피한다).
///   2) Level 3&4 — DNA response element로 페이드하고, 안정화된 DBD 4개가 모여
///      기능하는 p53 tetramer가 DNA에 결합하는 짧은 연출을 보여준다.
///
/// Rezatapopt가 tetramerization 자체를 만드는 것처럼 보이면 안 된다는 설계 원칙에 따라,
/// 리간드는 이 연출에 등장하지 않는다 — "DBD가 안정화됐으니 원래 하던 일(사량체 결합)을
/// 다시 할 수 있게 됐다"만 보여준다.
/// </summary>
public class P53QuestDirector : MonoBehaviour
{
    [Header("참조")]
    public DockingQuestController dockingController;
    public ThermalStabilityController thermal;
    public ThermalStabilityHUD hud;
    [Tooltip("정답 화합물 id. 이 id의 도킹이 Success일 때만 마무리 연출을 시작한다.")]
    public string stabilizerCompoundId = "p53_stabilizer";
    [Tooltip("사량체가 모여들 DBD 대표 위치 (보통 ProteinAnchor_Main)")]
    public Transform dbdAnchor;

    [Header("Before/After 타이밍(초)")]
    public float holdAfterDockingSeconds = 1.5f;
    public float beforeHoldSeconds = 2.2f;
    public float afterHoldSeconds = 2.2f;

    [Header("페이드")]
    public float fadeDuration = 0.6f;

    [Header("DNA / Tetramer 연출")]
    public float dnaHelixLength = 3.2f;
    public float dnaRadius = 0.35f;
    public int dnaBasePairCount = 22;
    public Color dnaBackboneColor = new Color(0.55f, 0.65f, 0.75f);
    public Color dnaBasePairColor = new Color(0.3f, 0.75f, 0.9f);
    public Color tetramerColor = new Color(0.25f, 1f, 0.35f);
    public float tetramerConvergeDuration = 2.2f;

    private CanvasGroup _fadeOverlay;
    private bool _finalePlayed;

    private void Awake()
    {
        if (dockingController == null) dockingController = FindFirstObjectByType<DockingQuestController>(FindObjectsInactive.Include);
        if (thermal == null) thermal = FindFirstObjectByType<ThermalStabilityController>(FindObjectsInactive.Include);
        if (hud == null) hud = FindFirstObjectByType<ThermalStabilityHUD>(FindObjectsInactive.Include);
        if (dbdAnchor == null && thermal != null && thermal.proteinLoader != null) dbdAnchor = thermal.proteinLoader.transform;

        BuildFadeOverlay();
    }

    private void OnEnable()
    {
        if (dockingController != null) dockingController.OnDockingFinished += HandleDockingFinished;
    }

    private void OnDisable()
    {
        if (dockingController != null) dockingController.OnDockingFinished -= HandleDockingFinished;
    }

    private void HandleDockingFinished(DockingResult result)
    {
        if (_finalePlayed) return;
        if (!result.IsSuccess) return;
        if (result.Compound == null || result.Compound.id != stabilizerCompoundId) return;

        _finalePlayed = true;
        StartCoroutine(FinaleRoutine());
    }

    private IEnumerator FinaleRoutine()
    {
        yield return new WaitForSeconds(holdAfterDockingSeconds);

        yield return BeforeAfterRoutine();

        yield return Fade(0f, 1f);
        BuildDnaScene();
        yield return Fade(1f, 0f);

        yield return TetramerConvergeRoutine();

        if (hud != null)
            hud.ShowMessage("단백질이 안정되면 → DNA와 다시 결합할 수 있고 → p53이 원래 하던 일(암 억제)을 다시 할 수 있어요.");
    }

    // --- 1) 37°C Before/After 재검증 ---

    private IEnumerator BeforeAfterRoutine()
    {
        if (thermal == null || hud == null) yield break;

        thermal.SetTemperature(thermal.physiologicalCelsius);

        thermal.SetStabilized(false);
        hud.ShowMessage("약 사용 전 — Y220C · 안정성: 낮음 · 흔들림: 심함");
        yield return new WaitForSeconds(beforeHoldSeconds);

        thermal.SetStabilized(true);
        hud.ShowMessage("약 사용 후 — Y220C + 안정화제 · 안정성: 좋아짐 · 흔들림: 적음");
        yield return new WaitForSeconds(afterHoldSeconds);
    }

    // --- 2) DNA / Tetramer ---

    private GameObject _dnaRoot;
    private readonly List<Transform> _tetramerSubunits = new List<Transform>();

    private void BuildDnaScene()
    {
        if (_dnaRoot != null) return;
        if (dbdAnchor == null) return;

        _dnaRoot = new GameObject("DnaResponseElement");
        _dnaRoot.transform.SetParent(dbdAnchor.parent, false);
        _dnaRoot.transform.position = dbdAnchor.position + dbdAnchor.forward * 1.4f;
        _dnaRoot.transform.rotation = Quaternion.LookRotation(dbdAnchor.forward, Vector3.up);

        BuildDnaHelix(_dnaRoot.transform);
        BuildTetramerSubunits(_dnaRoot.transform);
    }

    /// <summary>단순화한 이중나선 — 두 가닥(구슬 사슬)과 그 사이를 잇는 염기쌍(가는 실린더).
    /// 실제 나선 좌표를 계산해서 배치하되, 원자 단위가 아니라 장식용 프리미티브라 가볍다.</summary>
    private void BuildDnaHelix(Transform parent)
    {
        Material backboneMat = RuntimeMaterials.Solid;

        var strandA = new List<Vector3>();
        var strandB = new List<Vector3>();

        float turns = dnaBasePairCount / 10f; // 실제 DNA는 약 10.5 bp/turn
        for (int i = 0; i < dnaBasePairCount; i++)
        {
            float tNorm = i / (float)Mathf.Max(dnaBasePairCount - 1, 1);
            float y = Mathf.Lerp(-dnaHelixLength * 0.5f, dnaHelixLength * 0.5f, tNorm);
            float angle = tNorm * turns * Mathf.PI * 2f;

            Vector3 a = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dnaRadius + Vector3.up * y;
            Vector3 b = new Vector3(Mathf.Cos(angle + Mathf.PI), 0f, Mathf.Sin(angle + Mathf.PI)) * dnaRadius + Vector3.up * y;
            strandA.Add(a);
            strandB.Add(b);

            SpawnBead(parent, a, 0.045f, dnaBackboneColor, backboneMat);
            SpawnBead(parent, b, 0.045f, dnaBackboneColor, backboneMat);

            // 몇 칸마다 염기쌍(가로대)을 이어 사다리 느낌을 준다
            if (i % 2 == 0)
                SpawnRod(parent, a, b, 0.018f, dnaBasePairColor, backboneMat);
        }
    }

    private void BuildTetramerSubunits(Transform dnaParent)
    {
        _tetramerSubunits.Clear();

        // "dimer-of-dimers" — DNA 응답요소 양옆에 2개씩, 대칭으로 배치한다.
        Vector3[] targetOffsets =
        {
            new Vector3(0.55f, 0.5f, 0f), new Vector3(0.55f, -0.5f, 0f),
            new Vector3(-0.55f, 0.5f, 0f), new Vector3(-0.55f, -0.5f, 0f),
        };

        for (int i = 0; i < targetOffsets.Length; i++)
        {
            GameObject sub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sub.name = $"DBD_Subunit_{i}";
            Object.Destroy(sub.GetComponent<Collider>());
            sub.transform.SetParent(dnaParent, false);
            sub.transform.localScale = Vector3.one * 0.32f;

            var renderer = sub.GetComponent<Renderer>();
            RuntimeMaterials.ApplySolid(sub);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", tetramerColor);
            mpb.SetColor("_EmissionColor", tetramerColor * 0.4f);
            renderer.SetPropertyBlock(mpb);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 시작 위치는 목표에서 바깥으로 밀어둔다 — 수렴하며 다가오는 연출을 위해
            sub.transform.localPosition = targetOffsets[i] * 3f;

            _tetramerSubunits.Add(sub.transform);
        }

        // 목표 위치는 Rotate 애니메이션 코루틴에서 참조할 수 있게 로컬 데이터로 들고 있는다
        _tetramerTargets = targetOffsets;
    }

    private Vector3[] _tetramerTargets;

    private IEnumerator TetramerConvergeRoutine()
    {
        if (_tetramerSubunits.Count == 0 || _tetramerTargets == null) yield break;

        var starts = new Vector3[_tetramerSubunits.Count];
        for (int i = 0; i < _tetramerSubunits.Count; i++)
            starts[i] = _tetramerSubunits[i].localPosition;

        float elapsed = 0f;
        while (elapsed < tetramerConvergeDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / tetramerConvergeDuration));
            for (int i = 0; i < _tetramerSubunits.Count; i++)
            {
                if (_tetramerSubunits[i] == null) continue;
                _tetramerSubunits[i].localPosition = Vector3.Lerp(starts[i], _tetramerTargets[i], k);
            }
            yield return null;
        }

        for (int i = 0; i < _tetramerSubunits.Count; i++)
            if (_tetramerSubunits[i] != null) _tetramerSubunits[i].localPosition = _tetramerTargets[i];

        if (hud != null) hud.SetDnaBindingCompetent(true);
    }

    // --- 작은 헬퍼: 장식용 프리미티브 ---

    private static void SpawnBead(Transform parent, Vector3 localPos, float scale, Color color, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * scale;
        Tint(go, material, color);
    }

    private static void SpawnRod(Transform parent, Vector3 a, Vector3 b, float radius, Color color, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        go.transform.localScale = new Vector3(radius, Vector3.Distance(a, b) * 0.5f, radius);
        Tint(go, material, color);
    }

    private static void Tint(GameObject go, Material material, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(mpb);
    }

    // --- 화면 페이드 ---

    private void BuildFadeOverlay()
    {
        var canvasGo = new GameObject("P53FadeCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var imgGo = new GameObject("Fade", typeof(RectTransform));
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rect = (RectTransform)imgGo.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = imgGo.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        _fadeOverlay = imgGo.AddComponent<CanvasGroup>();
        _fadeOverlay.alpha = 0f;
        _fadeOverlay.blocksRaycasts = false;
    }

    private IEnumerator Fade(float from, float to)
    {
        if (_fadeOverlay == null) yield break;

        _fadeOverlay.alpha = from;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            _fadeOverlay.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }
        _fadeOverlay.alpha = to;
    }
}
