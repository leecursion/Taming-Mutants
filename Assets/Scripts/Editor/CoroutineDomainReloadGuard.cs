using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이 도중 스크립트를 고쳐 도메인이 다시 로드될 때, 돌고 있던 코루틴을 먼저 정리한다.
///
/// 네이티브 Coroutine 객체는 C# 이터레이터를 GCHandle로 붙잡고 있다. 도메인이 바뀌면 그 핸들이
/// 가리키던 관리 객체가 사라지므로, 나중에 코루틴이 해제될 때 유니티가
/// "Release of invalid GC handle. The handle is from a previous domain."과
/// "Assertion failed on expression: '!m_CoroutineEnumeratorGCHandle.HasTarget()'"를 함께 남긴다.
/// 도메인이 내려가기 직전에 코루틴을 멈춰 핸들이 살아 있는 동안 해제되게 하면 두 메시지가 사라진다.
///
/// 어차피 리로드를 넘기지 못하는 코루틴만 멈춘다 — 리로드 뒤에는 어떤 코루틴도 이어지지 않는다.
/// 에디터 전용이며 빌드에는 포함되지 않는다. 같은 이유로 리로드를 못 넘기는 것들이 더 있다:
/// HoloFont의 동적 폰트, HoloSpriteFactory의 런타임 텍스처 — 둘 다 각자 되살리는 길을 갖고 있다.
/// </summary>
[InitializeOnLoad]
public static class CoroutineDomainReloadGuard
{
    static CoroutineDomainReloadGuard()
    {
        AssemblyReloadEvents.beforeAssemblyReload += StopRunningCoroutines;
    }

    private static void StopRunningCoroutines()
    {
        // 플레이 중이 아니면 돌고 있는 코루틴도 없다.
        if (!EditorApplication.isPlaying) return;

        // 꺼져 있는 오브젝트까지 훑는다. 코루틴을 시작한 뒤 비활성으로 바뀐 컴포넌트도
        // 네이티브 코루틴 객체는 그대로 들고 있기 때문이다.
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour != null) behaviour.StopAllCoroutines();
        }
    }
}
