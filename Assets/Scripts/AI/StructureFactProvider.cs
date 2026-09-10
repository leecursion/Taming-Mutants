using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 화면에서 실제로 확인할 수 있는 수치만 모아 LLM 컨텍스트에 붙일 사실 블록을 만든다.
///
/// 존재 이유는 환각 방지 하나다. 모델은 지금 화면에 무엇이 떠 있는지 모르는 채 답하므로,
/// "이 부분 pLDDT 얼마예요?" 같은 질문에 그럴듯한 숫자를 지어낸다. 중학생 대상 교육
/// 콘텐츠에서 지어낸 수치는 틀린 것보다 나쁘다 — 학습자가 그것을 사실로 배운다.
/// 여기서 만든 목록을 서버가 별도 system 메시지로 넣고, 프롬프트가 "이 목록 밖의 숫자는
/// 말하지 말 것"이라는 규칙을 건다.
///
/// 설계 원칙(guide.md 1장)을 따라 이 컴포넌트는 <b>있으면 좋은 추가 정보</b>일 뿐이다.
/// 없거나, 구조가 아직 로드되지 않았거나, 생성이 실패해도 비서는 지금과 똑같이 답해야 한다.
/// 그래서 모든 조회가 null을 견디고, 확인할 수 있는 게 없으면 빈 문자열을 돌려준다.
///
/// 씬 파일에 저장하지 않고 <see cref="Ensure"/>로 런타임에 확보한다 —
/// <see cref="MutationHighlighter.EnsureFor"/>와 같은 방식이다.
/// </summary>
public class StructureFactProvider : MonoBehaviour
{
    [Header("참조 (비워두면 자동 탐색)")]
    public ProteinLoader proteinLoader;
    public StructureLevelController levelController;
    public DockingQuestController dockingQuest;
    [Tooltip("사건 5(p53 열안정성)에서만 존재한다. 없으면 온도 줄을 생략한다.")]
    public ThermalStabilityController thermal;

    [Header("디버그")]
    [Tooltip("켜면 매 요청마다 생성된 사실 블록을 콘솔에 남긴다.")]
    public bool logFacts;

    /// <summary>블록 전체 상한. 매 질문마다 나가는 내용이라 토큰 비용에 직접 영향을 준다.</summary>
    private const int MaxLines = 15;
    private const int MaxCharacters = 1200;

    /// <summary>포켓 잔기 목록에 그대로 적을 최대 개수. 넘으면 "… 외 N개"로 줄인다.</summary>
    private const int MaxPocketResiduesListed = 10;

    private AtomInfo _lastSelected;

    /// <summary>
    /// 씬에 없으면 만들어서 돌려준다. <see cref="MutationHighlighter.EnsureFor"/>와 같은 방식.
    ///
    /// 인트로 동안에는 단백질 무대가 통째로 꺼져 있어 비활성 오브젝트까지 훑어야 한다.
    /// 붙일 곳은 ProteinLoader 쪽이 자연스럽다 — 사실의 출처가 대부분 그것이고,
    /// 퀘스트를 전환해도 그 오브젝트는 살아남아 선택 기록이 유지된다.
    /// </summary>
    public static StructureFactProvider Ensure()
    {
        var existing = FindFirstObjectByType<StructureFactProvider>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        var loader = FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        if (loader != null) return loader.gameObject.AddComponent<StructureFactProvider>();

        // 구조가 아직 씬에 없는 단계(인트로 등)에서도 기록은 받아둘 수 있어야 한다.
        return new GameObject("StructureFactProvider").AddComponent<StructureFactProvider>();
    }

    /// <summary>현재 선택된 원자. 없거나 이미 파괴됐으면 null.</summary>
    public AtomInfo LastSelectedAtom => _lastSelected != null ? _lastSelected : null;

    /// <summary>마우스/시선 셀렉터가 원자를 고를 때마다 기록한다.</summary>
    public void RecordSelection(AtomInfo atom)
    {
        _lastSelected = atom;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// 비어 있는 참조만 채운다. Awake와 <see cref="BuildFactBlock"/> 양쪽에서 부른다 —
    /// <see cref="Ensure"/>가 꺼져 있는 오브젝트에 컴포넌트를 붙이면 Awake가 한참 뒤에야
    /// 불리고, 그 사이에 들어온 질문은 참조가 비어 사실을 하나도 못 만든다.
    /// 이미 채워진 참조는 건드리지 않으므로 여러 번 불러도 안전하다.
    /// </summary>
    private void ResolveReferences()
    {
        if (proteinLoader == null)
            proteinLoader = FindFirstObjectByType<ProteinLoader>(FindObjectsInactive.Include);
        if (levelController == null)
            levelController = FindFirstObjectByType<StructureLevelController>(FindObjectsInactive.Include);
        if (dockingQuest == null)
            dockingQuest = FindFirstObjectByType<DockingQuestController>(FindObjectsInactive.Include);
        // 열안정성은 사건 5에만 있다 — 못 찾아도 정상이므로 조용히 null로 둔다.
        if (thermal == null)
            thermal = FindFirstObjectByType<ThermalStabilityController>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// LLM 컨텍스트에 붙일 사실 블록. 확인할 수 있는 게 없으면 빈 문자열.
    ///
    /// 머리말만 있는 블록을 보내면 모델이 "사실이 하나도 없다"로 읽고, 빈 목록을 근거 삼아
    /// 오히려 엉뚱한 소리를 한다. 내용이 없으면 아예 보내지 않는다.
    /// </summary>
    public string BuildFactBlock()
    {
        ResolveReferences();

        if (proteinLoader == null || !proteinLoader.HasStructure) return string.Empty;

        var lines = new List<string>(MaxLines);

        AppendViewLevel(lines);
        AppendStructure(lines);
        AppendSelection(lines);
        AppendTemperature(lines);

        if (lines.Count == 0) return string.Empty;

        var builder = new StringBuilder(MaxCharacters);
        builder.Append("[확인된 사실 — 아래 수치만 인용할 것]");

        // 머리말도 한 줄이므로 사실은 MaxLines - 1개까지만 싣는다.
        int limit = Mathf.Min(lines.Count, MaxLines - 1);
        for (int i = 0; i < limit; i++) builder.Append('\n').Append("- ").Append(lines[i]);

        string block = builder.ToString();
        if (block.Length > MaxCharacters) block = block.Substring(0, MaxCharacters);

        if (logFacts) Debug.Log($"[StructureFactProvider] 사실 블록\n{block}", this);

        return block;
    }

    private void AppendViewLevel(List<string> lines)
    {
        if (levelController == null) return;

        lines.Add("현재 표시 단계: " + DescribeLevel(levelController.CurrentLevel));
    }

    /// <summary>표시 단계 enum을 학습자가 화면에서 보는 말로 옮긴다.</summary>
    private static string DescribeLevel(StructureLevelController.ViewLevel level)
    {
        switch (level)
        {
            case StructureLevelController.ViewLevel.Ribbon: return "리본 (전체 구조)";
            case StructureLevelController.ViewLevel.Helix: return "나선 구간";
            case StructureLevelController.ViewLevel.AminoAcid: return "아미노산 (원자 단위)";
            default: return level.ToString();
        }
    }

    private void AppendStructure(List<string> lines)
    {
        string file = StructureFileName();
        lines.Add(string.IsNullOrEmpty(file)
            ? $"구조: 원자 {proteinLoader.AtomCount}개"
            : $"구조: {file}, 원자 {proteinLoader.AtomCount}개");
    }

    /// <summary>경로에서 파일명만 뽑는다. 원격 URL로 로드한 경우도 같은 규칙으로 자른다.</summary>
    private string StructureFileName()
    {
        string path = !string.IsNullOrEmpty(proteinLoader.streamingAssetsRelativePath)
            ? proteinLoader.streamingAssetsRelativePath
            : proteinLoader.remoteJsonUrl;

        if (string.IsNullOrEmpty(path)) return null;

        int slash = path.LastIndexOfAny(new[] { '/', '\\' });
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    private void AppendSelection(List<string> lines)
    {
        AtomInfo selected = LastSelectedAtom;
        if (selected == null) return;

        IReadOnlyList<AtomInfo> residueAtoms = proteinLoader.AtomsOfResidue(selected.ResidueId);

        // 잔기 평균 pLDDT — 전체 원자를 훑지 않고 잔기 인덱스만 쓴다.
        if (residueAtoms.Count > 0)
        {
            float sum = 0f;
            foreach (var atom in residueAtoms) sum += atom.PLDDT;
            lines.Add($"선택 잔기: {selected.ResidueName}{selected.ResidueId} " +
                      $"(원자 {residueAtoms.Count}개, pLDDT 평균 {sum / residueAtoms.Count:0.0})");
        }
        else
        {
            lines.Add($"선택 잔기: {selected.ResidueName}{selected.ResidueId}");
        }

        lines.Add($"선택 원자: {selected.AtomName} ({selected.Element}), pLDDT {selected.PLDDT:0.0}");

        AppendPocketRelation(lines, selected);
    }

    private void AppendPocketRelation(List<string> lines, AtomInfo selected)
    {
        if (dockingQuest == null) return;

        bool isTarget = selected.ResidueId == dockingQuest.targetResidueId;
        bool inPocket = dockingQuest.pocketResidueIds != null &&
                        dockingQuest.pocketResidueIds.Contains(selected.ResidueId);

        // 포켓 밖이라는 것도 사실이다. 이 줄이 없으면 모델은 "포켓 안"이라는 말이 안 보일 때
        // 그것이 확인된 부정인지 정보가 없는 것인지 구분하지 못한다.
        if (isTarget) lines.Add("이 잔기가 표적 잔기 본인");
        else if (inPocket) lines.Add("이 잔기는 표적 포켓에 속함");
        else lines.Add("이 잔기는 표적 포켓 밖");

        AppendDistanceToTarget(lines, selected);
        AppendPocketList(lines);
    }

    private void AppendDistanceToTarget(List<string> lines, AtomInfo selected)
    {
        // 도킹 연출이 겨냥하는 것과 정확히 같은 원자를 집는다. 규칙을 여기에 다시 적으면
        // 화면이 향하는 지점과 비서가 말하는 거리의 기준이 조용히 어긋난다.
        AtomInfo target = DockingQuestController.PickTargetAtom(
            proteinLoader.AtomsOfResidue(dockingQuest.targetResidueId), dockingQuest.targetAtomName);

        if (target == null) return;

        // 원자 좌표는 생성 시 (x,y,z) * 0.1f - CenterOffset 으로 옹스트롬을 0.1배 축소해
        // localPosition에 저장돼 있다. 따라서 두 원자의 localPosition 차이 × 10 이 옹스트롬이고,
        // CenterOffset은 차이를 빼는 과정에서 상쇄된다.
        //
        // transform.position(월드 좌표)를 쓰면 앵커(ProteinAnchor)의 스케일이 곱해져 값이 틀어진다.
        // 앵커는 줌/회전 연출로 스케일이 바뀌므로, 같은 두 원자인데도 카메라 조작에 따라
        // 거리가 달라지는 수치가 나온다. 반드시 localPosition을 쓴다.
        float angstrom = Vector3.Distance(
            selected.transform.localPosition, target.transform.localPosition) * 10f;

        lines.Add($"표적 잔기 {dockingQuest.targetResidueId}까지 거리: {angstrom:0.0} Å");
    }

    private void AppendPocketList(List<string> lines)
    {
        List<int> pocket = dockingQuest.pocketResidueIds;
        if (pocket == null || pocket.Count == 0) return;

        var builder = new StringBuilder(64);
        builder.Append("표적 포켓 잔기 목록: ");

        int listed = Mathf.Min(pocket.Count, MaxPocketResiduesListed);
        for (int i = 0; i < listed; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.Append(pocket[i]);
        }

        if (pocket.Count > listed) builder.Append($" … 외 {pocket.Count - listed}개");

        lines.Add(builder.ToString());
    }

    private void AppendTemperature(List<string> lines)
    {
        // 사건 5가 아니면 온도 개념 자체가 화면에 없다. 0 °C 같은 기본값을 사실로 보내면
        // 모델이 있지도 않은 온도계를 설명한다.
        if (thermal == null || !thermal.isActiveAndEnabled) return;

        lines.Add($"현재 온도: {thermal.CurrentCelsius:0} °C (체온 {thermal.physiologicalCelsius:0} °C)");
    }
}
