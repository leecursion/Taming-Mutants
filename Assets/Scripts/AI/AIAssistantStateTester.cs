using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// AI 연동 전에 표정/색/회전 연출을 눈으로 확인하기 위한 임시 컴포넌트.
/// 숫자키 1~5로 상태를 바꾼다. 실제 연동이 끝나면 이 컴포넌트는 지워도 된다.
/// </summary>
public class AIAssistantStateTester : MonoBehaviour
{
    public AIAssistantVisual visual;
    public AIAssistantSpeechBubble bubble;
    [Tooltip("현재 상태를 화면 좌상단에 표시")]
    public bool showOnScreenLabel = true;

    [Header("Space로 띄워볼 예시 문구")]
    [TextArea(2, 4)]
    public string[] sampleMessages =
    {
        "EGFR 단백질의 키나아제 도메인을 불러왔어요. 리본을 클릭하면 나선 구조로 들어갈 수 있습니다.",
        "858번 잔기가 류신에서 아르기닌으로 바뀌었네요. 폐암에서 자주 보이는 활성화 돌연변이입니다.",
        "이 부위는 pLDDT가 70 아래예요. 예측 신뢰도가 낮으니 해석에 주의하세요.",
    };

    private int _sampleIndex;

    private static readonly AIAssistantState[] Order =
    {
        AIAssistantState.Idle,
        AIAssistantState.Listening,
        AIAssistantState.Thinking,
        AIAssistantState.Speaking,
        AIAssistantState.Alert,
    };

    private void Awake()
    {
        if (visual == null) visual = GetComponent<AIAssistantVisual>();
        if (bubble == null) bubble = GetComponentInChildren<AIAssistantSpeechBubble>(includeInactive: true);

        if (bubble == null)
            Debug.LogWarning("[AIAssistantStateTester] 말풍선을 찾지 못했습니다. " +
                             "Tools > Taming Mutants > AI 비서 생성 을 다시 실행해 새로 만드세요.", this);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (bubble != null && keyboard.spaceKey.wasPressedThisFrame && sampleMessages.Length > 0)
        {
            bubble.Say(sampleMessages[_sampleIndex % sampleMessages.Length]);
            _sampleIndex++;
        }

        if (visual == null) return;

        // 매 프레임 배열을 새로 만들면 GC가 쌓이므로 직접 훑는다.
        // digit1KeyControl은 Key.Digit1 ~ Digit5가 연속된 enum 값이라 오프셋으로 접근할 수 있다.
        for (int i = 0; i < Order.Length; i++)
        {
            KeyControl key = keyboard[(Key)((int)Key.Digit1 + i)];
            if (key != null && key.wasPressedThisFrame)
            {
                visual.SetState(Order[i]);
                return;
            }
        }
    }

    private void OnGUI()
    {
        if (!showOnScreenLabel || visual == null) return;

        GUI.Label(new Rect(10, 10, 600, 20),
            $"AI 비서 상태: {visual.CurrentState}  (1=Idle 2=Listening 3=Thinking 4=Speaking 5=Alert / Space=말풍선)");
    }
}
