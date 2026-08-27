using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 사건 4(CFTR F508del) 마무리 연출 — potentiator(Ivacaftor-like) 도킹 성공 직후:
///   화면 페이드 → 상피세포 표면 "도착 화면"(Level 3&4) 구성:
///     airway surface liquid(ASL) 레이어가 차오르고, 그 위 점액 레이어가 묽어지고(옅어지고),
///     섬모(cilia)가 파동형으로 다시 움직이기 시작한다.
///
/// P53QuestDirector(사건 5 마무리 연출)와 같은 자리 — DockingQuestController.OnDockingFinished를
/// 직접 구독해 반응하고, 게이트/Cl- flow 같은 국소 연출은 CftrRescueController가 이미 담당하므로
/// 여기서는 "넓은 장면"만 만든다. 실제 ER→Golgi→vesicle escort 시뮬레이션은 만들지 않는다.
/// </summary>
public class CftrFinaleController : MonoBehaviour
{
    [Header("참조")]
    public DockingQuestController dockingController;
    public ProteinLoader proteinLoader;
    public CftrHUD hud;
    [Tooltip("이 화합물 id의 도킹이 Success일 때만 마무리 연출을 시작한다 (potentiator)")]
    public string finaleCompoundId = "ivacaftor_like";

    [Header("타이밍(초)")]
    public float holdAfterDockingSeconds = 1.5f;
    public float fadeDuration = 0.6f;
    public float aslRiseDuration = 2f;
    public float mucusThinDuration = 2f;
    public float ciliaSpinUpDuration = 1.5f;
    public float sceneHoldSeconds = 3f;

    [Header("상피세포 표면 연출")]
    public Vector2 layerSize = new Vector2(2.4f, 2.4f);
    public Color aslColor = new Color(0.3f, 0.7f, 1f, 0.45f);
    public Color mucusColorBefore = new Color(0.55f, 0.7f, 0.35f, 0.75f);
    public Color mucusColorAfter = new Color(0.75f, 0.85f, 0.7f, 0.25f);
    public int ciliaCount = 24;
    public float ciliaHeight = 0.22f;
    public float ciliaSwayDegrees = 22f;
    public float ciliaSwaySpeed = 3.2f;

    private CanvasGroup _fadeOverlay;
    private bool _finalePlayed;
    private GameObject _sceneRoot;
    private Transform _aslLayer, _mucusLayer;
    private readonly List<Transform> _cilia = new List<Transform>();
    private Coroutine _ciliaSwayRoutine;

    private void Awake()
    {
        if (dockingController == null) dockingController = FindFirstObjectByType<DockingQuestController>(FindObjectsInactive.Include);
        if (proteinLoader == null) proteinLoader = FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        if (hud == null) hud = FindFirstObjectByType<CftrHUD>(FindObjectsInactive.Include);

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
        if (result.Compound == null || result.Compound.id != finaleCompoundId) return;

        _finalePlayed = true;
        StartCoroutine(FinaleRoutine());
    }

    private IEnumerator FinaleRoutine()
    {
        yield return new WaitForSeconds(holdAfterDockingSeconds);

        yield return Fade(0f, 1f);
        BuildEpitheliumScene();
        yield return Fade(1f, 0f);

        yield return AslRiseRoutine();
        yield return MucusThinRoutine();
        _ciliaSwayRoutine = StartCoroutine(CiliaSwayRoutine());

        if (hud != null)
            hud.ShowMessage("교정제와 채널을 여는 약을 함께 쓰면 CFTR이 제자리를 찾고, 채널도 열리고, 점액도 다시 잘 빠져나가요.");

        yield return new WaitForSeconds(sceneHoldSeconds);
    }

    // --- 상피세포 표면 장면 ---

    private void BuildEpitheliumScene()
    {
        if (_sceneRoot != null) return;
        Transform anchor = proteinLoader != null ? proteinLoader.transform : transform;

        _sceneRoot = new GameObject("CftrEpitheliumScene");
        _sceneRoot.transform.SetParent(anchor.parent != null ? anchor.parent : anchor, false);
        _sceneRoot.transform.position = anchor.position + anchor.forward * 1.6f;
        _sceneRoot.transform.rotation = Quaternion.LookRotation(anchor.forward, Vector3.up);

        _aslLayer = BuildLayer("AslLayer", 0.02f, aslColor);
        _mucusLayer = BuildLayer("MucusLayer", 0.12f, mucusColorBefore);
        _mucusLayer.localPosition = new Vector3(0f, 0.1f, 0f);

        BuildCilia();
    }

    private Transform BuildLayer(string name, float startThickness, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(_sceneRoot.transform, false);
        go.transform.localScale = new Vector3(layerSize.x, startThickness, layerSize.y);
        go.transform.localPosition = new Vector3(0f, startThickness * 0.5f, 0f);

        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = RuntimeMaterials.Transparent;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(mpb);
        return go.transform;
    }

    private void BuildCilia()
    {
        _cilia.Clear();
        var rng = new System.Random(12345);
        int perSide = Mathf.CeilToInt(Mathf.Sqrt(ciliaCount));
        int spawned = 0;

        for (int ix = 0; ix < perSide && spawned < ciliaCount; ix++)
        {
            for (int iz = 0; iz < perSide && spawned < ciliaCount; iz++, spawned++)
            {
                float x = Mathf.Lerp(-layerSize.x * 0.4f, layerSize.x * 0.4f, ix / (float)Mathf.Max(perSide - 1, 1));
                float z = Mathf.Lerp(-layerSize.y * 0.4f, layerSize.y * 0.4f, iz / (float)Mathf.Max(perSide - 1, 1));

                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"Cilium_{spawned}";
                Destroy(go.GetComponent<Collider>());
                var pivot = new GameObject($"CiliumPivot_{spawned}");
                pivot.transform.SetParent(_sceneRoot.transform, false);
                pivot.transform.localPosition = new Vector3(x, 0f, z);

                go.transform.SetParent(pivot.transform, false);
                go.transform.localScale = new Vector3(0.02f, ciliaHeight * 0.5f, 0.02f);
                go.transform.localPosition = new Vector3(0f, ciliaHeight * 0.5f, 0f);

                var renderer = go.GetComponent<Renderer>();
                RuntimeMaterials.ApplySolid(go);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(0.85f, 0.7f, 0.5f));
                renderer.SetPropertyBlock(mpb);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                pivot.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                _cilia.Add(pivot.transform);
            }
        }
    }

    private IEnumerator AslRiseRoutine()
    {
        if (_aslLayer == null) yield break;
        float targetThickness = 0.16f;
        Vector3 startScale = _aslLayer.localScale;
        Vector3 targetScale = new Vector3(startScale.x, targetThickness, startScale.z);

        float elapsed = 0f;
        while (elapsed < aslRiseDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / aslRiseDuration));
            float thickness = Mathf.Lerp(startScale.y, targetScale.y, k);
            _aslLayer.localScale = new Vector3(startScale.x, thickness, startScale.z);
            _aslLayer.localPosition = new Vector3(0f, thickness * 0.5f, 0f);
            if (_mucusLayer != null) _mucusLayer.localPosition = new Vector3(0f, thickness + 0.06f, 0f);
            yield return null;
        }
    }

    private IEnumerator MucusThinRoutine()
    {
        if (_mucusLayer == null) yield break;
        var renderer = _mucusLayer.GetComponent<Renderer>();
        var mpb = new MaterialPropertyBlock();
        Vector3 startScale = _mucusLayer.localScale;
        Vector3 targetScale = new Vector3(startScale.x, startScale.y * 0.4f, startScale.z);

        float elapsed = 0f;
        while (elapsed < mucusThinDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / mucusThinDuration);
            _mucusLayer.localScale = Vector3.Lerp(startScale, targetScale, k);
            Color c = Color.Lerp(mucusColorBefore, mucusColorAfter, k);
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            renderer.SetPropertyBlock(mpb);
            yield return null;
        }
    }

    private IEnumerator CiliaSwayRoutine()
    {
        float phaseStep = 0.4f;
        float startTime = Time.time;
        while (true)
        {
            // 멎어 있던 섬모가 갑자기 최대 진폭으로 튀지 않도록, 다시 움직이기 시작하는 처음
            // ciliaSpinUpDuration 동안만 진폭을 0→1로 서서히 끌어올린다.
            float amplitude01 = ciliaSpinUpDuration > 0f
                ? Mathf.Clamp01((Time.time - startTime) / ciliaSpinUpDuration)
                : 1f;

            for (int i = 0; i < _cilia.Count; i++)
            {
                if (_cilia[i] == null) continue;
                float wave = Mathf.Sin(Time.time * ciliaSwaySpeed + i * phaseStep);
                Vector3 euler = _cilia[i].localEulerAngles;
                _cilia[i].localRotation = Quaternion.Euler(wave * ciliaSwayDegrees * amplitude01, euler.y, 0f);
            }
            yield return null;
        }
    }

    // --- 화면 페이드 ---

    private void BuildFadeOverlay()
    {
        var canvasGo = new GameObject("CftrFadeCanvas");
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
