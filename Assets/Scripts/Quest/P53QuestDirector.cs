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
///   2) DNA 배경과 기능 회복 결과를 표시한 뒤 공통 성공 화면으로 넘어간다.
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
    [Tooltip("마무리 연출로 만든 DNA를 원자 단계에서만 보이게 하려고 구독한다. " +
             "비우면 씬에서 자동 탐색.")]
    public StructureLevelController levelController;
    [Tooltip("다른 사건으로 넘어갈 때 이 연출을 치우려고 구독한다. 비우면 씬에서 자동 탐색.")]
    public DockingQuestCatalog questCatalog;
    [Tooltip("이 연출이 속한 퀘스트 id. 다른 사건이 시작되면 만들어 둔 장면을 지운다.")]
    public string activeForQuestId = "p53_y220c";

    [Header("Before/After 타이밍(초)")]
    public float holdAfterDockingSeconds = 1.5f;
    public float beforeHoldSeconds = 2.2f;
    public float afterHoldSeconds = 2.2f;

    [Header("페이드")]
    public float fadeDuration = 0.6f;

    [Header("DNA 연출")]
    public float dnaHelixLength = 3.2f;
    public float dnaRadius = 0.35f;
    public int dnaBasePairCount = 22;
    public Color dnaBackboneColor = new Color(0.55f, 0.65f, 0.75f);
    public Color dnaBasePairColor = new Color(0.3f, 0.75f, 0.9f);

    private CanvasGroup _fadeOverlay;
    private bool _finalePlayed;
    public bool IsFinalePlaying { get; private set; }

    private void Awake()
    {
        if (dockingController == null) dockingController = FindFirstObjectByType<DockingQuestController>(FindObjectsInactive.Include);
        if (thermal == null) thermal = FindFirstObjectByType<ThermalStabilityController>(FindObjectsInactive.Include);
        if (hud == null) hud = FindFirstObjectByType<ThermalStabilityHUD>(FindObjectsInactive.Include);
        if (dbdAnchor == null && thermal != null && thermal.proteinLoader != null) dbdAnchor = thermal.proteinLoader.transform;
        if (levelController == null && thermal != null) levelController = thermal.levelController;
        if (levelController == null) levelController = FindFirstObjectByType<StructureLevelController>(FindObjectsInactive.Include);
        if (questCatalog == null) questCatalog = FindFirstObjectByType<DockingQuestCatalog>(FindObjectsInactive.Include);

        BuildFadeOverlay();
    }

    private void OnEnable()
    {
        if (dockingController != null) dockingController.OnDockingFinished += HandleDockingFinished;
        if (levelController != null) levelController.OnLevelChanged += HandleLevelChanged;
        if (questCatalog != null) questCatalog.OnQuestStarted += HandleQuestStarted;
    }

    private void OnDisable()
    {
        if (dockingController != null) dockingController.OnDockingFinished -= HandleDockingFinished;
        if (levelController != null) levelController.OnLevelChanged -= HandleLevelChanged;
        if (questCatalog != null) questCatalog.OnQuestStarted -= HandleQuestStarted;
    }

    /// <summary>
    /// DNA는 단백질 앵커 옆에 세운 별개 오브젝트라, 원자를 끄는 레벨 전환이 함께 끄지
    /// 못한다. 마무리 연출을 본 뒤 '이전'을 누르면 나선/리본 화면에 DNA만 남는다.
    /// </summary>
    private void HandleLevelChanged(StructureLevelController.ViewLevel level)
    {
        if (_dnaRoot == null) return;
        _dnaRoot.SetActive(level == StructureLevelController.ViewLevel.AminoAcid);
    }

    /// <summary>다른 사건으로 넘어가면 이 사건의 마무리 장면은 치운다. 같은 사건을 다시 고르면
    /// 연출도 처음부터 다시 볼 수 있어야 하므로 재생 여부도 함께 되돌린다.</summary>
    private void HandleQuestStarted(DockingQuestDefinition def)
    {
        // StopAllCoroutines는 finally를 실행하지 않는다 — 플래그는 직접 되돌린다.
        StopAllCoroutines();
        IsFinalePlaying = false;
        if (_fadeOverlay != null) _fadeOverlay.alpha = 0f;

        if (_dnaRoot != null) Destroy(_dnaRoot);
        _dnaRoot = null;

        _finalePlayed = def == null || def.id != activeForQuestId;
    }

    private void HandleDockingFinished(DockingResult result)
    {
        if (_finalePlayed) return;
        if (!result.IsSuccess) return;
        if (result.Compound == null || result.Compound.id != stabilizerCompoundId) return;

        // 성공 즉시 해결 화면으로 넘어가는 설정이면 마무리 장면을 끼워 넣지 않는다.
        // 검증 버튼을 띄워도 해결 화면이 그 위를 덮어 누를 수 없다.
        if (dockingController != null && dockingController.completeImmediatelyOnSuccess) return;

        _finalePlayed = true;
        if (dockingController.selectionPanel != null)
            dockingController.selectionPanel.Experiment.OfferVerification("37°C에서 검증", () => StartCoroutine(TrackedFinaleRoutine()));
        else StartCoroutine(TrackedFinaleRoutine());
    }

    // 비서 대사 큐에는 없는 시각 연출도 끝난 뒤 질문할 수 있게 알린다.
    private IEnumerator TrackedFinaleRoutine()
    {
        IsFinalePlaying = true;
        try { yield return FinaleRoutine(); }
        finally { IsFinalePlaying = false; }
    }

    private IEnumerator FinaleRoutine()
    {
        yield return new WaitForSeconds(holdAfterDockingSeconds);

        yield return BeforeAfterRoutine();

        yield return Fade(0f, 1f);
        BuildDnaScene();
        yield return Fade(1f, 0f);

        if (hud != null) hud.SetDnaBindingCompetent(true);
        if (dockingController != null && dockingController.selectionPanel != null)
            dockingController.selectionPanel.Experiment.FinishVerification("37°C · 처리 전후 비교: 흔들림 감소 / DNA 결합 회복");
        // The success shield belongs exclusively to the common completion screen.
        yield return new WaitForSeconds(1.5f);
        if (_dnaRoot != null) _dnaRoot.SetActive(false);
        if (dockingController != null) dockingController.CompleteVerification();

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

    // --- 2) DNA ---

    private GameObject _dnaRoot;

    private void BuildDnaScene()
    {
        if (_dnaRoot != null) return;
        if (dbdAnchor == null) return;

        _dnaRoot = new GameObject("DnaResponseElement");
        AminoAcidOnlyVisual.Mark(_dnaRoot, levelController);
        _dnaRoot.transform.SetParent(dbdAnchor.parent, false);
        _dnaRoot.transform.position = dbdAnchor.position + dbdAnchor.forward * 1.4f;
        _dnaRoot.transform.rotation = Quaternion.LookRotation(dbdAnchor.forward, Vector3.up);

        BuildDnaHelix(_dnaRoot.transform);
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
