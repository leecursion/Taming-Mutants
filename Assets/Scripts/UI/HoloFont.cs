using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 한글을 쓸 수 있는 폰트를 한 곳에서 만들어 공유한다.
///
/// Unity 내장 LegacyRuntime.ttf에는 한글 글리프가 없어서 OS 폰트로 갈아끼워야 하는데,
/// <see cref="Font.CreateDynamicFontFromOSFont(string[], int)"/>는 호출할 때마다 별개의
/// 동적 폰트를 만들고 각자 자기 아틀라스를 굽는다. 말풍선·퀘스트 보드·HUD가 따로 만들면
/// 같은 글자를 여러 번 굽느라 텍스처만 늘어난다. 여기서 하나만 만들어 돌려 쓴다.
///
/// Quest(Android) 빌드에서는 OS 폰트 목록을 신뢰할 수 없으므로,
/// 한글 TTF를 프로젝트에 임포트해 <see cref="overrideFont"/>에 넣는 편이 안전하다.
/// </summary>
public static class HoloFont
{
    /// <summary>여기에 폰트를 넣어두면 OS 폰트 탐색을 건너뛰고 이것만 쓴다.</summary>
    public static Font overrideFont;

    private static readonly string[] DefaultCandidates =
    {
        "Malgun Gothic", "맑은 고딕", "NanumGothic", "Noto Sans CJK KR", "AppleSDGothicNeo-Regular",
    };

    private static Font _cached;

    /// <summary>
    /// 공용 폰트. 런타임에 만든 동적 폰트는 도메인 리로드를 넘기지 못하고 null이 되므로
    /// (플레이 중 스크립트를 고치면 글자가 통째로 사라진다) 매번 살아 있는지 확인하고
    /// 죽어 있으면 다시 만든다.
    /// </summary>
    public static Font Resolve(string[] candidates = null)
    {
        if (overrideFont != null) return overrideFont;
        if (_cached != null) return _cached;

        _cached = Font.CreateDynamicFontFromOSFont(candidates ?? DefaultCandidates, 32);
        return _cached;
    }

    /// <summary>대상과 그 자식의 모든 Text를 공용 폰트로 교체한다.</summary>
    public static void Apply(GameObject root, string[] candidates = null)
    {
        if (root == null) return;

        Font font = Resolve(candidates);
        if (font == null) return;

        foreach (Text text in root.GetComponentsInChildren<Text>(includeInactive: true))
            text.font = font;
    }
}
