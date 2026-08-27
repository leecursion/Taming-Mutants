using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F-04 사건 4(CFTR F508del) 전용 컨트롤러. ThermalStabilityController(p53, 사건 5)와 같은 자리 —
/// 공용 도킹 엔진(DockingQuestController/CompoundSelectionPanel/StructureLevelController)은
/// 건드리지 않고, "지금 로드된 퀘스트가 cftr_f508del일 때만" 반응해 아래를 담당한다.
///
///   Level 1→2 전환: DNA(코드로 생성) 페이드인 → 508번 코돈 삭제 연출(pre-baked, 실제 폴딩
///                    시뮬레이션 아님) → DNA 페이드아웃 + 8EJ1 페이드인
///   Level 2: NBD1 F508 루프는 DockingQuestController의 포켓 마커가 이미 강조하므로 손대지 않고,
///             TMD2 ICL4 계면(1030-1085)만 보조색으로 틴트. wobble + ubiquitin/QC 파티클 + HUD.
///   후보물질 결과별 반응(DockingQuestController가 호출): corrector 성공 시 8EJ1→8EIQ 구조 스왑,
///     potentiator 성공 시 gate opening + Cl- particle flow, 오답별 개별 연출/HUD 문구.
///
/// 실제 co-translational folding/ERAD/vesicle trafficking 시뮬레이션은 만들지 않는다(설계 원칙).
/// </summary>
public class CftrRescueController : MonoBehaviour
{
    [Header("참조 (비우면 씬에서 자동 탐색)")]
    public ProteinLoader proteinLoader;
    public StructureLevelController levelController;
    public DockingQuestCatalog questCatalog;
    public CftrHUD hud;

    [Tooltip("이 컨트롤러가 반응할 퀘스트 id")]
    public string activeForQuestId = "cftr_f508del";

    [Header("변이 자리 / ICL4 계면 (로드된 구조 JSON의 res_id 기준)")]
    [Tooltip("F508은 결실이라 존재하지 않는다 — 바로 옆(507/509)을 wobble·파티클 기준점으로 쓴다")]
    public int anchorResidueId = 509;
    public int icl4StartResId = 1030;
    public int icl4EndResId = 1085;
    public Color icl4HighlightColor = new Color(0.55f, 0.4f, 0.9f);

    [Header("Wobble")]
    public float maxWobbleAmplitude = 0.05f;
    public float wobbleRadius = 1.4f;
    public float wobbleFrequency = 7f;

    [Header("Ubiquitin / QC particle")]
    public float maxQcEmissionRate = 12f;
    public Color qcParticleColor = new Color(0.85f, 0.9f, 0.3f, 0.55f);

    [Header("ER stress 경고 (Proteasome inhibitor 오답)")]
    public Color erStressColor = new Color(1f, 0.4f, 0.15f);

    [Header("인트로: DNA → F508 결실 → 단백질")]
    public int dnaBasePairCount = 18;
    public float dnaHelixLength = 2.6f;
    public float dnaRadius = 0.32f;
    public Color dnaBackboneColor = new Color(0.55f, 0.65f, 0.75f);
    public Color dnaBasePairColor = new Color(0.3f, 0.75f, 0.9f);
    public float dnaFadeDuration = 0.6f;
    public float dnaHoldDuration = 0.9f;
    public float deletionAnimDuration = 0.8f;
    public float proteinFadeDuration = 1.0f;

    [Header("Gate / Cl- flow (potentiator 성공)")]
    public float gateOpenDuration = 1f;
    public float clFlowEmissionRate = 18f;
    public Color clColor = new Color(0.35f, 0.75f, 1f, 0.85f);

    private bool _isActiveQuest;
    private bool _introPending;
    private bool _stageActive; // wobble/파티클이 매 프레임 갱신되는지
    private float _instability01 = 1f;

    // --- wobble 인덱싱 ---
    private readonly List<Transform> _wobbleTargets = new List<Transform>();
    private readonly List<Vector3> _wobbleHome = new List<Vector3>();
    private readonly List<float> _wobbleWeight = new List<float>();
    private readonly List<AtomInfo> _icl4Atoms = new List<AtomInfo>();
    private float[] _noiseSeeds;
    private Vector3 _anchorLocalPos;
    private bool _hasAnchor;

    // --- QC 파티클 ---
    private ParticleSystem _qcParticles;
    private ParticleSystem.EmissionModule _qcEmission;
    private Coroutine _qcDipRoutine;

    // --- 게이트/Cl- flow ---
    private GameObject _gateRoot;
    private Transform _gateLeft, _gateRight;
    private ParticleSystem _clFlow;

    // --- DNA 인트로 ---
    private GameObject _dnaRoot;
    private readonly List<Renderer> _dnaRenderers = new List<Renderer>();
    private Transform _deletionBeadA, _deletionBeadB, _deletionRod;

    private void Awake()
    {
        if (proteinLoader == null) proteinLoader = FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        if (levelController == null && proteinLoader != null)
            levelController = proteinLoader.GetComponent<StructureLevelController>();
        if (questCatalog == null) questCatalog = FindFirstObjectByType<DockingQuestCatalog>(FindObjectsInactive.Include);
        if (hud == null) hud = FindFirstObjectByType<CftrHUD>(FindObjectsInactive.Include);
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

    private void Update()
    {
        if (!_stageActive) return;
        ApplyWobble();
        ApplyQcParticles();
    }

    // --- 퀘스트 진입/이탈 ---

    private void HandleQuestStarted(DockingQuestDefinition def)
    {
        bool wasActive = _isActiveQuest;
        _isActiveQuest = def != null && def.id == activeForQuestId;

        if (_isActiveQuest)
        {
            _instability01 = 1f;
            _stageActive = false;
            _introPending = true; // 다음 ProteinLoader.OnLoaded(8EJ1)에서 인트로 시퀀스를 재생한다
            if (hud != null)
            {
                hud.gameObject.SetActive(false);
                hud.SetSurfaceCftr(0f, "매우 적음");
                hud.SetChannelActivity(0f, "측정 전");
                hud.HideWarning();
                hud.ShowMessage(string.Empty);
            }
        }
        else if (wasActive)
        {
            _stageActive = false;
            _introPending = false;
            DestroyDna();
            if (hud != null) hud.gameObject.SetActive(false);
            if (_gateRoot != null) { Destroy(_gateRoot); _gateRoot = null; _clFlow = null; }
            if (_qcParticles != null) { Destroy(_qcParticles.gameObject); _qcParticles = null; }
        }
    }

    private void HandleProteinLoaded(ProteinLoader.ProteinData data)
    {
        IndexAtoms();
        if (_introPending && _isActiveQuest)
        {
            _introPending = false;
            StartCoroutine(IntroSequenceRoutine());
        }
    }

    private void HandleLevelChanged(StructureLevelController.ViewLevel level)
    {
        if (!_isActiveQuest) return;
        bool aminoAcid = level == StructureLevelController.ViewLevel.AminoAcid;
        if (hud != null) hud.gameObject.SetActive(aminoAcid && !_introPending);
        if (aminoAcid) ApplyIcl4Tint();
    }

    // --- 인덱싱 / wobble / ICL4 하이라이트 ---

    private void IndexAtoms()
    {
        _wobbleTargets.Clear();
        _wobbleHome.Clear();
        _wobbleWeight.Clear();
        _icl4Atoms.Clear();
        _hasAnchor = false;
        if (proteinLoader == null) return;

        AtomInfo[] atoms = proteinLoader.GetComponentsInChildren<AtomInfo>(true);
        foreach (AtomInfo atom in atoms)
        {
            if (atom.ResidueId == anchorResidueId && atom.AtomName == "CA")
            {
                _anchorLocalPos = atom.transform.localPosition;
                _hasAnchor = true;
            }
        }

        foreach (AtomInfo atom in atoms)
        {
            Transform t = atom.transform;
            _wobbleTargets.Add(t);
            _wobbleHome.Add(t.localPosition);

            float weight = 1f;
            if (_hasAnchor)
            {
                float dist = Vector3.Distance(t.localPosition, _anchorLocalPos);
                weight = Mathf.Clamp01(1f - dist / Mathf.Max(wobbleRadius, 0.01f));
            }
            _wobbleWeight.Add(weight);

            if (atom.ResidueId >= icl4StartResId && atom.ResidueId <= icl4EndResId)
                _icl4Atoms.Add(atom);
        }

        _noiseSeeds = new float[_wobbleTargets.Count];
        for (int i = 0; i < _noiseSeeds.Length; i++) _noiseSeeds[i] = Random.value * 100f;

        if (_hasAnchor)
        {
            if (_qcParticles != null) _qcParticles.transform.localPosition = _anchorLocalPos;
            if (_gateRoot != null) _gateRoot.transform.localPosition = _anchorLocalPos;
        }

        ApplyIcl4Tint();
    }

    /// <summary>TMD2 ICL4 계면을 NBD1 F508 루프(DockingQuestController 포켓 마커가 이미 강조)와
    /// 구분되는 보조색으로 은은하게 틴트한다 — "보조적으로 표시" 요구사항.</summary>
    private void ApplyIcl4Tint()
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var atom in _icl4Atoms)
        {
            if (atom == null) continue;
            var renderer = atom.GetComponent<Renderer>();
            if (renderer == null) continue;
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", icl4HighlightColor);
            mpb.SetColor("_EmissionColor", icl4HighlightColor * 0.5f);
            renderer.SetPropertyBlock(mpb);
        }
    }

    private void ApplyWobble()
    {
        if (_wobbleTargets.Count == 0) return;
        float amplitude = maxWobbleAmplitude * _instability01;
        if (amplitude <= 0.0005f) return;

        for (int i = 0; i < _wobbleTargets.Count; i++)
        {
            Transform t = _wobbleTargets[i];
            if (t == null) continue;
            float w = _wobbleWeight[i];
            if (w <= 0f) continue;

            float seed = _noiseSeeds[i];
            float ox = (Mathf.PerlinNoise(Time.time * wobbleFrequency + seed, 0f) - 0.5f) * 2f;
            float oy = (Mathf.PerlinNoise(0f, Time.time * wobbleFrequency + seed) - 0.5f) * 2f;
            float oz = (Mathf.PerlinNoise(Time.time * wobbleFrequency + seed, seed) - 0.5f) * 2f;

            t.localPosition = _wobbleHome[i] + new Vector3(ox, oy, oz) * (amplitude * w);
        }
    }

    private void SetInstability(float value)
    {
        _instability01 = Mathf.Clamp01(value);
        ApplyQcParticles();
    }

    // --- Ubiquitin / QC particle ---

    private void EnsureQcParticles()
    {
        if (_qcParticles != null || !_hasAnchor || proteinLoader == null) return;

        var go = new GameObject("CftrQcParticles");
        go.transform.SetParent(proteinLoader.transform, false);
        go.transform.localPosition = _anchorLocalPos;

        _qcParticles = go.AddComponent<ParticleSystem>();
        var main = _qcParticles.main;
        main.loop = true;
        main.startLifetime = 2.4f;
        main.startSpeed = 0.06f;
        main.startSize = 0.045f;
        main.startColor = qcParticleColor;
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var shape = _qcParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = wobbleRadius * 0.7f;

        _qcEmission = _qcParticles.emission;
        _qcEmission.rateOverTime = 0f;

        var colorOverLifetime = _qcParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(qcParticleColor, 0f), new GradientColorKey(qcParticleColor, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader != null) renderer.sharedMaterial = new Material(particleShader);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void ApplyQcParticles()
    {
        if (_qcParticles == null) return;
        float capped = Mathf.Min(_instability01, 0.9f);
        _qcEmission.rateOverTime = maxQcEmissionRate * capped;
    }

    /// <summary>Proteasome inhibitor 오답: 분해 신호(ubiquitin icon)만 잠깐 줄고, 접힘 문제(instability01)는
    /// 그대로라 곧 원래 수준으로 되돌아간다 — "버려지는 걸 막아도 접힘은 안 고쳐진다"는 설계 의도.</summary>
    private IEnumerator QcDipRoutine()
    {
        if (_qcParticles == null) yield break;
        _qcEmission.rateOverTime = maxQcEmissionRate * 0.15f;
        yield return new WaitForSeconds(3.5f);
        ApplyQcParticles();
    }

    private IEnumerator BurstEffect(Vector3 worldPos, Color color, float maxScale, float duration)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.position = worldPos;

        var renderer = sphere.GetComponent<Renderer>();
        var mpb = new MaterialPropertyBlock();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            sphere.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, maxScale, k);
            Color c = Color.Lerp(color, color * 0.1f, k);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_EmissionColor", c * 2.5f);
            renderer.SetPropertyBlock(mpb);
            yield return null;
        }
        Destroy(sphere);
    }

    // --- 화합물 결과 반응 (DockingQuestController가 호출) ---

    public void HandleCompoundSuccess(string compoundId)
    {
        if (!_isActiveQuest) return;
        switch (compoundId)
        {
            case "corrector_pair":
                SetInstability(0.15f);
                if (hud != null)
                {
                    hud.SetSurfaceCftr(0.75f, "많음 (증가!)");
                    hud.ShowMessage("교정제 덕분에 단백질이 더 잘 접히고 조각들도 잘 붙었어요! 세포 표면의 CFTR이 늘었어요.");
                }
                StartCoroutine(SwapToStructureRoutine("structures/8EIQ.json", 0.8f));
                break;

            case "ivacaftor_like":
                SetInstability(0.05f);
                if (hud != null)
                {
                    hud.SetChannelActivity(0.9f, "활발함 (증가!)");
                    hud.ShowMessage("채널이 더 활발하게 열려요!");
                }
                StartCoroutine(OpenGateAndFlowRoutine());
                break;
        }
    }

    public void HandleCompoundFailure(string compoundId, DockingOutcome outcome)
    {
        if (!_isActiveQuest) return;
        switch (compoundId)
        {
            case "lumacaftor_like":
                SetInstability(0.55f);
                if (hud != null)
                {
                    hud.SetSurfaceCftr(0.35f, "적음 → 일부 회복");
                    hud.ShowMessage("일부만 좋아졌어요. 더 효과적인 교정제 조합이 필요해요.");
                }
                break;

            case "proteasome_inhibitor_like":
                if (_qcDipRoutine != null) StopCoroutine(_qcDipRoutine);
                _qcDipRoutine = StartCoroutine(QcDipRoutine());
                if (_hasAnchor && proteinLoader != null)
                    StartCoroutine(BurstEffect(proteinLoader.transform.TransformPoint(_anchorLocalPos), erStressColor, 0.5f, 0.6f));
                if (hud != null)
                {
                    hud.ShowWarning("세포 스트레스 경고: 처리 못 한 단백질이 쌓이고 있어요");
                    hud.ShowMessage("불량 단백질을 못 버리게 막았을 뿐, 접힘 문제는 그대로예요.");
                }
                break;

            case "kras_g12c_inhibitor_like":
                if (hud != null) hud.ShowMessage("약의 종류는 맞지만, 이 병에 맞는 표적이 아니에요.");
                break;
        }
    }

    public void HandleOrderError(string compoundId)
    {
        if (!_isActiveQuest) return;
        if (hud != null)
            hud.ShowMessage("채널을 여는 약만으로는 부족해요. 세포 표면에 CFTR이 아직 너무 적어요 — 교정제를 먼저 써야 해요.");
    }

    // --- 구조 스왑 (corrector 성공: 8EJ1 → 8EIQ) ---

    private IEnumerator SwapToStructureRoutine(string relativePath, float fadeDuration)
    {
        yield return proteinLoader.FadeOutRoutine(fadeDuration);

        bool loaded = false;
        void OnLoadedOnce(ProteinLoader.ProteinData d) { loaded = true; }
        proteinLoader.OnLoaded += OnLoadedOnce;
        proteinLoader.LoadStructure(relativePath);
        while (!loaded) yield return null;
        proteinLoader.OnLoaded -= OnLoadedOnce;

        yield return proteinLoader.FadeInRoutine(fadeDuration);
    }

    // --- Gate opening + Cl- flow (potentiator 성공) ---

    private void EnsureGate()
    {
        if (_gateRoot != null || proteinLoader == null || !_hasAnchor) return;

        Vector3 outward = _anchorLocalPos.sqrMagnitude > 1e-4f ? _anchorLocalPos.normalized : Vector3.forward;

        _gateRoot = new GameObject("CftrGate");
        _gateRoot.transform.SetParent(proteinLoader.transform, false);
        _gateRoot.transform.localPosition = _anchorLocalPos;
        _gateRoot.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);

        _gateLeft = BuildGateFlap("GateLeft", Vector3.left * 0.02f);
        _gateRight = BuildGateFlap("GateRight", Vector3.right * 0.02f);
    }

    private Transform BuildGateFlap(string name, Vector3 closedLocalOffset)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(_gateRoot.transform, false);
        go.transform.localScale = new Vector3(0.18f, 0.35f, 0.03f);
        go.transform.localPosition = closedLocalOffset;

        RuntimeMaterials.ApplySolid(go);
        var renderer = go.GetComponent<Renderer>();
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", new Color(0.6f, 0.7f, 0.8f));
        renderer.SetPropertyBlock(mpb);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private IEnumerator OpenGateAndFlowRoutine()
    {
        EnsureGate();
        if (_gateRoot == null) yield break;

        Vector3 leftClosed = _gateLeft.localPosition, rightClosed = _gateRight.localPosition;
        Vector3 leftOpen = leftClosed + Vector3.left * 0.22f;
        Vector3 rightOpen = rightClosed + Vector3.right * 0.22f;

        float elapsed = 0f;
        while (elapsed < gateOpenDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / gateOpenDuration));
            _gateLeft.localPosition = Vector3.Lerp(leftClosed, leftOpen, k);
            _gateRight.localPosition = Vector3.Lerp(rightClosed, rightOpen, k);
            yield return null;
        }

        EnsureClFlow();
    }

    private void EnsureClFlow()
    {
        if (_clFlow != null)
        {
            var em = _clFlow.emission;
            em.rateOverTime = clFlowEmissionRate;
            return;
        }
        if (_gateRoot == null) return;

        var go = new GameObject("CftrClFlow");
        go.transform.SetParent(_gateRoot.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        _clFlow = go.AddComponent<ParticleSystem>();
        var main = _clFlow.main;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0.9f;
        main.startSize = 0.035f;
        main.startColor = clColor;
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var shape = _clFlow.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.02f;

        var emission = _clFlow.emission;
        emission.rateOverTime = clFlowEmissionRate;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader != null) renderer.sharedMaterial = new Material(particleShader);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // --- 인트로: DNA → F508 결실(pre-baked) → 단백질 ---

    private IEnumerator IntroSequenceRoutine()
    {
        // 새로 로드된 8EJ1은 이미 보이는 상태 — 즉시 숨긴다 (duration 0 = 스냅)
        yield return proteinLoader.FadeOutRoutine(0f);

        BuildDna();
        yield return FadeDnaRoutine(0f, 1f, dnaFadeDuration);
        yield return new WaitForSeconds(dnaHoldDuration * 0.5f);

        yield return PlayDeletionRoutine();
        yield return new WaitForSeconds(dnaHoldDuration * 0.5f);

        StartCoroutine(FadeDnaRoutine(1f, 0f, proteinFadeDuration));
        yield return proteinLoader.FadeInRoutine(proteinFadeDuration);
        DestroyDna();

        _stageActive = true;
        EnsureQcParticles();
        ApplyQcParticles();
        if (hud != null)
        {
            hud.gameObject.SetActive(true);
            hud.SetSurfaceCftr(0.05f, "매우 적음");
            hud.ShowMessage("F508 자리가 사라지면서 단백질 조각들이 서로 잘 안 맞물려요.");
        }

        yield return new WaitForSeconds(3f);
        if (hud != null) hud.ShowMessage("지금 배우는 순서는 실제 병원에서 약을 쓰는 순서와는 달라요.");
        yield return new WaitForSeconds(3f);
        if (hud != null) hud.ShowMessage(string.Empty);
    }

    private void BuildDna()
    {
        DestroyDna();
        _dnaRoot = new GameObject("CftrDnaIntro");
        _dnaRoot.transform.SetParent(proteinLoader.transform, false);

        float turns = dnaBasePairCount / 10f; // 실제 DNA는 약 10.5 bp/turn
        int deletionIndex = (dnaBasePairCount / 2) / 2 * 2; // 짝수(염기쌍 가로대가 있는 인덱스)로 맞춤

        for (int i = 0; i < dnaBasePairCount; i++)
        {
            float tNorm = i / (float)Mathf.Max(dnaBasePairCount - 1, 1);
            float y = Mathf.Lerp(-dnaHelixLength * 0.5f, dnaHelixLength * 0.5f, tNorm);
            float angle = tNorm * turns * Mathf.PI * 2f;

            Vector3 a = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dnaRadius + Vector3.up * y;
            Vector3 b = new Vector3(Mathf.Cos(angle + Mathf.PI), 0f, Mathf.Sin(angle + Mathf.PI)) * dnaRadius + Vector3.up * y;

            Transform beadA = SpawnDnaBead(a, 0.045f, dnaBackboneColor);
            Transform beadB = SpawnDnaBead(b, 0.045f, dnaBackboneColor);

            if (i % 2 == 0)
            {
                Transform rod = SpawnDnaRod(a, b, 0.018f, dnaBasePairColor);
                if (i == deletionIndex) { _deletionBeadA = beadA; _deletionBeadB = beadB; _deletionRod = rod; }
            }
        }
    }

    private Transform SpawnDnaBead(Vector3 localPos, float scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(_dnaRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * scale;
        TintDnaPart(go, color);
        return go.transform;
    }

    private Transform SpawnDnaRod(Vector3 a, Vector3 b, float radius, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(_dnaRoot.transform, false);
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        go.transform.localScale = new Vector3(radius, Vector3.Distance(a, b) * 0.5f, radius);
        TintDnaPart(go, color);
        return go.transform;
    }

    private void TintDnaPart(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = RuntimeMaterials.Transparent; // 알파 페이드를 쓰기 위해 처음부터 투명 재질
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var mpb = new MaterialPropertyBlock();
        Color c = color; c.a = 0f; // FadeDnaRoutine이 0→1로 올린다
        mpb.SetColor("_BaseColor", c);
        renderer.SetPropertyBlock(mpb);
        _dnaRenderers.Add(renderer);
    }

    private IEnumerator FadeDnaRoutine(float from, float to, float duration)
    {
        if (_dnaRenderers.Count == 0) yield break;
        var mpb = new MaterialPropertyBlock();

        void Apply(float alpha)
        {
            foreach (var r in _dnaRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                Color c = mpb.GetColor("_BaseColor");
                c.a = alpha;
                mpb.SetColor("_BaseColor", c);
                r.SetPropertyBlock(mpb);
            }
        }

        Apply(from);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Apply(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        Apply(to);
    }

    /// <summary>508번 코돈이 통째로 빠지는 걸 섬광+축소로 표현하는 pre-baked 연출.
    /// 실제 co-translational folding/deletion 시뮬레이션이 아니라 정해진 순서로 재생되는 애니메이션이다.</summary>
    private IEnumerator PlayDeletionRoutine()
    {
        if (_deletionBeadA == null) yield break;

        Vector3 scaleA = _deletionBeadA.localScale;
        Vector3 scaleB = _deletionBeadB.localScale;
        Vector3 scaleR = _deletionRod != null ? _deletionRod.localScale : Vector3.one;

        float flashDuration = deletionAnimDuration * 0.35f;
        StartCoroutine(ScaleTo(_deletionBeadA, scaleA * 2.2f, flashDuration));
        StartCoroutine(ScaleTo(_deletionBeadB, scaleB * 2.2f, flashDuration));
        if (_deletionRod != null) StartCoroutine(ScaleTo(_deletionRod, scaleR * 1.6f, flashDuration));
        yield return new WaitForSeconds(flashDuration);

        float vanishDuration = deletionAnimDuration * 0.45f;
        StartCoroutine(ScaleTo(_deletionBeadA, Vector3.zero, vanishDuration));
        StartCoroutine(ScaleTo(_deletionBeadB, Vector3.zero, vanishDuration));
        if (_deletionRod != null) StartCoroutine(ScaleTo(_deletionRod, Vector3.zero, vanishDuration));
        yield return new WaitForSeconds(vanishDuration);
    }

    private static IEnumerator ScaleTo(Transform t, Vector3 target, float duration)
    {
        if (t == null) yield break;
        Vector3 start = t.localScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (t == null) yield break;
            elapsed += Time.deltaTime;
            t.localScale = Vector3.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        if (t != null) t.localScale = target;
    }

    private void DestroyDna()
    {
        if (_dnaRoot != null) Destroy(_dnaRoot);
        _dnaRoot = null;
        _dnaRenderers.Clear();
        _deletionBeadA = _deletionBeadB = _deletionRod = null;
    }
}
