using System;
using System.Text;
using UnityEngine;

/// <summary>
/// AudioClip ↔ WAV(16비트 PCM) 변환.
///
/// 두 방향이 다 필요하다. 마이크로 녹음한 것은 파일로 만들어 올려야 하고(Encode),
/// 음성 합성이 돌려준 바이트는 AudioClip으로 되돌려야 재생할 수 있다(Decode).
///
/// mp3 대신 wav를 쓰는 이유: Unity의 런타임 mp3 디코딩은 플랫폼마다 지원이 갈리고
/// <c>DownloadHandlerAudioClip</c>은 URL 확장자에 따라 동작이 달라지는 경우가 있다.
/// wav는 헤더가 단순해서 직접 읽으면 어느 플랫폼에서든 같은 결과가 나온다.
/// 용량은 커지지만 한 번에 몇 초짜리 문장이라 문제가 되지 않는다.
/// </summary>
public static class WavCodec
{
    private const int HeaderSize = 44;
    private const short PcmFormat = 1;
    private const short BitsPerSample = 16;

    /// <summary>AudioClip을 16비트 PCM WAV 바이트로 만든다.</summary>
    public static byte[] Encode(AudioClip clip)
    {
        if (clip == null) return null;

        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        return Encode(samples, clip.channels, clip.frequency);
    }

    /// <summary>-1~1 부동소수 샘플을 16비트 PCM WAV 바이트로 만든다.</summary>
    public static byte[] Encode(float[] samples, int channels, int sampleRate)
    {
        if (samples == null || samples.Length == 0) return null;

        int dataSize = samples.Length * sizeof(short);
        var bytes = new byte[HeaderSize + dataSize];
        int offset = 0;

        WriteAscii(bytes, ref offset, "RIFF");
        WriteInt32(bytes, ref offset, HeaderSize - 8 + dataSize);
        WriteAscii(bytes, ref offset, "WAVE");

        WriteAscii(bytes, ref offset, "fmt ");
        WriteInt32(bytes, ref offset, 16);                       // fmt 청크 길이
        WriteInt16(bytes, ref offset, PcmFormat);
        WriteInt16(bytes, ref offset, (short)channels);
        WriteInt32(bytes, ref offset, sampleRate);
        WriteInt32(bytes, ref offset, sampleRate * channels * BitsPerSample / 8); // byte rate
        WriteInt16(bytes, ref offset, (short)(channels * BitsPerSample / 8));     // block align
        WriteInt16(bytes, ref offset, BitsPerSample);

        WriteAscii(bytes, ref offset, "data");
        WriteInt32(bytes, ref offset, dataSize);

        foreach (float sample in samples)
        {
            // 클램프가 없으면 1을 살짝 넘는 값이 short로 넘어갈 때 뒤집혀 딱딱 끊기는 잡음이 된다.
            short value = (short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
            WriteInt16(bytes, ref offset, value);
        }

        return bytes;
    }

    /// <summary>
    /// WAV 바이트를 AudioClip으로 되돌린다. 읽지 못하면 null과 사유를 돌려준다.
    ///
    /// 청크를 순서대로 훑는 이유: "data"가 항상 44바이트째에 오지 않는다.
    /// 합성 서비스에 따라 "LIST"(제작 정보) 같은 청크를 앞에 끼워 보내는데,
    /// 고정 오프셋으로 읽으면 그 바이트를 소리로 해석해 잡음이 섞인다.
    /// </summary>
    public static AudioClip Decode(byte[] wav, string clipName, out string error)
    {
        error = null;

        if (wav == null || wav.Length < 12)
        {
            error = "WAV 데이터가 너무 짧습니다.";
            return null;
        }

        if (ReadAscii(wav, 0, 4) != "RIFF" || ReadAscii(wav, 8, 4) != "WAVE")
        {
            error = "RIFF/WAVE 헤더가 아닙니다.";
            return null;
        }

        int channels = 0, sampleRate = 0, bits = 0;
        int dataStart = -1, dataLength = 0;

        int cursor = 12;
        while (cursor + 8 <= wav.Length)
        {
            string id = ReadAscii(wav, cursor, 4);
            int size = BitConverter.ToInt32(wav, cursor + 4);
            int body = cursor + 8;

            if (size < 0 || body + size > wav.Length) size = wav.Length - body; // 잘린 파일 방어

            if (id == "fmt " && size >= 16)
            {
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToInt16(wav, body + 14);
            }
            else if (id == "data")
            {
                dataStart = body;
                dataLength = size;
                break;
            }

            cursor = body + size + (size % 2); // 청크는 짝수 경계에 맞춰진다
        }

        if (dataStart < 0 || channels <= 0 || sampleRate <= 0)
        {
            error = "fmt 또는 data 청크를 찾지 못했습니다.";
            return null;
        }

        if (bits != 16)
        {
            error = $"16비트 PCM만 읽을 수 있습니다 (받은 값: {bits}비트).";
            return null;
        }

        int sampleCount = dataLength / sizeof(short);
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            samples[i] = BitConverter.ToInt16(wav, dataStart + i * sizeof(short)) / (float)short.MaxValue;

        AudioClip clip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // --- 쓰기 보조 ---

    private static void WriteAscii(byte[] target, ref int offset, string text)
    {
        Encoding.ASCII.GetBytes(text, 0, text.Length, target, offset);
        offset += text.Length;
    }

    private static void WriteInt32(byte[] target, ref int offset, int value)
    {
        // WAV는 리틀엔디언 고정이다. BitConverter는 실행 환경을 따라가므로 직접 쪼갠다.
        target[offset++] = (byte)value;
        target[offset++] = (byte)(value >> 8);
        target[offset++] = (byte)(value >> 16);
        target[offset++] = (byte)(value >> 24);
    }

    private static void WriteInt16(byte[] target, ref int offset, short value)
    {
        target[offset++] = (byte)value;
        target[offset++] = (byte)(value >> 8);
    }

    private static string ReadAscii(byte[] source, int offset, int length)
    {
        return Encoding.ASCII.GetString(source, offset, length);
    }
}
