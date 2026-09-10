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

    public static bool HasValidEvidence(OralGrade grade, string answer, string previousAnswer)
    {
        if (grade == null || !grade.evaluated) return false;
        if (string.IsNullOrWhiteSpace(grade.evidence)) return !grade.understood;
        return (!string.IsNullOrEmpty(answer) && answer.Contains(grade.evidence)) ||
               (!string.IsNullOrEmpty(previousAnswer) && previousAnswer.Contains(grade.evidence));
    }
}
