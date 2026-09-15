using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public class MoleculeViewSpec
{
    public string id, title, pdbId, chains, description;
    public string view = "ribbon";
    public int residueStart, residueEnd;
    public string ligand, secondaryStructure;

    public void NormalizeAndValidate()
    {
        pdbId = (pdbId ?? "").Trim().ToUpperInvariant();
        chains = string.Join(",", (chains ?? "").Split(',').Select(c => c.Trim()).Distinct());
        if (!Regex.IsMatch(pdbId, @"^[0-9][A-Z0-9]{3}$"))
            throw new InvalidOperationException("검색 서버가 올바른 4자리 PDB ID를 반환하지 않았어요. 단백질 이름을 다시 알려주시겠어요?");
        if (!Regex.IsMatch(chains, @"^([A-Za-z0-9](,[A-Za-z0-9]){0,7})?$"))
            throw new InvalidOperationException("검색 서버가 지원하지 않는 체인 범위를 반환했어요. 체인 지정 없이 단백질 이름으로 다시 검색해 주시겠어요?");
        if (residueStart < 0 || residueEnd < residueStart || residueEnd > 9999)
            throw new InvalidOperationException("잔기 범위가 올바르지 않아요. 시작과 끝 번호를 다시 알려주시겠어요?");
    }
}

public static class MoleculeCatalog
{
    public static readonly MoleculeViewSpec[] Entries = {
        new MoleculeViewSpec { id="insulin", title="사람 인슐린", pdbId="1TRZ", chains="A,B", description="인슐린 한 분자의 A·B 체인 · 결정 구조의 일부" },
        new MoleculeViewSpec { id="hemoglobin", title="사람 헤모글로빈", pdbId="4HHB", chains="A", description="산소 비결합 상태 · 전체 4개 소단위 중 알파 소단위 하나와 헴" },
        new MoleculeViewSpec { id="gfp", title="녹색 형광 단백질 (GFP)", pdbId="1EMA", chains="A", description="해파리 유래 GFP · 한 체인과 발색단" }
    };
    public static MoleculeViewSpec Find(string id) => Array.Find(Entries, e => e.id == id);
    // Only exact supported requests bypass semantic resolution. Species/state qualifiers
    // must never silently collapse to a different representative structure.
    //
    // 군더더기는 떼되 이름 자체는 완전일치로 본다. "인슐린 구조 보여줘"가 서버로 넘어가면
    // 지원한다고 적어둔 대표 분자조차 네트워크와 LLM 판단에 기대게 되고, 반대로 포함 검사로
    // 넓히면 "인슐린 수용체"처럼 다른 단백질까지 인슐린으로 바뀌어 버린다.
    public static MoleculeViewSpec Match(string input)
    {
        string s = Regex.Replace((input ?? "").ToLowerInvariant(), @"[\s?!.,]", "");
        foreach (string suffix in new[] { "보여주세요", "보여줘", "불러주세요", "불러줘", "검색해줘", "찾아줘", "띄워줘" })
            s = s.Replace(suffix, "");
        foreach (string filler in new[] { "의구조", "구조", "입체", "3d", "모양", "그림", "좀", "한번", "다시" })
            s = s.Replace(filler, "");
        s = s.TrimEnd('을', '를');
        // 종을 바꿔 말한 요청("쥐 인슐린")은 여기서 걸러져 서버로 가야 한다 — 사람 구조로
        // 대신 보여주면 안 되므로, 대표 구조와 같은 종일 때만 접두어를 떼어낸다.
        foreach (string species in new[] { "사람의", "사람", "인간", "human" })
            if (s.StartsWith(species)) { s = s.Substring(species.Length); break; }
        if (new[] { "인슐린", "insulin" }.Contains(s)) return Find("insulin");
        if (new[] { "헤모글로빈", "혈색소", "hemoglobin" }.Contains(s)) return Find("hemoglobin");
        if (new[] { "gfp", "녹색형광단백질", "녹색형광단백질(gfp)", "형광단백질" }.Contains(s)) return Find("gfp");
        return null;
    }
}

public sealed class ExplorationAtom
{
    public string chain, residueKey, name, residue, element;
    public int number;
    public bool hetero;
    public Vector3 position;
}

public static class ExplorationPdbParser
{
    // 실제 표시량을 묶는 것은 MaxAtoms다. MaxBytes는 내려받다가 멈추는 한도일 뿐인데,
    // 8 MiB로는 NMR 앙상블(1DT7은 모델 여러 개가 겹쳐 11.7 MiB)이 통째로 걸린다.
    // 파서는 첫 ENDMDL에서 멈추므로 뒤 모델은 읽지도 않는다 — 받는 한도만 넓힌다.
    public const int MaxBytes = 24 * 1024 * 1024;
    public const int MaxAtoms = 60000;
    public static List<ExplorationAtom> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBytes)
            throw new FormatException("구조 파일이 비어 있거나 크기 제한을 넘었습니다.");
        var atoms = new List<ExplorationAtom>();
        var seen = new HashSet<string>();
        int segment = 0;
        using (var reader = new StringReader(text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("ENDMDL")) break;
                if (line.StartsWith("TER")) { segment++; continue; }
                bool hetero = line.StartsWith("HETATM");
                if (!hetero && !line.StartsWith("ATOM  ")) continue;
                if (line.Length < 54) throw new FormatException("좌표 레코드가 불완전합니다.");
                if (line[16] != ' ' && line[16] != 'A') continue;
                string name = line.Substring(12,4).Trim(), res = line.Substring(17,3).Trim();
                if (res == "HOH" || res == "WAT" || res == "DOD") continue;
                string element = line.Length >= 78 ? line.Substring(76,2).Trim().ToUpperInvariant() : "";
                if (element.Length == 0) element = name.TrimStart('0','1','2','3','4')[0].ToString();
                if (element == "H" || element == "D") continue;
                string chain = line[21].ToString(), key = chain + ":" + segment + ":" + line.Substring(22,5).Trim();
                if (!seen.Add(key + ":" + name)) continue;
                if (!int.TryParse(line.Substring(22,4), out int number)) throw new FormatException("잔기 번호가 올바르지 않습니다.");
                float x = Coordinate(line,30), y = Coordinate(line,38), z = Coordinate(line,46);
                atoms.Add(new ExplorationAtom { chain=chain, residueKey=key, name=name, residue=res,
                    element=element, number=number, hetero=hetero, position=new Vector3(x,y,z) });
                if (atoms.Count > MaxAtoms) throw new FormatException("이 구조는 현재 탐색 범위를 넘습니다. 더 작은 구조를 선택해 주세요.");
            }
        }
        if (atoms.Count == 0) throw new FormatException("표시할 원자 좌표가 없습니다.");
        return atoms;
    }
    static float Coordinate(string line, int start)
    {
        if (!float.TryParse(line.Substring(start,8), NumberStyles.Float, CultureInfo.InvariantCulture, out float n)
            || float.IsNaN(n) || float.IsInfinity(n)) throw new FormatException("원자 좌표가 올바르지 않습니다.");
        return n;
    }
}

