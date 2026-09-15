using System;
using UnityEngine;

/// <summary>
/// StreamingAssets 경로를 UnityWebRequest가 받아들이는 URL로 바꾼다.
///
/// Application.streamingAssetsPath는 플랫폼마다 형태가 다르다.
///   Android(Quest) : jar:file:///.../base.apk!/assets  — 이미 스킴이 있다
///   에디터/데스크톱 : /Users/.../Assets/StreamingAssets — 스킴이 없는 맨 경로
/// 맨 경로를 UnityWebRequest.Get에 그대로 넘기면 로컬 파일이 아니라 상대 URL로 해석되어
/// 네트워크 연결을 시도하고 "Cannot connect to destination host"로 실패한다.
/// 그래서 스킴이 없을 때만 file:// URI로 바꾼다. Uri.AbsoluteUri를 쓰면 경로에 공백이나
/// 한글이 섞여 있어도 이스케이프가 함께 처리된다.
/// </summary>
public static class StreamingAssetsUrl
{
    /// <summary>StreamingAssets 기준 상대 경로(예: "quests/index.json")를 URL로 만든다.</summary>
    public static string For(string relativePath)
    {
        string relative = string.IsNullOrEmpty(relativePath) ? string.Empty : relativePath.Replace('\\', '/').TrimStart('/');
        // Path.Combine은 Windows에서 '\'를 붙여 URL을 깨뜨리므로 '/'로 직접 잇는다.
        return ToUrl($"{Application.streamingAssetsPath}/{relative}");
    }

    /// <summary>이미 스킴이 있는 문자열(jar:file://, http://, file://)은 그대로 두고, 맨 경로만 file:// URI로 바꾼다.</summary>
    public static string ToUrl(string pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return pathOrUrl;
        if (pathOrUrl.Contains("://")) return pathOrUrl;
        return new Uri(pathOrUrl.Replace('\\', '/')).AbsoluteUri;
    }
}
