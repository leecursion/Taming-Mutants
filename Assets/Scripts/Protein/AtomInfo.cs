using UnityEngine;

/// <summary>
/// 각 원자 오브젝트에 부착되어 원자/잔기 메타데이터를 보관한다.
/// 시선/포인터로 원자를 조준했을 때(F-04.1) 정보 패널에 표시하는 용도로 사용.
/// </summary>
public class AtomInfo : MonoBehaviour
{
    public string AtomName { get; private set; }
    public string Element { get; private set; }
    public string ResidueName { get; private set; }
    public int ResidueId { get; private set; }
    public float PLDDT { get; private set; }

    public void Set(string atomName, string element, string resName, int resId, float plddt)
    {
        AtomName = atomName;
        Element = element;
        ResidueName = resName;
        ResidueId = resId;
        PLDDT = plddt;
    }

    public string GetDisplayLabel()
    {
        return $"{ResidueName}{ResidueId} - {AtomName} ({Element})\npLDDT: {PLDDT:0.0}";
    }
}
