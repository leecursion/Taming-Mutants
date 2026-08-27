using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F-04.4 p53 Y220C 열안정성 인터랙션 (Level 2).
///
/// 20~60°C 슬라이더 하나로 세 가지 시각 효과만 움직인다 — 별도의 MD trajectory나
/// Dynamics Scrub은 만들지 않는다(설계 원칙 그대로): 온도가 오를수록
///   1) wobble  — 변이 자리(Y220C) 근처 원자가 더 심하게 흔들린다 (거리 기반 감쇠)
///   2) transparency — 원자/결합이 살짝 더 투명해진다 ("녹아내림"이 아니라 "불안정해 보임" 정도로 절제)
///   3) aggregate particle — 변이 자리 주변에 흐린 입자가 더 많이/진하게 떠다닌다
///
/// 37°C(생리 온도)에서 unstable 상태를 강조하되 "완전히 녹아 있다"라고 표현하지 않는다 —
/// 그래서 각 효과의 최대치를 절반 언저리로 캡을 둔다.
/// </summary>
public class ThermalStabilityController : MonoBehaviour
{
    [Header("참조")]
    public ProteinLoader proteinLoader;
    [Tooltip("있으면 아미노산(원자) 레벨에 들어설 때 자동으로 EnterThermalStage()를 부른다 — " +
             "DockingQuestController가 도킹을 활성화하는 시점과 같다. 비우면 씬에서 자동 탐색.")]
    public StructureLevelController levelController;
    [Tooltip("여러 퀘스트가 같은 ProteinAnchor_Main/StructureLevelController를 공유하므로, " +
             "지금 로드된 퀘스트가 이 id일 때만 온도 인터랙션을 켠다. 비우면 씬에서 자동 탐색.")]
    public DockingQuestCatalog questCatalog;
    [Tooltip("이 컨트롤러가 반응할 퀘스트 id — 다른 퀘스트(KRAS/EGFR 등)가 아미노산 레벨에 " +
             "들어가도 온도 슬라이더가 뜨지 않게 막는 기준이다.")]
    public string activeForQuestId = "p53_y220c";
    [Tooltip("카메라가 변이 자리로 클로즈업되는 동안, '구조 옆 사선' 배치 대신 카메라 옆에 크게 " +
             "고정시킬 후보물질 판넬. 비우면 씬에서 자동 탐색.")]
    public CompoundSelectionPanel selectionPanel;
    [Tooltip("카메라가 변이 자리로 클로즈업되는 동안, 분자 옆 배치 대신 사용자 옆으로 옮길 AI 비서. " +
             "비우면 씬에서 자동 탐색.")]
    public AIAssistantFollower assistantFollower;

    private bool _isActiveQuest;
    [Tooltip("wobble의 기준점이 되는 변이 잔기 번호 (Y220C)")]
    public int mutationResidueId = 220;
    public ThermalStabilityHUD hud;
    [Tooltip("비워두면 Camera.main")]
    public Camera targetCamera;

    [Header("온도 범위")]
    public float minCelsius = 20f;
    public float maxCelsius = 60f;
    [Tooltip("생리 온도 — 37°C 강조 문구를 띄우는 기준")]
    public float physiologicalCelsius = 37f;
    public float startCelsius = 20f;

    [Header("Wobble")]
    [Tooltip("변이 자리 바로 옆 원자의 최대 흔들림 진폭(unit). 최고 온도에서의 값.")]
    public float maxWobbleAmplitude = 0.045f;
    [Tooltip("wobble이 미치는 반경(unit, 씬 스케일). 이 반경 밖은 거의 흔들리지 않는다.")]
    public float wobbleRadius = 1.4f;
    public float wobbleFrequency = 7f;

    [Header("Transparency")]
    [Tooltip("가장 뜨거울 때의 알파값. \"완전히 녹아있다\"로 보이지 않도록 0.5 언저리에서 캡을 둔다.")]
    [Range(0.2f, 1f)] public float minAlphaAtMaxTemp = 0.55f;

    [Header("Aggregate Particle")]
    public float maxParticleEmissionRate = 14f;
    public Color particleColor = new Color(1f, 0.55f, 0.35f, 0.5f);

    [Header("카메라 전환 연출")]
    public float cameraTransitionDuration = 1.1f;
    [Tooltip("전환 후 변이 자리까지의 거리(unit)")]
    public float cameraCloseUpDistance = 0.9f;

    /// <summary>0(minCelsius)~1(maxCelsius) 정규화된 현재 온도.</summary>
    public float Normalized01 { get; private set; }
    public float CurrentCelsius { get; private set; }

    // 도킹에 성공(안정화 리간드 결합)하면 true — 온도가 높아도 wobble이 거의 나지 않고
    // HUD는 "IMPROVED / LOW"를 유지한다("37°C에서 안정화 유지" 요구사항).
    private bool _stabilized;
    private const float StabilizedWobbleDamping = 0.12f;

    private Slider _slider;
    private GameObject _sliderPanel;
    private Vector3 _cameraPosBeforeTransition;
    private Quaternion _cameraRotBeforeTransition;
    private bool _hasCameraSnapshot;
    private Material _transparentMaterial;
    private ParticleSystem _aggregateParticles;
    private ParticleSystem.EmissionModule _particleEmission;

    private readonly List<Transform> _wobbleTargets = new List<Transform>();
    private readonly List<Vector3> _wobbleHomePositions = new List<Vector3>();
    private readonly List<float> _wobbleWeights = new List<float>();
    private readonly List<Renderer> _thermalRenderers = new List<Renderer>();
    private Vector3 _mutationLocalPos;
    private bool _hasMutationAnchor;
    private float[] _noiseSeeds;

    private void Awake()
    {
        if (proteinLoader == null) proteinLoader = FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        if (targetCamera == null) targetCamera = Camera.main;
        if (levelController == null && proteinLoader != null)
            levelController = proteinLoader.GetComponent<StructureLevelController>();
        if (questCatalog == null) questCatalog = FindFirstObjectByType<DockingQuestCatalog>(FindObjectsInactive.Include);
        if (selectionPanel == null) selectionPanel = FindFirstObjectByType<CompoundSelectionPanel>(FindObjectsInactive.Include);
        if (assistantFollower == null) assistantFollower = FindFirstObjectByType<AIAssistantFollower>(FindObjectsInactive.Include);
        _transparentMaterial = BuildTransparentMaterial();
    }

    private void OnEnable()
    {
        if (proteinLoader != null) proteinLoader.OnLoaded += HandleProteinLoaded;
        if (levelController != null) levelController.OnLevelChanged += HandleLevelChanged;
        if (questCatalog != null) questCatalog.OnQuestStarted += HandleQuestStarted;
    }

    private void OnDisable()
    {
        if (proteinLoader != null) proteinLoader.OnLoaded -= HandleProteinLoaded;
        if (levelController != null) levelController.OnLevelChanged -= HandleLevelChanged;
        if (questCatalog != null) questCatalog.OnQuestStarted -= HandleQuestStarted;
    }

    // 여러 퀘스트가 같은 ProteinAnchor_Main을 공유한다 — 지금 로드된 퀘스트가 이 컨트롤러가
    // 맡은 것(p53_y220c)이 아니면 아미노산 레벨에 들어가도 아무 반응도 하지 않는다.
    private void HandleQuestStarted(DockingQuestDefinition def)
    {
        _isActiveQuest = def != null && def.id == activeForQuestId;
        if (!_isActiveQuest) ExitThermalStage();
    }

    // DockingQuestController가 도킹을 활성화하는 시점(아미노산 레벨)과 같은 신호로
    // 열안정성 슬라이더도 켜고 끈다 — 리본/Helix로 돌아가면 온도 인터랙션도 함께 숨는다.
    private void HandleLevelChanged(StructureLevelController.ViewLevel level)
    {
        if (!_isActiveQuest) return;
        if (level == StructureLevelController.ViewLevel.AminoAcid) EnterThermalStage();
        else ExitThermalStage();
    }

    private void Update()
    {
        ApplyWobble();
    }

    // --- 진입 지점: Level 2 시작 ---

    /// <summary>Level 2로 들어설 때 QuestSession/DockingQuestCatalog 쪽에서 호출한다.
    /// 슬라이더를 띄우고, 카메라를 변이 자리로 당기고, 시작 온도로 초기화한다.</summary>
    public void EnterThermalStage()
    {
        if (_slider == null) BuildSliderUI();
        _sliderPanel.SetActive(true);

        IndexAtoms();
        EnsureAggregateParticles();

        if (hud != null) hud.gameObject.SetActive(true);

        // 카메라가 구조 전체가 아니라 변이 자리 좁은 부위로 확 당겨지므로, "구조 옆/구조 곁"
        // 배치 기준이 이 동안은 의미가 없어진다 — 클로즈업이 끝날 때까지 카메라 옆 고정으로 돌린다.
        if (selectionPanel != null) selectionPanel.SetZoomOverride(true);
        if (assistantFollower != null) assistantFollower.SetCloseUpOverride(true);

        StopAllCoroutines();
        StartCoroutine(CameraTransitionRoutine());

        SetTemperature(startCelsius);
        _slider.SetValueWithoutNotify(Mathf.InverseLerp(minCelsius, maxCelsius, startCelsius));
    }

    public void ExitThermalStage()
    {
        if (_sliderPanel != null) _sliderPanel.SetActive(false);
        if (hud != null) hud.gameObject.SetActive(false);

        // 카메라가 원래 자리로 돌아가기 시작하는 시점에 맞춰 판넬/비서도 원래 배치 규칙으로
        // 되돌린다. 둘 다 lazy-follow/매 프레임 재배치로 자리를 잡으므로 카메라 복귀 애니메이션과
        // 함께 자연스럽게 제자리를 찾아간다.
        if (selectionPanel != null) selectionPanel.SetZoomOverride(false);
        if (assistantFollower != null) assistantFollower.SetCloseUpOverride(false);

        // 진입할 때 당겨둔 카메라를 원래 자리로 되돌린다 — 안 그러면 '이전'을 눌러
        // Ribbon/Helix로 돌아가도 카메라가 클로즈업된 채로 남는다.
        if (_hasCameraSnapshot && targetCamera != null)
        {
            StopAllCoroutines();
            StartCoroutine(CameraRestoreRoutine());
        }
    }

    private void HandleProteinLoaded(ProteinLoader.ProteinData data)
    {
        IndexAtoms();
    }

    // --- 온도 반영 ---

    public void SetTemperature(float celsius)
    {
        CurrentCelsius = Mathf.Clamp(celsius, minCelsius, maxCelsius);
        Normalized01 = Mathf.InverseLerp(minCelsius, maxCelsius, CurrentCelsius);

        ApplyTransparency(Normalized01);
        ApplyAggregateParticles(Normalized01);

        if (hud != null)
        {
            hud.SetTemperature(CurrentCelsius);

            if (_stabilized)
            {
                hud.SetStability(0.9f, "좋아짐");
                hud.SetWobble(0.08f, "적음");
                hud.ShowMessage(Mathf.Abs(CurrentCelsius - physiologicalCelsius) < 0.5f
                    ? "이 변이 자리에만 맞는 약 덕분에 체온에서도 안정적이에요!"
                    : string.Empty);
            }
            else
            {
                // wobble이 클수록 안정성은 낮다 — 같은 정규화 값을 뒤집어서 보여준다.
                string stabilityLabel = Normalized01 < 0.15f ? "양호" : Normalized01 < 0.55f ? "낮음" : "매우 낮음";
                hud.SetStability(1f - Normalized01, stabilityLabel);
                string wobbleLabel = Normalized01 < 0.15f ? "적음" : Normalized01 < 0.55f ? "보통" : "심함";
                hud.SetWobble(Normalized01, wobbleLabel);

                hud.ShowMessage(Mathf.Abs(CurrentCelsius - physiologicalCelsius) < 0.5f
                    ? "Y220C 변이는 정상 p53보다 체온에서 훨씬 불안정해요."
                    : string.Empty);
            }
        }
    }

    private void HandleSliderChanged(float value01)
    {
        SetTemperature(Mathf.Lerp(minCelsius, maxCelsius, value01));
    }

    /// <summary>
    /// 안정화 리간드(Rezatapopt-like) 도킹 성공 시 DockingQuestController가 호출한다.
    /// 이후로는 온도를 얼마나 올려도 wobble이 거의 나지 않고 HUD는 IMPROVED/LOW를 유지한다.
    /// </summary>
    public void SetStabilized(bool stabilized)
    {
        _stabilized = stabilized;
        SetTemperature(CurrentCelsius); // HUD 라벨을 즉시 다시 계산해 반영한다
    }

    // --- Wobble ---

    private void IndexAtoms()
    {
        _wobbleTargets.Clear();
        _wobbleHomePositions.Clear();
        _wobbleWeights.Clear();
        _thermalRenderers.Clear();
        _hasMutationAnchor = false;

        if (proteinLoader == null) return;

        AtomInfo[] atoms = proteinLoader.GetComponentsInChildren<AtomInfo>(true);
        foreach (AtomInfo atom in atoms)
        {
            if (atom.ResidueId == mutationResidueId && atom.AtomName == "CA")
            {
                _mutationLocalPos = atom.transform.localPosition;
                _hasMutationAnchor = true;
            }
        }

        foreach (AtomInfo atom in atoms)
        {
            Transform t = atom.transform;
            _wobbleTargets.Add(t);
            _wobbleHomePositions.Add(t.localPosition);

            float weight = 1f;
            if (_hasMutationAnchor)
            {
                float dist = Vector3.Distance(t.localPosition, _mutationLocalPos);
                weight = Mathf.Clamp01(1f - dist / Mathf.Max(wobbleRadius, 0.01f));
            }
            _wobbleWeights.Add(weight);

            Renderer r = atom.GetComponent<Renderer>();
            if (r != null) _thermalRenderers.Add(r);
        }

        foreach (GameObject bond in GetBondRenderers())
        {
            Renderer r = bond.GetComponent<Renderer>();
            if (r != null) _thermalRenderers.Add(r);
        }

        _noiseSeeds = new float[_wobbleTargets.Count];
        for (int i = 0; i < _noiseSeeds.Length; i++) _noiseSeeds[i] = Random.value * 100f;

        // 새로 인덱싱했으니 지금 온도값을 다시 입혀 투명도가 원자 재생성 전 상태로 남지 않게 한다.
        ApplyTransparency(Normalized01);
    }

    private IEnumerable<GameObject> GetBondRenderers()
    {
        if (proteinLoader == null) yield break;
        foreach (Transform child in proteinLoader.transform)
        {
            // Bond 프리팹 인스턴스는 AtomInfo가 없다 — 원기둥 렌더러만 남는다.
            // AggregateParticles처럼 이 컨트롤러가 직접 붙인 장식 오브젝트는 제외한다 —
            // 안 그러면 재로드 후에도 살아남은 파티클 렌더러가 다음 IndexAtoms()에 딸려 들어와
            // 원자/결합용 투명 머티리얼을 파티클에 잘못 입히게 된다.
            if (child.GetComponent<AtomInfo>() != null) continue;
            if (child.GetComponent<ParticleSystem>() != null) continue;
            if (child.GetComponent<Renderer>() != null) yield return child.gameObject;
        }
    }

    private void ApplyWobble()
    {
        if (_wobbleTargets.Count == 0 || Normalized01 <= 0f) return;

        float amplitude = maxWobbleAmplitude * Normalized01 * (_stabilized ? StabilizedWobbleDamping : 1f);
        if (amplitude <= 0f) return;

        for (int i = 0; i < _wobbleTargets.Count; i++)
        {
            Transform t = _wobbleTargets[i];
            if (t == null) continue;

            float w = _wobbleWeights[i];
            if (w <= 0f) continue;

            float seed = _noiseSeeds[i];
            float ox = (Mathf.PerlinNoise(Time.time * wobbleFrequency + seed, 0f) - 0.5f) * 2f;
            float oy = (Mathf.PerlinNoise(0f, Time.time * wobbleFrequency + seed) - 0.5f) * 2f;
            float oz = (Mathf.PerlinNoise(Time.time * wobbleFrequency + seed, seed) - 0.5f) * 2f;

            t.localPosition = _wobbleHomePositions[i] + new Vector3(ox, oy, oz) * (amplitude * w);
        }
    }

    // --- Transparency ---

    private void ApplyTransparency(float t01)
    {
        if (_transparentMaterial == null || _thermalRenderers.Count == 0) return;

        float alpha = Mathf.Lerp(1f, minAlphaAtMaxTemp, t01);
        var mpb = new MaterialPropertyBlock();

        foreach (Renderer r in _thermalRenderers)
        {
            if (r == null) continue;

            if (t01 > 0.001f) r.sharedMaterial = _transparentMaterial;

            r.GetPropertyBlock(mpb);
            Color c = mpb.GetColor("_BaseColor");
            if (c.a <= 0f) c = new Color(0.75f, 0.78f, 0.82f, 1f); // 기본값이 안 잡혀 있으면 은은한 회색으로 시작
            c.a = alpha;
            mpb.SetColor("_BaseColor", c);
            r.SetPropertyBlock(mpb);
        }
    }

    private static Material BuildTransparentMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return null;

        var mat = new Material(shader) { name = "ThermalUnstable_Transparent" };
        mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
        mat.SetFloat("_Blend", 0f);   // 0 = Alpha
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_EMISSION");
        return mat;
    }

    // --- Aggregate Particle ---

    private void EnsureAggregateParticles()
    {
        if (_aggregateParticles != null || !_hasMutationAnchor || proteinLoader == null) return;

        var go = new GameObject("AggregateParticles");
        go.transform.SetParent(proteinLoader.transform, false);
        go.transform.localPosition = _mutationLocalPos;

        _aggregateParticles = go.AddComponent<ParticleSystem>();
        var main = _aggregateParticles.main;
        main.loop = true;
        main.startLifetime = 2.2f;
        main.startSpeed = 0.08f;
        main.startSize = 0.05f;
        main.startColor = particleColor;
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var shape = _aggregateParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = wobbleRadius * 0.8f;

        _particleEmission = _aggregateParticles.emission;
        _particleEmission.rateOverTime = 0f;

        var colorOverLifetime = _aggregateParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(particleColor, 0f), new GradientColorKey(particleColor, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader != null) renderer.sharedMaterial = new Material(particleShader);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void ApplyAggregateParticles(float t01)
    {
        if (_aggregateParticles == null) return;

        // 절반을 넘기지 않게 캡 — "완전히 녹아 뭉친다"가 아니라 "위험 신호가 보이기 시작한다" 정도로.
        float capped = Mathf.Min(t01, 0.85f);
        _particleEmission.rateOverTime = maxParticleEmissionRate * capped;
    }

    // --- 카메라 전환 연출 ---

    private IEnumerator CameraTransitionRoutine()
    {
        if (targetCamera == null || !_hasMutationAnchor || proteinLoader == null) yield break;

        // 되돌아갈 자리를 먼저 찍어둔다 — ExitThermalStage(예: '이전' 버튼)가 이 값으로 복귀시킨다.
        _cameraPosBeforeTransition = targetCamera.transform.position;
        _cameraRotBeforeTransition = targetCamera.transform.rotation;
        _hasCameraSnapshot = true;

        Vector3 focusWorld = proteinLoader.transform.TransformPoint(_mutationLocalPos);
        Vector3 fromPos = targetCamera.transform.position;
        Vector3 dirFromFocus = (fromPos - focusWorld);
        if (dirFromFocus.sqrMagnitude < 1e-4f) dirFromFocus = -targetCamera.transform.forward;
        Vector3 toPos = focusWorld + dirFromFocus.normalized * cameraCloseUpDistance;

        Quaternion fromRot = targetCamera.transform.rotation;
        Quaternion toRot = Quaternion.LookRotation((focusWorld - toPos).normalized, Vector3.up);

        yield return LerpCamera(fromPos, toPos, fromRot, toRot);
    }

    private IEnumerator CameraRestoreRoutine()
    {
        if (targetCamera == null) yield break;

        Vector3 fromPos = targetCamera.transform.position;
        Quaternion fromRot = targetCamera.transform.rotation;

        yield return LerpCamera(fromPos, _cameraPosBeforeTransition, fromRot, _cameraRotBeforeTransition);
        _hasCameraSnapshot = false;
    }

    private IEnumerator LerpCamera(Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot)
    {
        float elapsed = 0f;
        while (elapsed < cameraTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / cameraTransitionDuration));
            targetCamera.transform.position = Vector3.Lerp(fromPos, toPos, k);
            targetCamera.transform.rotation = Quaternion.Slerp(fromRot, toRot, k);
            yield return null;
        }
        targetCamera.transform.position = toPos;
        targetCamera.transform.rotation = toRot;
    }

    // --- 슬라이더 UI ---

    private void BuildSliderUI()
    {
        var canvasGo = new GameObject("ThermalSliderCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGo.AddComponent<GraphicRaycaster>();

        var rootGo = new GameObject("SliderPanel", typeof(RectTransform));
        _sliderPanel = rootGo;
        rootGo.transform.SetParent(canvasGo.transform, false);
        var rootRect = (RectTransform)rootGo.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 40f);
        rootRect.sizeDelta = new Vector2(560f, 90f);

        CreateLayer(rootGo.transform, "Glow", HoloSpriteFactory.Glow(), new Color(0.35f, 0.85f, 1f, 0.16f), 16f);
        CreateLayer(rootGo.transform, "Panel", HoloSpriteFactory.Panel(), new Color(0.02f, 0.06f, 0.10f, 0.9f), 0f);
        CreateLayer(rootGo.transform, "Stroke", HoloSpriteFactory.Stroke(), new Color(0.35f, 0.85f, 1f, 0.7f), 0f);

        Text label = CreateText(rootGo.transform, "Label", 22, FontStyle.Bold, new Color(0.85f, 0.95f, 1f));
        label.alignment = TextAnchor.UpperCenter;
        label.text = "온도";
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = new Vector2(0f, 1f); labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -10f);
        labelRect.sizeDelta = new Vector2(-40f, 28f);

        var sliderGo = new GameObject("Slider", typeof(RectTransform));
        sliderGo.transform.SetParent(rootGo.transform, false);
        var sliderRect = (RectTransform)sliderGo.transform;
        sliderRect.anchorMin = new Vector2(0f, 0f); sliderRect.anchorMax = new Vector2(1f, 0f);
        sliderRect.pivot = new Vector2(0.5f, 0f);
        sliderRect.anchoredPosition = new Vector2(0f, 16f);
        sliderRect.sizeDelta = new Vector2(-60f, 22f);

        _slider = sliderGo.AddComponent<Slider>();
        _slider.minValue = 0f; _slider.maxValue = 1f;

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(sliderGo.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.sprite = HoloSpriteFactory.Panel();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(1f, 1f, 1f, 0.12f);
        var bgRect = (RectTransform)bg.transform;
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;

        var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGo.transform.SetParent(sliderGo.transform, false);
        var fillAreaRect = (RectTransform)fillAreaGo.transform;
        fillAreaRect.anchorMin = Vector2.zero; fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(5f, 0f); fillAreaRect.offsetMax = new Vector2(-5f, 0f);

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(fillAreaGo.transform, false);
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.color = new Color(1f, 0.55f, 0.3f, 0.85f);
        var fillRect = (RectTransform)fillGo.transform;
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        _slider.fillRect = fillRect;
        _slider.targetGraphic = fillImg;

        var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaGo.transform.SetParent(sliderGo.transform, false);
        var handleAreaRect = (RectTransform)handleAreaGo.transform;
        handleAreaRect.anchorMin = Vector2.zero; handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10f, 0f); handleAreaRect.offsetMax = new Vector2(-10f, 0f);

        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(handleAreaGo.transform, false);
        var handleImg = handleGo.AddComponent<Image>();
        handleImg.sprite = HoloSpriteFactory.Circle();
        handleImg.color = Color.white;
        var handleRect = (RectTransform)handleGo.transform;
        handleRect.sizeDelta = new Vector2(24f, 24f);
        _slider.handleRect = handleRect;

        _slider.onValueChanged.AddListener(HandleSliderChanged);

        // 슬라이더 양 끝에 최소/최대 온도만 작게 표시한다 — 정확한 현재값은 ThermalStabilityHUD가
        // 이미 보여주므로 여기서는 라벨과 겹치지 않는 범위 힌트 정도만 둔다.
        Text minLabel = CreateText(rootGo.transform, "MinLabel", 15, FontStyle.Normal, new Color(0.85f, 0.95f, 1f, 0.6f));
        minLabel.text = $"{minCelsius:0}°C";
        var minRect = (RectTransform)minLabel.transform;
        minRect.anchorMin = new Vector2(0f, 0f); minRect.anchorMax = new Vector2(0f, 0f);
        minRect.pivot = new Vector2(0f, 1f);
        minRect.anchoredPosition = new Vector2(30f, 14f);
        minRect.sizeDelta = new Vector2(60f, 18f);

        Text maxLabel = CreateText(rootGo.transform, "MaxLabel", 15, FontStyle.Normal, new Color(0.85f, 0.95f, 1f, 0.6f));
        maxLabel.text = $"{maxCelsius:0}°C";
        maxLabel.alignment = TextAnchor.MiddleRight;
        var maxRect = (RectTransform)maxLabel.transform;
        maxRect.anchorMin = new Vector2(1f, 0f); maxRect.anchorMax = new Vector2(1f, 0f);
        maxRect.pivot = new Vector2(1f, 1f);
        maxRect.anchoredPosition = new Vector2(-30f, 14f);
        maxRect.sizeDelta = new Vector2(60f, 18f);
    }

    private static Image CreateLayer(Transform parent, string name, Sprite sprite, Color color, float expand)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-expand, -expand);
        rect.offsetMax = new Vector2(expand, expand);

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(Transform parent, string name, int fontSize, FontStyle style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<Text>();
        text.font = HoloFont.Resolve();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
