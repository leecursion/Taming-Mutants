#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 메뉴 [Tools/Taming Mutants/AI 비서 생성]으로 씬에 AI 비서 캐릭터를 조립한다.
///
/// 외부 에셋 없이 Unity 기본 도형만 사용한다:
///   몸통(구) + 바이저(눌린 구) + 눈 2개 + 안테나 + 궤도 링 2개 + 포인트 라이트
/// 링만 기본 도형에 없어서(토러스 없음) 메시를 직접 생성해 에셋으로 저장한다.
///
/// 나중에 실제 3D 모델로 교체할 때는 도형들만 지우고
/// AIAssistantVisual.targetRenderers / AIAssistantFace의 눈 참조만 다시 지정하면 된다.
/// </summary>
public static class AIAssistantSetupMenu
{
    private const string MaterialFolder = "Assets/Materials";
    private const string MeshFolder = "Assets/Meshes";
    private const string VisorMaterialPath = MaterialFolder + "/AIAssistant_Visor.mat";
    private const string GlowMaterialPath = MaterialFolder + "/AIAssistant_Glow.mat";
    private const string ShellMaterialPath = MaterialFolder + "/AIAssistant_Shell.mat";
    private const string RingMaterialPath = MaterialFolder + "/AIAssistant_Ring.mat";
    private const string MoteMaterialPath = MaterialFolder + "/AIAssistant_Mote.mat";
    private const string HighlightMaterialPath = MaterialFolder + "/AIAssistant_Highlight.mat";
    private const string RingMeshPath = MeshFolder + "/AIAssistantRing.asset";
    private const string CrystalMeshPath = MeshFolder + "/AIAssistantCrystal.asset";

    // 몸통 지름 0.12m, 링까지 포함해 전체 약 0.25m.
    // 기본 배치 거리(0.9m)에서 시야각 약 15도 — 눈에 띄지만 시야를 가리지는 않는 크기.
    private const float BodyDiameter = 0.12f;
    private const float BodyRadius = BodyDiameter * 0.5f;
    private const float RingMajorRadius = 0.115f;
    private const float RingMinorRadius = 0.005f;
    // 결정은 반드시 바이저 뒤(z < 0.019)에 들어가야 서로 뚫고 지나가지 않는다.
    // 바이저를 얇은 렌즈로 만들어 셸 안쪽 공간을 비워둔 뒤 그 뒤에 넣는다.
    private const float CrystalRadius = 0.032f;
    private const float CrystalCenterZ = -0.016f;
    private static readonly Vector3 VisorCenter = new Vector3(0f, 0.004f, 0.034f);
    private static readonly Vector3 VisorScale = new Vector3(0.088f, 0.058f, 0.032f);
    private const float EyeCenterZ = 0.046f;

    // 분자 옆에 세울 때의 확대 배율. 분자가 카메라에서 약 3m 앞에 있어
    // 등배로 두면 사용자 옆에 있을 때보다 훨씬 작게 보인다.
    private const float AnchorModeScale = 1.25f;

    // 말풍선: 캔버스 300unit × 스케일 0.001 = 실제 폭 0.30m.
    // 캔버스를 크게 잡고 잘게 축소해야 글자가 픽셀 단위로 뭉개지지 않는다.
    private const float BubbleWidth = 300f;
    private const float BubbleCanvasScale = 0.001f;
    private const float BubblePivotX = 0.94f;
    // 말풍선을 "화면에서 일정한 크기"로 유지하는 기준 카메라 거리(m).
    // 아래 치수들은 이 거리에서 읽기 좋도록 잡은 값이고, 실제 거리 보정은
    // AIAssistantSpeechBubble이 실행 중에 맡는다.
    private const float BubbleReferenceDistance = 2f;

    // 분자 옆에 세우면 카메라에서 약 2m 떨어지므로, 사용자 옆에 있을 때와 같은 치수로는
    // 글자가 화면에서 너무 작아 읽을 수 없다. 캔버스 단위 치수를 이 배율로 키운다.
    // (캔버스 스케일이 아니라 치수를 키워야 글자를 굽는 해상도가 정상 범위에 머문다.)
    private const float AnchoredBubbleUiScale = 2.6f;

    private const string TextureFolder = "Assets/Textures/UI";
    private const string PanelSpritePath = TextureFolder + "/AIAssistantPanel.png";
    private const string StrokeSpritePath = TextureFolder + "/AIAssistantPanelStroke.png";
    private const string GlowSpritePath = TextureFolder + "/AIAssistantPanelGlow.png";
    private const string CircleSpritePath = TextureFolder + "/AIAssistantDot.png";

    [MenuItem("Tools/Taming Mutants/AI 비서 생성")]
    public static void CreateAssistant()
    {
        // 이제 비서는 편집 모드에서 비활성 상태로 저장되므로 비활성까지 뒤져야 기존 것을 찾는다.
        var existing = Object.FindFirstObjectByType<AIAssistantFollower>(FindObjectsInactive.Include);
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "AI 비서",
                "씬에 이미 AI 비서가 있습니다. 기존 것을 지우고 새로 만들까요?",
                "새로 만들기", "취소");

            if (!replace)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        Material visorMaterial = LoadOrCreateVisorMaterial();
        Material glowMaterial = LoadOrCreateGlowMaterial();
        Material shellMaterial = LoadOrCreateShellMaterial();
        Material ringMaterial = LoadOrCreateRingMaterial();
        Material moteMaterial = LoadOrCreateMoteMaterial();
        Material highlightMaterial = LoadOrCreateHighlightMaterial();
        Mesh ringMesh = LoadOrCreateRingMesh();
        Mesh crystalMesh = LoadOrCreateCrystalMesh();

        var root = new GameObject("AIAssistant");
        Undo.RegisterCreatedObjectUndo(root, "AI 비서 생성");

        // --- 몸통: 반투명 에너지 셸 + 그 안에서 도는 결정 코어 ---
        // 단색 구 하나로는 밋밋해서, 안이 비치는 껍질과 안쪽 결정의 두 겹으로 만든다.
        // 원자와 궤도라는 이 앱의 시각 언어를 비서 자신이 그대로 닮게 한 구성이기도 하다.
        Renderer body = CreatePart(root.transform, "Shell", PrimitiveType.Sphere,
            Vector3.zero, Vector3.one * BodyDiameter, shellMaterial);

        Renderer crystal = CreateMeshPart(root.transform, "Crystal", crystalMesh, glowMaterial,
            new Vector3(0f, 0f, CrystalCenterZ), Quaternion.Euler(18f, 0f, 12f));
        // 결정이 천천히 돌아야 각 면이 번갈아 빛을 받아 살아 있는 느낌이 난다.
        var crystalSpin = crystal.gameObject.AddComponent<AIAssistantSpin>();
        crystalSpin.localAxis = new Vector3(0.25f, 1f, 0.15f);
        crystalSpin.degreesPerSecond = 14f;

        // --- 얼굴 ---
        // Face는 스케일 1인 빈 오브젝트. 눈을 바이저의 자식으로 두면 바이저의 비균등
        // 스케일이 눈까지 상속돼 깜빡임 계산이 어긋나므로 형제로 둔다.
        var face = new GameObject("Face");
        face.transform.SetParent(root.transform, false);

        // 얇은 렌즈 형태로 셸 안쪽에 넣는다. 셸이 거의 투명해서 유리 돔 아래 얼굴이 있는
        // 것처럼 보이고, 뒤쪽 공간이 비어 결정 코어가 들어갈 자리가 생긴다.
        CreatePart(face.transform, "Visor", PrimitiveType.Sphere,
            VisorCenter, VisorScale, visorMaterial);

        Renderer eyeLeft = CreateEye(face.transform, "Eye_L", -0.019f, glowMaterial, highlightMaterial);
        Renderer eyeRight = CreateEye(face.transform, "Eye_R", 0.019f, glowMaterial, highlightMaterial);

        // --- 안테나 ---
        // 기본 Cylinder는 높이가 2unit이라 scale.y = 0.02면 실제 높이 0.04m.
        Renderer antenna = CreatePart(root.transform, "Antenna", PrimitiveType.Cylinder,
            new Vector3(0f, BodyRadius + 0.02f, 0f),
            new Vector3(0.004f, 0.02f, 0.004f), shellMaterial);

        Renderer antennaTip = CreatePart(root.transform, "AntennaTip", PrimitiveType.Sphere,
            new Vector3(0f, BodyRadius + 0.044f, 0f), Vector3.one * 0.018f, glowMaterial);

        // --- 궤도 링 ---
        Renderer ringA = CreateRing(root.transform, "Ring_A", ringMesh, ringMaterial,
            Quaternion.Euler(8f, 0f, 0f), 40f);
        Renderer ringB = CreateRing(root.transform, "Ring_B", ringMesh, ringMaterial,
            Quaternion.Euler(0f, 0f, 68f), -28f);

        // --- 주변 입자 ---
        ParticleSystemRenderer motes = CreateMotes(root.transform, moteMaterial);

        // --- 조명 ---
        var lightGo = new GameObject("GlowLight");
        lightGo.transform.SetParent(root.transform, false);
        Light glow = lightGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 1.5f;
        glow.intensity = 1.5f;
        glow.color = new Color(0.2f, 0.8f, 1f);
        glow.shadows = LightShadows.None; // 원자 2천 개짜리 씬에 그림자 광원을 더하지 않는다

        // --- 컴포넌트 배선 ---
        var follower = root.AddComponent<AIAssistantFollower>();
        if (Camera.main != null) follower.followTarget = Camera.main.transform;

        // 씬에 단백질이 있으면 사용자 시야가 아니라 분자 옆자리를 기본 배치로 삼는다.
        var proteinLoader = Object.FindFirstObjectByType<ProteinLoader>();
        if (proteinLoader != null)
        {
            follower.anchorTarget = proteinLoader.transform;
            // 분자는 카메라에서 3m 넘게 떨어져 있어 원래 크기로는 너무 작게 보인다.
            // 겉보기 크기가 사용자 옆에 있을 때와 비슷해지도록 키운다.
            root.transform.localScale = Vector3.one * AnchorModeScale;
        }

        if (follower.followTarget != null) follower.SnapToAnchor();

        var visual = root.AddComponent<AIAssistantVisual>();
        // 몸통·결정·눈·안테나·링·주변 입자까지 전부 상태 색을 따라간다.
        // 바이저(어두운 배경)와 눈 하이라이트(흰 반사점)만 제외한다 — 이 둘은
        // 대비를 만드는 역할이라 같이 물들면 형태가 뭉개진다.
        visual.targetRenderers = new Renderer[]
        {
            body, crystal, eyeLeft, eyeRight, antenna, antennaTip, ringA, ringB, motes
        };
        visual.glowLight = glow;

        var faceComponent = root.AddComponent<AIAssistantFace>();
        faceComponent.visual = visual;
        faceComponent.eyeLeft = eyeLeft.transform;
        faceComponent.eyeRight = eyeRight.transform;
        if (Camera.main != null) faceComponent.lookTarget = Camera.main.transform;

        // 말풍선은 비서 스케일이 아니라 "읽을 수 있는 크기"로 따로 정한다.
        // 분자 옆(멀리)에 세운 경우에만 크게 키운다.
        AIAssistantSpeechBubble bubble = CreateSpeechBubble(root.transform, visual,
            proteinLoader != null ? AnchoredBubbleUiScale : 1f);

        var tester = root.AddComponent<AIAssistantStateTester>();
        tester.visual = visual;
        tester.bubble = bubble;

        // Play 버튼을 누르기 전(에디터 편집 모드)에는 비서가 보이지 않아야 한다.
        // IntroDirector.Awake()가 Play 시작 시점에 다시 켠다.
        root.SetActive(false);

        Selection.activeGameObject = root;
        Debug.Log("[AIAssistantSetup] AI 비서 캐릭터를 생성했습니다(편집 모드에서는 비활성). " +
                  "Play 후 숫자키 1~5로 상태 전환, Space로 말풍선 테스트.");
    }

    // --- 파츠 생성 ---

    private static Renderer CreatePart(Transform parent, string name, PrimitiveType type,
                                       Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        // 콜라이더를 남겨두면 MouseWorldSelector의 레이캐스트가 비서에 먼저 맞아
        // 뒤쪽 원자 선택이 막힌다 (히트한 오브젝트에 AtomInfo가 없으면 그대로 return하므로).
        Object.DestroyImmediate(go.GetComponent<Collider>());

        var renderer = go.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (material != null) renderer.sharedMaterial = material;
        return renderer;
    }

    /// <summary>직접 만든 메시로 파츠를 만든다 (결정 코어처럼 기본 도형에 없는 모양).</summary>
    private static Renderer CreateMeshPart(Transform parent, string name, Mesh mesh, Material material,
                                           Vector3 localPosition, Quaternion localRotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    /// <summary>
    /// 눈 하나. 흰 반사점을 자식으로 붙여 유리알처럼 보이게 한다.
    /// 자식이라 깜빡일 때 눈과 함께 눌리므로 따로 애니메이션할 필요가 없다.
    /// </summary>
    private static Renderer CreateEye(Transform parent, string name, float offsetX,
                                      Material eyeMaterial, Material highlightMaterial)
    {
        // 눈은 바이저 앞면을 뚫고 살짝 나오되 셸 표면(반지름 0.06) 안에는 머물러야 한다.
        Renderer eye = CreatePart(parent, name, PrimitiveType.Sphere,
            new Vector3(offsetX, 0.004f, EyeCenterZ), Vector3.one * 0.022f, eyeMaterial);

        // 좌표는 눈의 로컬 기준(반지름 0.5). 표면 바깥으로 살짝 나오게 배치해야 가려지지 않는다.
        CreatePart(eye.transform, "Highlight", PrimitiveType.Sphere,
            new Vector3(-0.22f, 0.25f, 0.35f), Vector3.one * 0.28f, highlightMaterial);

        return eye;
    }

    /// <summary>
    /// 비서 주위를 느리게 떠다니는 입자. 텍스처 없이 전용 셰이더로 그린다.
    /// ParticleSystemRenderer도 Renderer라서 AIAssistantVisual의 targetRenderers에 넣으면
    /// 다른 파츠와 똑같이 MaterialPropertyBlock으로 상태 색이 입혀진다.
    /// </summary>
    private static ParticleSystemRenderer CreateMotes(Transform parent, Material material)
    {
        var go = new GameObject("Motes");
        go.transform.SetParent(parent, false);

        var particles = go.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.loop = true;
        main.startLifetime = 4f;
        main.startSpeed = 0.015f;
        main.startSize = 0.009f;
        // 흰색으로 둬야 머티리얼에 입혀지는 상태 색이 그대로 나온다.
        // 여기에 청록색을 넣으면 Alert(빨강)일 때도 색이 섞여 탁해진다.
        main.startColor = Color.white;
        main.maxParticles = 48;
        // Local이라야 비서를 따라 움직일 때 입자가 뒤에 흘리지 않고 함께 붙어 다닌다.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = particles.emission;
        emission.rateOverTime = 9f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        shape.radiusThickness = 0.35f; // 껍질 쪽에서 주로 생겨 몸통 안이 지저분해지지 않게

        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.035f;
        noise.frequency = 0.5f;

        // 생겼다 사라지는 걸 알파로 처리해야 입자가 툭 끊기지 않는다.
        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private static Renderer CreateRing(Transform parent, string name, Mesh mesh, Material material,
                                       Quaternion localRotation, float degreesPerSecond)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = localRotation;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var spin = go.AddComponent<AIAssistantSpin>();
        spin.localAxis = Vector3.up;
        spin.degreesPerSecond = degreesPerSecond;

        return renderer;
    }

    // --- 말풍선 ---

    /// <summary>
    /// 비서 아래쪽에 펼쳐지는 World Space 정보 패널을 만든다.
    ///
    /// 구성: 외곽 글로우 / 둥근 패널 / 강조 외곽선 / 비서까지 잇는 연결선 /
    ///       헤더(상태 점 + 라벨) / 구분선 / 본문.
    /// 배경·외곽선·글로우 스프라이트는 기본 UI 스킨 대신 직접 그려서 굽는다
    /// (기본 스킨은 저해상도 회색이라 홀로그램 톤과 맞지 않는다).
    /// </summary>
    /// <param name="uiScale">
    /// 말풍선을 물리적으로 몇 배 크게 만들지. 캔버스 스케일이 아니라 캔버스 단위 치수
    /// (폭·폰트 크기·여백)를 곱해서 키운다. 스케일로 키우면 글자를 굽는 해상도가 같이 올라가
    /// 폰트 아틀라스가 넘치는 순간 글자가 깨진다.
    /// </param>
    private static AIAssistantSpeechBubble CreateSpeechBubble(Transform parent, AIAssistantVisual visual, float uiScale)
    {
        float S(float value) => value * uiScale;
        int Si(float value) => Mathf.RoundToInt(value * uiScale);

        Sprite panelSprite = LoadOrCreatePanelSprite();
        Sprite strokeSprite = LoadOrCreateStrokeSprite();
        Sprite glowSprite = LoadOrCreateGlowSprite();
        Sprite dotSprite = LoadOrCreateCircleSprite();

        Color panelColor = new Color(0.02f, 0.06f, 0.10f, 0.92f);
        Color bodyColor = new Color(0.86f, 0.95f, 1f);

        // 9-slice 경계는 스프라이트에 픽셀로 박혀 있어 uiScale을 따라오지 않는다.
        // 이 배율을 나눠주면 모서리 곡률도 같은 비율로 커져 형태가 그대로 유지된다.
        float pixelsPerUnitMultiplier = 1f / uiScale;

        var root = new GameObject("SpeechBubble");
        root.transform.SetParent(parent, false);
        // 링(반지름 0.115m) 아래로 내려서 캐릭터와 겹치지 않게 한다.
        // 부모 스케일이 곱해지므로 비서를 키우면 이 간격도 같이 벌어진다.
        root.transform.localPosition = new Vector3(0f, -0.135f, 0f);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // 캔버스 rect를 0으로 만들면 모든 앵커가 원점 한 점으로 모인다.
        // 덕분에 자식 pivot만으로 "이 지점에서 아래로 펼치기"를 정확히 표현할 수 있다.
        var canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = Vector2.zero;

        // 부모가 확대돼 있어도 캔버스의 월드 스케일은 항상 BubbleCanvasScale로 고정한다.
        // (실행 중에는 AIAssistantSpeechBubble이 부모 스케일 변화를 따라 다시 맞춘다.)
        float parentScale = parent != null ? parent.lossyScale.x : 1f;
        if (parentScale <= 1e-6f) parentScale = 1f;
        canvasRect.localScale = Vector3.one * (BubbleCanvasScale / parentScale);

        var scaler = root.AddComponent<CanvasScaler>();
        // 실행 중 AIAssistantSpeechBubble이 카메라 거리에 맞춰 다시 정한다.
        // 여기 값은 에디터에서 미리보기용으로만 쓰인다.
        scaler.dynamicPixelsPerUnit = 1f;

        // GraphicRaycaster는 여기서 붙이지 않는다 — 이 시점의 말풍선에는 누를 것이 없고,
        // 원자 선택 클릭을 가로챌 이유도 없다. 실행 중 건너뛰기 버튼이 만들어질 때
        // AIAssistantSpeechBubble.EnsureSkipButton()이 그때 붙인다(그 버튼 말고는 모든
        // 그래픽의 raycastTarget이 꺼져 있어 클릭을 가로채지 않는다).

        var bubbleGo = new GameObject("Bubble");
        bubbleGo.transform.SetParent(root.transform, false);

        var bubbleRect = bubbleGo.AddComponent<RectTransform>();
        bubbleRect.anchorMin = bubbleRect.anchorMax = new Vector2(0.5f, 0.5f);
        // 비서가 시야 오른쪽에 있으므로 폭의 대부분이 왼쪽으로 가야 화면 밖으로 나가지 않는다.
        // pivot.x = 0.85 -> 기준점 왼쪽에 85%, 오른쪽에 15%.
        bubbleRect.pivot = new Vector2(BubblePivotX, 1f);
        bubbleRect.anchoredPosition = Vector2.zero;
        bubbleRect.sizeDelta = new Vector2(S(BubbleWidth), S(100f)); // 높이는 ContentSizeFitter가 덮어쓴다

        var canvasGroup = bubbleGo.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var layout = bubbleGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(Si(22), Si(22), Si(18), Si(20));
        layout.spacing = S(10f);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = bubbleGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // 폭은 고정, 높이만 글에 맞춘다
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // --- 배경 레이어 (레이아웃에서 제외하고 패널 전체에 늘린다) ---
        Image glow = CreateStretchedLayer(bubbleGo.transform, "Glow", glowSprite,
            new Color(1f, 1f, 1f, 0.16f), S(24f), pixelsPerUnitMultiplier);
        CreateStretchedLayer(bubbleGo.transform, "Panel", panelSprite, panelColor, 0f,
            pixelsPerUnitMultiplier);
        Image stroke = CreateStretchedLayer(bubbleGo.transform, "Stroke", strokeSprite,
            new Color(1f, 1f, 1f, 0.7f), 0f, pixelsPerUnitMultiplier);

        // --- 비서와 패널을 잇는 연결선 (말풍선 꼬리 대신) ---
        Image connector = CreateOverlay(bubbleGo.transform, "Connector", null,
            new Color(1f, 1f, 1f, 0.65f),
            new Vector2(BubblePivotX, 1f), new Vector2(0.5f, 0f),
            new Vector2(0f, 0f), new Vector2(S(2f), S(26f)));

        Image connectorDot = CreateOverlay(bubbleGo.transform, "ConnectorDot", dotSprite,
            new Color(1f, 1f, 1f, 0.95f),
            new Vector2(BubblePivotX, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, S(29f)), new Vector2(S(9f), S(9f)));

        // --- 헤더: 상태 점 + 라벨 ---
        var headerGo = new GameObject("Header");
        headerGo.transform.SetParent(bubbleGo.transform, false);
        headerGo.AddComponent<RectTransform>();

        var headerLayout = headerGo.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = S(9f);
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        var headerDotGo = new GameObject("StatusDot");
        headerDotGo.transform.SetParent(headerGo.transform, false);
        headerDotGo.AddComponent<RectTransform>();
        var headerDot = headerDotGo.AddComponent<Image>();
        headerDot.sprite = dotSprite;
        headerDot.color = Color.white;
        headerDot.raycastTarget = false;
        var dotLayout = headerDotGo.AddComponent<LayoutElement>();
        dotLayout.preferredWidth = S(11f);
        dotLayout.preferredHeight = S(11f);

        Text title = CreateText(headerGo.transform, "Title", Si(15), FontStyle.Bold,
            new Color(1f, 1f, 1f, 0.85f));
        title.text = "AI CO-SCIENTIST";
        title.alignment = TextAnchor.MiddleLeft;

        // --- 구분선 ---
        var dividerGo = new GameObject("Divider");
        dividerGo.transform.SetParent(bubbleGo.transform, false);
        dividerGo.AddComponent<RectTransform>();
        var divider = dividerGo.AddComponent<Image>();
        divider.color = new Color(1f, 1f, 1f, 0.22f);
        divider.raycastTarget = false;
        var dividerLayout = dividerGo.AddComponent<LayoutElement>();
        dividerLayout.minHeight = S(1f);
        dividerLayout.preferredHeight = S(1f);

        // --- 본문 ---
        Text label = CreateText(bubbleGo.transform, "Label", Si(25), FontStyle.Normal, bodyColor);
        label.lineSpacing = 1.25f;

        // --- 배선 ---
        var bubble = root.AddComponent<AIAssistantSpeechBubble>();
        bubble.canvasGroup = canvasGroup;
        bubble.label = label;
        bubble.bubbleRect = bubbleRect;
        bubble.visual = visual;
        bubble.canvasScaler = scaler;
        bubble.metersPerCanvasUnit = BubbleCanvasScale;
        bubble.referenceDistance = BubbleReferenceDistance;
        // 패널 본체(어두운 배경)와 본문 글자는 상태 색을 따라가지 않아야 가독성이 유지된다.
        bubble.accentGraphics = new Graphic[]
        {
            glow, stroke, connector, connectorDot, headerDot, title, divider
        };
        if (Camera.main != null) bubble.lookTarget = Camera.main.transform;

        canvasGroup.alpha = 0f;
        bubbleGo.SetActive(false);

        return bubble;
    }

    /// <summary>패널 전체에 늘어나는 배경 레이어. expand만큼 바깥으로 더 키운다(글로우용).</summary>
    private static Image CreateStretchedLayer(Transform parent, string name, Sprite sprite, Color color,
                                              float expand, float pixelsPerUnitMultiplier)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-expand, -expand);
        rect.offsetMax = new Vector2(expand, expand);

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        image.color = color;
        image.raycastTarget = false;

        go.AddComponent<LayoutElement>().ignoreLayout = true;
        return image;
    }

    /// <summary>레이아웃에서 제외된 자유 배치 그래픽 (연결선, 점 등).</summary>
    private static Image CreateOverlay(Transform parent, string name, Sprite sprite, Color color,
                                       Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = go.AddComponent<Image>();
        if (sprite != null) image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;

        go.AddComponent<LayoutElement>().ignoreLayout = true;
        return image;
    }

    private static Text CreateText(Transform parent, string name, int fontSize, FontStyle style, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        text.color = color;
        text.raycastTarget = false;
        text.text = string.Empty;
        return text;
    }

    // --- 에셋 생성 ---

    private static Material LoadOrCreateVisorMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(VisorMaterialPath);
        if (existing != null) return existing;

        Material material = CreateLitMaterial("AIAssistant_Visor");
        material.SetColor("_BaseColor", new Color(0.04f, 0.05f, 0.08f));
        material.SetFloat("_Smoothness", 0.9f);
        material.SetFloat("_Metallic", 0.4f);
        return SaveMaterial(material, VisorMaterialPath);
    }

    private static Material LoadOrCreateGlowMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
        if (existing != null) return existing;

        Material material = CreateLitMaterial("AIAssistant_Glow");
        material.SetColor("_BaseColor", new Color(0.2f, 0.8f, 1f));
        // AIAssistantVisual이 MaterialPropertyBlock으로 _EmissionColor를 덮어쓰려면
        // 머티리얼 쪽에 _EMISSION 키워드가 켜져 있어야 한다.
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        material.SetColor("_EmissionColor", new Color(0.2f, 0.8f, 1f) * 1.5f);
        return SaveMaterial(material, GlowMaterialPath);
    }

    private static Material LoadOrCreateShellMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(ShellMaterialPath);
        if (existing != null) return existing;

        Material material = CreateCustomMaterial("Custom/AIAssistantCore", "AIAssistant_Shell");
        material.SetColor("_BaseColor", new Color(0.2f, 0.8f, 1f));
        material.SetColor("_EmissionColor", new Color(0.2f, 0.8f, 1f));
        return SaveMaterial(material, ShellMaterialPath);
    }

    private static Material LoadOrCreateRingMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
        if (existing != null) return existing;

        Material material = CreateCustomMaterial("Custom/AIAssistantRing", "AIAssistant_Ring");
        material.SetColor("_BaseColor", new Color(0.2f, 0.8f, 1f));
        material.SetColor("_EmissionColor", new Color(0.2f, 0.8f, 1f));
        return SaveMaterial(material, RingMaterialPath);
    }

    private static Material LoadOrCreateMoteMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MoteMaterialPath);
        if (existing != null) return existing;

        Material material = CreateCustomMaterial("Custom/AIAssistantMote", "AIAssistant_Mote");
        material.SetColor("_BaseColor", new Color(0.45f, 0.9f, 1f, 0.8f));
        return SaveMaterial(material, MoteMaterialPath);
    }

    private static Material LoadOrCreateHighlightMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(HighlightMaterialPath);
        if (existing != null) return existing;

        Material material = CreateLitMaterial("AIAssistant_Highlight");
        material.SetColor("_BaseColor", Color.white);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        material.SetColor("_EmissionColor", Color.white * 2f);
        return SaveMaterial(material, HighlightMaterialPath);
    }

    /// <summary>전용 셰이더로 머티리얼을 만든다. 셰이더 컴파일이 실패했으면 Lit으로 대체한다.</summary>
    private static Material CreateCustomMaterial(string shaderName, string materialName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[AIAssistantSetup] 셰이더 '{shaderName}'를 찾지 못해 기본 Lit으로 대체합니다.");
            return CreateLitMaterial(materialName);
        }
        return new Material(shader) { name = materialName };
    }

    private static Material CreateLitMaterial(string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader) { name = name };
    }

    private static Material SaveMaterial(Material material, string path)
    {
        EnsureFolder(MaterialFolder);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Mesh LoadOrCreateRingMesh()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(RingMeshPath);
        if (existing != null) return existing;

        Mesh mesh = BuildTorus(RingMajorRadius, RingMinorRadius, 48, 8);
        EnsureFolder(MeshFolder);
        AssetDatabase.CreateAsset(mesh, RingMeshPath);
        return mesh;
    }

    private static Mesh LoadOrCreateCrystalMesh()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(CrystalMeshPath);
        if (existing != null) return existing;

        Mesh mesh = BuildFacetedIcosphere(CrystalRadius, 1);
        EnsureFolder(MeshFolder);
        AssetDatabase.CreateAsset(mesh, CrystalMeshPath);
        return mesh;
    }

    /// <summary>
    /// 각진 결정 모양의 구(icosphere)를 만든다. 매끈한 구와 달리 면마다 빛을 다르게 받아
    /// 회전할 때 반짝이는 느낌이 난다.
    ///
    /// 정점을 면마다 따로 만들고 면 노멀을 부여해 플랫 셰이딩이 되게 한다
    /// (정점을 공유하면 노멀이 뭉개져서 그냥 둥근 구로 보인다).
    /// </summary>
    private static Mesh BuildFacetedIcosphere(float radius, int subdivisions)
    {
        // 정이십면체의 12개 꼭짓점 — 황금비로 이루어진 직교하는 직사각형 3개의 모서리.
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var points = new List<Vector3>
        {
            new Vector3(-1f,  t, 0f), new Vector3( 1f,  t, 0f), new Vector3(-1f, -t, 0f), new Vector3( 1f, -t, 0f),
            new Vector3(0f, -1f,  t), new Vector3(0f,  1f,  t), new Vector3(0f, -1f, -t), new Vector3(0f,  1f, -t),
            new Vector3( t, 0f, -1f), new Vector3( t, 0f,  1f), new Vector3(-t, 0f, -1f), new Vector3(-t, 0f,  1f),
        };

        var faces = new List<int[]>
        {
            new[] {  0, 11,  5 }, new[] {  0,  5,  1 }, new[] {  0,  1,  7 }, new[] {  0,  7, 10 }, new[] {  0, 10, 11 },
            new[] {  1,  5,  9 }, new[] {  5, 11,  4 }, new[] { 11, 10,  2 }, new[] { 10,  7,  6 }, new[] {  7,  1,  8 },
            new[] {  3,  9,  4 }, new[] {  3,  4,  2 }, new[] {  3,  2,  6 }, new[] {  3,  6,  8 }, new[] {  3,  8,  9 },
            new[] {  4,  9,  5 }, new[] {  2,  4, 11 }, new[] {  6,  2, 10 }, new[] {  8,  6,  7 }, new[] {  9,  8,  1 },
        };

        for (int step = 0; step < subdivisions; step++)
        {
            var split = new List<int[]>(faces.Count * 4);
            var midpointCache = new Dictionary<long, int>();

            foreach (int[] face in faces)
            {
                int a = GetMidpoint(points, midpointCache, face[0], face[1]);
                int b = GetMidpoint(points, midpointCache, face[1], face[2]);
                int c = GetMidpoint(points, midpointCache, face[2], face[0]);

                split.Add(new[] { face[0], a, c });
                split.Add(new[] { face[1], b, a });
                split.Add(new[] { face[2], c, b });
                split.Add(new[] { a, b, c });
            }
            faces = split;
        }

        var vertices = new List<Vector3>(faces.Count * 3);
        var normals = new List<Vector3>(faces.Count * 3);
        var triangles = new List<int>(faces.Count * 3);

        foreach (int[] face in faces)
        {
            Vector3 v0 = points[face[0]].normalized * radius;
            Vector3 v1 = points[face[1]].normalized * radius;
            Vector3 v2 = points[face[2]].normalized * radius;

            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

            // 노멀이 중심 반대편(바깥)을 향하지 않으면 감김 순서가 뒤집힌 면이므로 바로잡는다.
            // 손으로 적은 면 목록의 방향을 일일이 믿지 않고 여기서 한 번에 보정한다.
            if (Vector3.Dot(normal, v0) < 0f)
            {
                (v1, v2) = (v2, v1);
                normal = -normal;
            }

            int baseIndex = vertices.Count;
            vertices.Add(v0); vertices.Add(v1); vertices.Add(v2);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            triangles.Add(baseIndex); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
        }

        var mesh = new Mesh { name = "AIAssistantCrystal" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int GetMidpoint(List<Vector3> points, Dictionary<long, int> cache, int i, int j)
    {
        // 두 인덱스 조합을 하나의 키로 만들어 이웃한 삼각형이 같은 중점을 재사용하게 한다.
        long key = i < j ? ((long)i << 32) + j : ((long)j << 32) + i;
        if (cache.TryGetValue(key, out int existing)) return existing;

        points.Add((points[i] + points[j]) * 0.5f);
        int index = points.Count - 1;
        cache[key] = index;
        return index;
    }

    /// <summary>
    /// XZ 평면에 눕힌 토러스를 만든다. Unity 기본 도형에 링이 없어서 직접 생성한다.
    /// 작은 조각을 원형으로 늘어놓는 방법보다 오브젝트 수와 드로우콜이 훨씬 적다.
    /// </summary>
    private static Mesh BuildTorus(float majorRadius, float minorRadius, int majorSegments, int minorSegments)
    {
        // 이음매에서 UV/노멀이 끊기지 않도록 시작점을 한 번 더 넣는다(+1).
        int columns = majorSegments + 1;
        int rows = minorSegments + 1;

        var vertices = new Vector3[columns * rows];
        var normals = new Vector3[columns * rows];
        var uvs = new Vector2[columns * rows];

        for (int i = 0; i < columns; i++)
        {
            float u = (float)i / majorSegments * Mathf.PI * 2f;
            float cosU = Mathf.Cos(u), sinU = Mathf.Sin(u);

            for (int j = 0; j < rows; j++)
            {
                float v = (float)j / minorSegments * Mathf.PI * 2f;
                float cosV = Mathf.Cos(v), sinV = Mathf.Sin(v);

                var normal = new Vector3(cosU * cosV, sinV, sinU * cosV);
                int index = i * rows + j;

                vertices[index] = new Vector3(cosU * majorRadius, 0f, sinU * majorRadius) + normal * minorRadius;
                normals[index] = normal;
                uvs[index] = new Vector2((float)i / majorSegments, (float)j / minorSegments);
            }
        }

        var triangles = new int[majorSegments * minorSegments * 6];
        int t = 0;
        for (int i = 0; i < majorSegments; i++)
        {
            for (int j = 0; j < minorSegments; j++)
            {
                int a = i * rows + j;
                int b = a + 1;          // 같은 기둥의 다음 v
                int c = a + rows;       // 다음 기둥의 같은 v
                int d = c + 1;

                // 앞면이 바깥을 향하도록 하는 감김 순서 (normal = Cross(b-a, c-a))
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }
        }

        var mesh = new Mesh { name = "AIAssistantRing" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        int slash = folder.LastIndexOf('/');
        string parent = folder.Substring(0, slash);
        string leaf = folder.Substring(slash + 1);
        EnsureFolder(parent); // 중간 폴더가 없으면 위에서부터 만든다
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // --- UI 스프라이트 생성 ---
    //
    // 기본 UI 스킨(Background.psd / UISprite.psd)은 32px 남짓한 저해상도 회색 이미지라
    // 확대하면 모서리가 뭉개지고 홀로그램 톤과도 안 맞는다. 필요한 모양을 SDF로 직접 그려
    // PNG로 굽고 9-slice 경계를 지정해 두면 어떤 크기로 늘려도 모서리가 선명하다.

    // 스프라이트는 설계 치수의 2배 해상도로 굽고 Pixels Per Unit도 2배(200)로 준다.
    // 캔버스 단위 환산은 spriteBorder / (spritePPU / referencePPU)라서 둘을 같이 2배 하면
    // 화면상 크기와 모서리 곡률은 그대로인 채 텍셀 밀도만 두 배가 된다 —
    // 비서를 확대해도 모서리가 계단으로 보이지 않는다.
    private const float SpriteSupersample = 2f;
    private const float SpritePixelsPerUnit = 100f * SpriteSupersample;

    private static Sprite LoadOrCreatePanelSprite()
    {
        const int size = 128;
        if (TryLoadSprite(PanelSpritePath, size, out Sprite existing)) return existing;

        const float radius = 28f;
        Texture2D texture = BuildTexture(size, (dx, dy) =>
        {
            float d = RoundedBoxDistance(dx, dy, size * 0.5f, size * 0.5f, radius);
            return Mathf.Clamp01(0.5f - d); // 경계에서 1픽셀만 부드럽게
        });
        return SaveSprite(PanelSpritePath, texture, new Vector4(36, 36, 36, 36));
    }

    private static Sprite LoadOrCreateStrokeSprite()
    {
        const int size = 128;
        if (TryLoadSprite(StrokeSpritePath, size, out Sprite existing)) return existing;

        const float radius = 28f;
        const float halfWidth = 2f; // 선 두께 4px = 캔버스 단위 2
        Texture2D texture = BuildTexture(size, (dx, dy) =>
        {
            float d = RoundedBoxDistance(dx, dy, size * 0.5f, size * 0.5f, radius);
            return Mathf.Clamp01(halfWidth - Mathf.Abs(d) + 0.5f); // 경계선 위에만 남긴다
        });
        return SaveSprite(StrokeSpritePath, texture, new Vector4(36, 36, 36, 36));
    }

    private static Sprite LoadOrCreateGlowSprite()
    {
        const int size = 192;
        if (TryLoadSprite(GlowSpritePath, size, out Sprite existing)) return existing;

        const float radius = 36f;
        const float falloff = 40f;
        // 모양을 텍스처 경계까지 채우면 바깥으로 번질 여백이 없어서 직선 구간이 falloff 없이
        // 잘려 나간다. 도형을 falloff만큼 안쪽으로 줄여 사방에 번질 자리를 남긴다.
        const float halfExtent = size * 0.5f - falloff;

        Texture2D texture = BuildTexture(size, (dx, dy) =>
        {
            float d = RoundedBoxDistance(dx, dy, halfExtent, halfExtent, radius);
            if (d <= 0f) return 1f;
            float t = Mathf.Clamp01(1f - d / falloff);
            return t * t; // 제곱해서 바깥으로 갈수록 빠르게 옅어지게
        });
        // 9-slice 경계는 radius + falloff보다 커야 늘렸을 때 번짐 구간이 뭉개지지 않는다.
        return SaveSprite(GlowSpritePath, texture, new Vector4(80, 80, 80, 80));
    }

    private static Sprite LoadOrCreateCircleSprite()
    {
        const int size = 64;
        if (TryLoadSprite(CircleSpritePath, size, out Sprite existing)) return existing;

        Texture2D texture = BuildTexture(size, (dx, dy) =>
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy) - (size * 0.5f - 1f);
            return Mathf.Clamp01(0.5f - d);
        });
        return SaveSprite(CircleSpritePath, texture, Vector4.zero);
    }

    /// <summary>
    /// 이미 있는 스프라이트를 재사용하되, 해상도가 지금 설계와 다르면 버리고 다시 굽는다.
    /// (이전 버전이 만들어 둔 저해상도 파일이 그대로 남아 확대 시 계단이 보이는 걸 막는다.)
    /// </summary>
    private static bool TryLoadSprite(string assetPath, int expectedSize, out Sprite sprite)
    {
        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null) return false;
        if (sprite.texture != null && sprite.texture.width == expectedSize) return true;

        sprite = null;
        return false;
    }

    /// <summary>중심 기준 좌표(dx, dy)를 받아 알파를 돌려주는 함수로 텍스처를 채운다.</summary>
    private static Texture2D BuildTexture(int size, System.Func<float, float, float> alphaAt)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float center = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center;
                float dy = y + 0.5f - center;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alphaAt(dx, dy));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>둥근 사각형까지의 부호 있는 거리. 음수면 안쪽, 양수면 바깥쪽.</summary>
    private static float RoundedBoxDistance(float px, float py, float halfWidth, float halfHeight, float radius)
    {
        float qx = Mathf.Abs(px) - halfWidth + radius;
        float qy = Mathf.Abs(py) - halfHeight + radius;

        float outsideX = Mathf.Max(qx, 0f);
        float outsideY = Mathf.Max(qy, 0f);
        float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);

        return outside + inside - radius;
    }

    private static Sprite SaveSprite(string assetPath, Texture2D texture, Vector4 border)
    {
        EnsureFolder(TextureFolder);

        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture); // 에셋으로 저장되지 않는 임시 텍스처는 바로 해제

        string absolutePath = System.IO.Path.Combine(
            Application.dataPath, assetPath.Substring("Assets/".Length));
        System.IO.File.WriteAllBytes(absolutePath, png);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;      // 9-slice 경계 (모서리를 늘리지 않는 영역)
            // PPU를 초과 샘플링 배율만큼 올려야 캔버스 단위 환산 결과가 등배일 때와 같아진다.
            importer.spritePixelsPerUnit = SpritePixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }
}
#endif
