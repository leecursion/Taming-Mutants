using UnityEngine;

/// <summary>
/// 잔기 번호를 구조 옆에 띄워 두고, 그 잔기까지 얇은 지시선을 긋는 빌보드 숫자표.
///
/// 비서는 "반짝이는 858번 자리를 보세요"처럼 번호로 자리를 가리키는데, 화면 어디에도 그 숫자가
/// 없었다. 붉게 맥동하는 공이 여럿이거나(변이 부위가 둘 이상), 포켓 잔기 여섯 개가 같은 색으로
/// 칠해져 있으면 말한 번호와 화면의 자리를 이을 방법이 아예 없다. 이 태그가 그 연결 고리다.
///
/// 원자(<see cref="AtomInfo"/>)의 자식이 아니라 단백질 루트의 자식으로 둔다 — 원자는 리본/나선
/// 단계에서 <see cref="ProteinLoader.SetAtomsVisible"/>(false)로 통째로 꺼지는데, 정작 비서가
/// 변이 자리를 짚어 보이는 도입 시나리오가 그 리본 단계에서 재생된다. 원자에 붙여 두면
/// 도입부 내내 숫자가 보이지 않는다.
///
/// 글자 크기는 카메라 거리에 비례해 보정한다. 리본 단계(구조 전체가 멀리 보임)와 아미노산
/// 단계(원자 하나가 화면을 채움)의 거리 차이가 수십 배라, 월드 크기를 고정하면 한쪽에서는
/// 점이 되고 다른 쪽에서는 화면을 가린다 — CompoundSelectionPanel.keepConstantApparentSize와
/// 같은 이유, 같은 방식이다.
/// </summary>
[DisallowMultipleComponent]
public class ResidueNumberTag : MonoBehaviour
{
    private Camera _camera;
    private TextMesh _text;
    private LineRenderer _leader;
    private Color _baseColor;
    private float _apparentSize;
    private float _popStart = float.NegativeInfinity;
    private float _popDuration;
    private bool _highlighted;

    /// <summary>이 태그가 가리키는 잔기 번호.</summary>
    public int ResidueId { get; private set; }

    /// <summary>
    /// 태그 하나를 만든다.
    /// </summary>
    /// <param name="proteinRoot">단백질 루트(원자들의 부모). 아미노산 단계의 중앙 정렬로 루트가
    /// 통째로 움직여도 태그가 같이 따라가도록 이 아래에 붙인다.</param>
    /// <param name="residueLocalPos">잔기 위치(단백질 루트 로컬 좌표).</param>
    /// <param name="caption">번호 아래 작게 붙는 별명. 한글이 들어가므로 한글 글리프가 있는
    /// 폰트를 함께 넘길 때만 지정한다 — 내장 LegacyRuntime은 한글이 네모로 나온다.</param>
    /// <param name="leaderLength">잔기에서 숫자까지 지시선 길이(로컬 단위). 구조 바깥쪽으로 뻗는다.</param>
    public static ResidueNumberTag Create(Transform proteinRoot, Vector3 residueLocalPos, int residueId,
                                          string caption, Color color, Font font, Camera camera,
                                          float leaderLength, float apparentSize)
    {
        var go = new GameObject($"ResidueTag_{residueId}");
        go.transform.SetParent(proteinRoot, false);

        var tag = go.AddComponent<ResidueNumberTag>();
        tag.Build(residueLocalPos, residueId, caption, color, font, camera, leaderLength, apparentSize);
        return tag;
    }

    private void Build(Vector3 residueLocalPos, int residueId, string caption, Color color,
                       Font font, Camera camera, float leaderLength, float apparentSize)
    {
        ResidueId = residueId;
        _camera = camera != null ? camera : Camera.main;
        _baseColor = color;
        _apparentSize = Mathf.Max(apparentSize, 1e-4f);

        // 구조는 ProteinLoader.CenterOffset을 빼서 원점 근처에 정렬돼 있으므로, 원점에서 잔기로
        // 향하는 방향이 곧 "구조 바깥쪽"이다. 그쪽으로 밀어야 숫자가 다른 원자에 파묻히지 않는다.
        Vector3 outward = residueLocalPos.sqrMagnitude > 1e-6f ? residueLocalPos.normalized : Vector3.up;
        Vector3 labelLocal = residueLocalPos + outward * leaderLength;

        transform.localPosition = labelLocal;
        transform.localRotation = Quaternion.identity; // 지시선 기준계 — 빌보드 회전은 글자 쪽에만 건다

        BuildLeader(residueLocalPos - labelLocal);
        BuildText(caption, font);
    }

    /// <summary>숫자에서 잔기까지 잇는 지시선. 숫자만 떠 있으면 "저 숫자가 어느 공을
    /// 가리키는가"가 다시 모호해진다 — 특히 원자가 빽빽한 아미노산 단계에서.</summary>
    private void BuildLeader(Vector3 toResidueLocal)
    {
        var lineGo = new GameObject("Leader");
        lineGo.transform.SetParent(transform, false);

        _leader = lineGo.AddComponent<LineRenderer>();
        _leader.useWorldSpace = false; // 부모가 회전하지 않으므로 로컬 좌표가 그대로 유지된다
        _leader.positionCount = 2;
        _leader.SetPosition(0, Vector3.zero);
        _leader.SetPosition(1, toResidueLocal);
        _leader.sharedMaterial = RuntimeMaterials.LineUnlit;
        _leader.textureMode = LineTextureMode.Stretch;
        _leader.numCapVertices = 2;
        // LateUpdate가 카메라 거리로 다시 잡기 전 한 프레임 — 기본값 1.0이면 그 한 프레임 동안
        // 화면을 가로지르는 통나무가 보인다.
        _leader.startWidth = 0.01f;
        _leader.endWidth = 0.004f;
        _leader.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _leader.receiveShadows = false;
        ApplyLeaderColor(_baseColor);
    }

    private void BuildText(string caption, Font font)
    {
        var textGo = new GameObject("Number");
        textGo.transform.SetParent(transform, false);

        _text = textGo.AddComponent<TextMesh>();
        _text.text = string.IsNullOrWhiteSpace(caption)
            ? ResidueId.ToString()
            : $"{ResidueId}\n<size=26>{caption}</size>";
        _text.font = font;
        _text.fontSize = 48;
        // TextMesh의 월드 높이 ≈ characterSize * fontSize / 10. 이 값을 넣으면 localScale 1일 때
        // 글자 높이가 정확히 1 월드 단위가 되어, LateUpdate가 localScale에 원하는 높이를
        // 그대로 넣을 수 있다 — 크기 조절 인자를 한 곳으로 모은다.
        _text.characterSize = 10f / 48f;
        _text.anchor = TextAnchor.MiddleCenter;
        _text.alignment = TextAlignment.Center;
        _text.richText = true;
        _text.color = _baseColor;

        var mr = textGo.GetComponent<MeshRenderer>();
        if (font != null) mr.sharedMaterial = font.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>
    /// 비서가 이 번호를 말하는 순간 한 번 튀어오르게 한다. 화면에 번호가 늘 떠 있어도, 여러 개
    /// 중 "지금 말하는 그것"이 어느 것인지는 따로 알려줘야 한다.
    /// </summary>
    public void Pop(float duration = 1.4f)
    {
        _popStart = Time.time;
        _popDuration = Mathf.Max(duration, 0.01f);
    }

    /// <summary>
    /// 지금 이야기 중인 자리로 계속 켜 둔다. <see cref="Pop"/>이 한 번의 반응이라면 이쪽은
    /// 그 대사가 끝날 때까지 유지되는 상태다.
    /// </summary>
    public void SetHighlighted(bool highlighted)
    {
        _highlighted = highlighted;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    private void LateUpdate()
    {
        if (_text == null) return;
        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        Transform cam = _camera.transform;
        Transform label = _text.transform;

        // 튀어오름: 처음에 크게 벌어졌다가 원래 크기로 가라앉는다.
        float pop = 0f;
        if (_popDuration > 0f)
        {
            float t = (Time.time - _popStart) / _popDuration;
            if (t >= 0f && t <= 1f) pop = 1f - t;
        }
        float emphasis = Mathf.Max(pop, _highlighted ? 0.45f : 0f);

        // 카메라 거리에 비례해 화면상 크기를 일정하게 유지한다. 부모(단백질 루트)의 스케일은
        // 이미 world 크기에 곱해져 있으므로 나눠서 상쇄한다 — 안 그러면 구조 스케일이 두 번 먹는다.
        float distance = Vector3.Distance(cam.position, label.position);
        float parentScale = Mathf.Max(transform.lossyScale.x, 1e-4f);
        float size = distance * _apparentSize * (1f + 0.8f * emphasis) / parentScale;
        label.localScale = Vector3.one * size;

        // TextMesh는 +Z가 뒤통수다 — forward가 카메라 반대편을 향해야 글자가 바로 읽힌다.
        Vector3 away = label.position - cam.position;
        if (away.sqrMagnitude > 1e-6f)
            label.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);

        Color tone = Color.Lerp(_baseColor, Color.white, emphasis);
        _text.color = tone;
        ApplyLeaderColor(tone);

        if (_leader != null)
        {
            // 지시선 굵기도 거리에 맞춰 잡는다. 고정 폭으로 두면 리본 단계에서는 실오라기가 되고
            // 아미노산 단계에서는 통나무가 된다.
            float width = distance * _apparentSize * 0.12f / parentScale;
            _leader.startWidth = width;
            _leader.endWidth = width * 0.35f; // 잔기 쪽으로 갈수록 가늘어져 방향이 읽힌다
        }
    }

    private void ApplyLeaderColor(Color color)
    {
        if (_leader == null) return;

        Color line = color;
        line.a = 0.75f;
        _leader.startColor = line;
        line.a = 0.25f;
        _leader.endColor = line;
    }
}
