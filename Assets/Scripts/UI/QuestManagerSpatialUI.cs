using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F-01.3 퀘스트 매니저 인터페이스
/// 5단계 신약개발 퀘스트(F-02~F-05 각 Quest 1~5)의 진행 상태를
/// 테이블 옆 World Space Canvas 홀로그램 패널에 표시한다.
/// 배경 패널 머티리얼은 Hologram.shader를 사용하는 것을 권장.
/// </summary>
public class QuestManagerSpatialUI : MonoBehaviour
{
    public enum QuestStage
    {
        Quest1_DiseaseAnalysis = 0,   // F-02 질병 원인 분석 (DNA 탐색)
        Quest2_ProteinStructure = 1, // F-03 단백질 구조 분석 (AlphaFold 연동)
        Quest3_TargetDiscovery = 2,  // F-04 치료 표적 발굴
        Quest4_CandidateEvaluation = 3, // F-04 후보물질 탐색·평가
        Quest5_Verification = 4      // F-05 치료 효과 검증 및 결과 분석
    }

    [Serializable]
    public class StagePanel
    {
        public QuestStage stage;
        public Image progressFill;      // 단계별 진행률 바 (Fill Amount 사용)
        public GameObject checkmarkIcon; // 완료 시 표시할 체크 아이콘
        public Text stageLabel;
    }

    [Header("단계별 UI 참조")]
    public StagePanel[] stagePanels;

    [Header("현재 상태")]
    public QuestStage currentStage = QuestStage.Quest1_DiseaseAnalysis;
    [Range(0f, 1f)] public float currentStageProgress = 0f;

    public event Action<QuestStage> OnStageCompleted;

    private void Update()
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        foreach (var panel in stagePanels)
        {
            bool isPast = (int)panel.stage < (int)currentStage;
            bool isCurrent = panel.stage == currentStage;

            float fill = isPast ? 1f : (isCurrent ? currentStageProgress : 0f);
            if (panel.progressFill != null) panel.progressFill.fillAmount = fill;
            if (panel.checkmarkIcon != null) panel.checkmarkIcon.SetActive(isPast);
        }
    }

    /// <summary>현재 단계 진행률을 갱신 (예: DNA 변이 부위 3개 중 1개 확인 -> 0.33)</summary>
    public void SetProgress(float progress01)
    {
        currentStageProgress = Mathf.Clamp01(progress01);
    }

    /// <summary>현재 단계를 완료 처리하고 다음 단계로 넘어간다.</summary>
    public void CompleteCurrentStageAndAdvance()
    {
        OnStageCompleted?.Invoke(currentStage);

        int next = (int)currentStage + 1;
        if (next <= (int)QuestStage.Quest5_Verification)
        {
            currentStage = (QuestStage)next;
            currentStageProgress = 0f;
        }
        else
        {
            currentStageProgress = 1f; // 전체 퀘스트 완료
        }
    }
}
