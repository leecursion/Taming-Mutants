using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 홀로그램 톤 UI에 쓰는 스프라이트를 코드로 굽는다.
///
/// AIAssistantSetupMenu는 같은 모양을 에디터에서 png 에셋으로 저장하지만,
/// 퀘스트 보드처럼 개수가 데이터에 따라 달라지는 UI는 런타임에 만들어야 해서
/// 에셋 경로로 접근할 수 없다(Resources 폴더 밖에 있다). 그래서 같은 수식을
/// 런타임용으로 한 번 더 둔다 — 대신 종류별로 한 장만 굽고 캐시해 돌려 쓴다.
///
/// 기본 UI 스킨을 쓰지 않는 이유는 저해상도 회색이라 홀로그램 톤과 맞지 않기 때문이다.
/// </summary>
public static class HoloSpriteFactory
{
    // 초과 샘플링 배율. PPU를 같은 배율로 올려두면 화면상 크기와 모서리 곡률은 그대로인 채
    // 텍셀 밀도만 올라가서, 패널을 크게 늘려도 모서리가 계단으로 보이지 않는다.
    private const float Supersample = 2f;
    private const float PixelsPerUnit = 100f * Supersample;

    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    /// <summary>둥근 사각형 배경. 9-slice라 어떤 비율로 늘려도 모서리가 일그러지지 않는다.</summary>
    public static Sprite Panel()
    {
        return GetOrBuild("panel", 128, new Vector4(36, 36, 36, 36), (dx, dy, size) =>
        {
            float d = RoundedBoxDistance(dx, dy, size * 0.5f, size * 0.5f, 28f);
            return Mathf.Clamp01(0.5f - d); // 경계에서 1픽셀만 부드럽게
        });
    }

    /// <summary>둥근 사각형 외곽선.</summary>
    public static Sprite Stroke()
    {
        return GetOrBuild("stroke", 128, new Vector4(36, 36, 36, 36), (dx, dy, size) =>
        {
            float d = RoundedBoxDistance(dx, dy, size * 0.5f, size * 0.5f, 28f);
            return Mathf.Clamp01(2f - Mathf.Abs(d) + 0.5f); // 경계선 위에만 남긴다
        });
    }

    /// <summary>바깥으로 번지는 글로우.</summary>
    public static Sprite Glow()
    {
        return GetOrBuild("glow", 192, new Vector4(80, 80, 80, 80), (dx, dy, size) =>
        {
            const float falloff = 40f;
            // 모양을 텍스처 경계까지 채우면 바깥으로 번질 여백이 없어 직선 구간이 잘려 나간다.
            // 도형을 falloff만큼 안쪽으로 줄여 사방에 번질 자리를 남긴다.
            float halfExtent = size * 0.5f - falloff;

            float d = RoundedBoxDistance(dx, dy, halfExtent, halfExtent, 36f);
            if (d <= 0f) return 1f;

            float t = Mathf.Clamp01(1f - d / falloff);
            return t * t; // 제곱해서 바깥으로 갈수록 빠르게 옅어지게
        });
    }

    /// <summary>꽉 찬 원. 상태 점, 난이도 표시 등에 쓴다.</summary>
    public static Sprite Circle()
    {
        return GetOrBuild("circle", 64, Vector4.zero, (dx, dy, size) =>
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy) - (size * 0.5f - 1f);
            return Mathf.Clamp01(0.5f - d);
        });
    }

    private static Sprite GetOrBuild(string key, int size, Vector4 border, Func<float, float, int, float> alphaAt)
    {
        // 도메인 리로드로 텍스처가 날아가면 사전에 죽은 참조만 남는다. 그때는 다시 굽는다.
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            // 런타임 생성 텍스처는 씬에 저장되지 않으므로 씬 전환에도 살아남게 둔다.
            hideFlags = HideFlags.HideAndDontSave,
        };

        var pixels = new Color32[size * size];
        float center = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center;
                float dy = y + 0.5f - center;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(dx, dy, size)) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            PixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            border);
        sprite.hideFlags = HideFlags.HideAndDontSave;

        Cache[key] = sprite;
        return sprite;
    }

    /// <summary>둥근 사각형까지의 부호 있는 거리. 음수면 안쪽, 양수면 바깥쪽.</summary>
    private static float RoundedBoxDistance(float px, float py, float halfWidth, float halfHeight, float radius)
    {
        float qx = Mathf.Abs(px) - halfWidth + radius;
        float qy = Mathf.Abs(py) - halfHeight + radius;

        float outsideX = Mathf.Max(qx, 0f);
        float outsideY = Mathf.Max(qy, 0f);
        float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);

        return outside + inside - radius;
    }
}
