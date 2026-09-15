using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public class OralGrade
{
    public bool understood;
    public string missingConcept;
    public string followUp;
    public bool evaluated;
    public string evidence;
}

public static class OralGradeProtocol
{
    /// <summary>누락·중복·타입 오류를 명시적으로 거른다. 잘못된 응답은 학습자의 오답이 아니다.</summary>
    public static OralGrade ParseGrade(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16000) return null;
        try
        {
            var value = JObject.Parse(json, new JsonLoadSettings {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (value["understood"]?.Type != JTokenType.Boolean ||
                value["missingConcept"]?.Type != JTokenType.String ||
                value["followUp"]?.Type != JTokenType.String) return null;
            // 구 버전 서버는 실패를 통과로 반환했다. evaluated 없는 응답은 확인 불가로 취급한다.
            if (value["evaluated"] != null && value["evaluated"].Type != JTokenType.Boolean) return null;
            if (value["evidence"] != null && value["evidence"].Type != JTokenType.String) return null;
            return new OralGrade {
                understood = (bool)value["understood"],
                missingConcept = (string)value["missingConcept"],
                followUp = (string)value["followUp"],
                evaluated = value["evaluated"] != null && (bool)value["evaluated"],
                evidence = (string)value["evidence"] ?? ""
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// 인용 대조용 정규화 — 글자와 숫자만 남긴다.
    ///
    /// 통과 판정에는 답변의 원문 인용이 필요한데, 모델이 인용을 옮기면서 띄어쓰기나
    /// 문장부호를 흔히 바꾼다("붙잡아요." -> "붙잡아요", "황 원자" -> "황원자").
    /// 글자 그대로 비교하면 제대로 설명한 학습자가 인용 표기 차이만으로 채점 불가가 됐다.
    ///
    /// 지어낸 인용은 여전히 걸린다 — 같은 글자가 같은 순서로 답변에 실제로 있어야 한다.
    /// 서버(server/src/index.js)의 normalizeQuote가 같은 규칙을 구현한다. 한쪽만 고치면
    /// 서버가 통과시킨 답을 여기서 다시 거부해 학습자에게는 침묵으로 보인다.
    /// </summary>
    public static string NormalizeForQuote(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
            if (char.IsLetterOrDigit(c)) builder.Append(c);
        return builder.ToString();
    }

    public static bool HasValidEvidence(OralGrade grade, string answer, string previousAnswer)
    {
        if (grade == null || !grade.evaluated) return false;
        if (string.IsNullOrWhiteSpace(grade.evidence)) return !grade.understood;
        string needle = NormalizeForQuote(grade.evidence);
        if (needle.Length == 0) return !grade.understood;
        return NormalizeForQuote(answer).Contains(needle) ||
               NormalizeForQuote(previousAnswer).Contains(needle);
    }
}
