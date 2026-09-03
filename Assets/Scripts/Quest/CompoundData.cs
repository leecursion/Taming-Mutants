using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>도킹 시도 결과 유형 (F-04 후보물질 평가 퀘스트).</summary>
public enum DockingOutcome
{
    Success,        // 정답: Cys12 공유결합 형성 → KRAS OFF 락인 / 또는 포켓 안정화 성공
    NoWarhead,      // 오답 1: 포켓엔 들어가나 고정되지 않고 튕겨 나옴
    StericClash,    // 오답 2: 포켓 입구에서 공간 충돌로 진입 실패
    OffTarget,      // 오답 3: 결합 부위 상충 → 자석 반발처럼 밀려남

    // --- p53 Y220C 열안정성 퀘스트 전용 (같은 Snap 판정 + 다른 짧은 VFX/HUD만 다르다) ---
    FragmentHit,     // 부분 정답: 포켓엔 들어가 안정화 효과가 잠깐 나타나지만, 곧 이탈한다
    WrongStrategy,   // 오답: 이 포켓과 무관한 전략(예: MDM2 억제제) — 애초에 포켓에 결합하지 않는다
    NoStabilization, // 오답: 표적 원자 근처엔 도달하나(proximity) 안정화 상호작용은 형성하지 못한다
    NonSelective     // 오답: 이 포켓 말고 주변에도 비특이적으로 들러붙는다
}

/// <summary>
/// 도킹 시도 한 번의 결과. <see cref="DockingQuestController.OnDockingFinished"/>가 실어 보낸다.
///
/// <see cref="DockingOutcome"/>만 넘기지 않는 이유는 "순서 오류"다 — 아직 때가 아니어서 물러난 경우도
/// 이벤트에는 NoWarhead로 실려 나가는데, 그걸 그대로 읽으면 비서가 "고정할 갈고리가 없었어"라는
/// 엉뚱한 설명을 한다. 방향은 맞았고 순서만 이른 상황이라 안내가 정반대가 된다.
/// </summary>
public struct DockingResult
{
    /// <summary>판정 결과. <see cref="IsOrderError"/>가 true면 이 값은 연출용 대체값이다.</summary>
    public DockingOutcome Outcome;

    /// <summary>시도한 후보물질.</summary>
    public CompoundData Compound;

    /// <summary>먼저 성공해야 할 다른 물질이 남아 "아직 때가 아니다"로 물러난 경우.</summary>
    public bool IsOrderError;

    /// <summary>화면에 실제로 표시된 문구. 비서가 말할 바닥선이 된다.</summary>
    public string Message;

    /// <summary>성공 판정인지. 순서 오류는 성공이 아니다.</summary>
    public bool IsSuccess => !IsOrderError && Outcome == DockingOutcome.Success;
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
    [Tooltip("선택 패널 이름표 1행 + 비서가 부르는 이름. 이름표는 TextMesh라 자동 줄바꿈이 없어, " +
             "칸 간격(boxSize+spacing) 안에 들어가려면 한글 11자/영문 22자를 넘기면 안 된다 — " +
             "넘기면 옆 칸 이름표와 겹친다. 학술명은 scientific_name에 둔다.")]
    public string display_name;
    [Tooltip("이름표 2행(<size=28>). 같은 이유로 한글 19자/영문 38자가 한계다.")]
    public string subtitle;
    [Tooltip("원래의 학술명/모델 근거. 이름표에는 띄우지 않고 비서 컨텍스트로만 넘긴다 — " +
             "display_name을 중학생용 짧은 한국어로 바꾸면서 밀려난 정보를 여기에 보관한다.")]
    public string scientific_name;
    public string outcome;        // DockingOutcome 이름 문자열
    public float affinity;        // kcal/mol (음수일수록 강한 결합)
    public string result_message; // 도킹 시도 후 표시할 안내/경고 문구
    public List<CompoundAtom> atoms;
    public List<CompoundBond> bonds;

    [Tooltip("Success인데도 이 값이 false면 이 화합물만으로는 단계를 완료하지 않는다. " +
             "예: CFTR corrector는 필요하지만 그 자체로 충분하지 않고 potentiator까지 필요한 경우.")]
    public bool completes_stage = true;
    [Tooltip("비어있지 않으면, 이 id를 가진 화합물이 먼저 Success하기 전까지는 이 화합물의 진짜 " +
             "outcome 대신 '순서 오류' 연출(order_error_message)이 나온다. " +
             "예: potentiator(Ivacaftor)를 corrector보다 먼저 골랐을 때.")]
    public string requires_prior_success_id;
    [Tooltip("requires_prior_success_id 조건이 아직 충족되지 않았을 때 표시할 안내 문구.")]
    public string order_error_message;

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
