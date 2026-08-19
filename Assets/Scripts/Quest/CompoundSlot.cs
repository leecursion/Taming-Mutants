using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F-04.2 후보물질 선택 박스 1칸.
/// 와이어프레임 큐브 안에 화합물 3D 분자와 이름 라벨을 담고,
/// 레이캐스트 선택을 위한 BoxCollider를 가진다.
/// CompoundSelectionPanel이 생성/배치하며 직접 씬에 붙일 필요는 없다.
/// </summary>
public class CompoundSlot : MonoBehaviour
{
    public CompoundData Data { get; private set; }
    public GameObject MoleculeRoot { get; private set; }
    /// <summary>박스 안 표시용 축소 전(월드 도킹용) 스케일 → 항상 1. 도킹 클론은 이 값 기준으로 복제.</summary>
    public float DisplayFitScale { get; private set; } = 1f;

    private readonly List<Renderer> _frameRenderers = new List<Renderer>();
    private Color _idleFrameColor;
    private float _spinSpeed;

    private static readonly Color HoverColor = new Color(0.4f, 1f, 1f);

    public void Init(CompoundData data, GameObject moleculeRoot, float boxSize,
                     Color frameColor, float frameThickness, float spinSpeed)
    {
        Data = data;
        MoleculeRoot = moleculeRoot;
        _idleFrameColor = frameColor;
        _spinSpeed = spinSpeed;

        // 분자를 박스 크기에 맞게 축소
        float radius = CompoundMoleculeBuilder.LocalRadius(moleculeRoot);
        DisplayFitScale = radius > 0.0001f ? (boxSize * 0.42f) / radius : 1f;
        moleculeRoot.transform.localScale = Vector3.one * DisplayFitScale;
        moleculeRoot.transform.localPosition = Vector3.zero;

        BuildWireFrame(boxSize, frameThickness);

        var col = gameObject.AddComponent<BoxCollider>();
        col.size = Vector3.one * boxSize;
    }

    private void Update()
    {
        // 박스 안에서 분자가 천천히 자전해 3D 형태를 파악하기 쉽게 한다.
        if (MoleculeRoot != null)
            MoleculeRoot.transform.Rotate(Vector3.up, _spinSpeed * Time.deltaTime, Space.Self);
    }

    public void SetHovered(bool hovered)
    {
        SetFrameColor(hovered ? HoverColor : _idleFrameColor);
    }

    /// <summary>도킹 결과에 따라 박스 프레임 색을 고정한다 (정답: 녹색 / 오답: 결과색).</summary>
    public void SetResultColor(Color color)
    {
        _idleFrameColor = color;
        SetFrameColor(color);
    }

    private void SetFrameColor(Color color)
    {
        WireBox.SetColor(_frameRenderers, color);
    }

    private void BuildWireFrame(float size, float thickness)
    {
        _frameRenderers.AddRange(WireBox.Build(transform, Vector3.one * size, thickness));
        SetFrameColor(_idleFrameColor);
    }
}
