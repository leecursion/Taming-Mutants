using System;
using System.Collections.Generic;

/// <summary>도킹 시도 결과 유형 (F-04 후보물질 평가 퀘스트).</summary>
public enum DockingOutcome
{
    Success,     // 정답: Cys12 공유결합 형성 → KRAS OFF 락인
    NoWarhead,   // 오답 1: 포켓엔 들어가나 고정되지 않고 튕겨 나옴
    StericClash, // 오답 2: 포켓 입구에서 공간 충돌로 진입 실패
    OffTarget    // 오답 3: 결합 부위 상충 → 자석 반발처럼 밀려남
}

/// <summary>
/// StreamingAssets/compounds/*.json 스키마.
/// 좌표는 단백질 JSON과 동일하게 Angstrom 단위이며,
/// 렌더링 시 ProteinLoader와 같은 0.1배 축소를 적용한다.
/// 결합(bonds)은 거리 추정이 아니라 명시적 인덱스 쌍으로 기록되어 있다.
/// </summary>
[Serializable]
public class CompoundData
{
    public string id;
    public string display_name;
    public string subtitle;
    public string outcome;        // DockingOutcome 이름 문자열
    public float affinity;        // kcal/mol (음수일수록 강한 결합)
    public string result_message; // 도킹 시도 후 표시할 안내/경고 문구
    public List<CompoundAtom> atoms;
    public List<CompoundBond> bonds;

    public DockingOutcome Outcome =>
        Enum.TryParse(outcome, out DockingOutcome parsed) ? parsed : DockingOutcome.OffTarget;
}

[Serializable]
public class CompoundAtom
{
    public string element;
    public float x, y, z;
    public bool is_warhead; // 아크릴아마이드 반응기 — 발광 강조 및 공유결합 지점
}

[Serializable]
public class CompoundBond
{
    public int a;
    public int b;
}
