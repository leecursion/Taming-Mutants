using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StreamingAssets/quests/*.json 스키마 — 도킹 퀘스트 1개의 정의.
/// 퀘스트 추가 = 이 스키마의 JSON 파일 1개 + index.json에 파일명 등록.
/// 코드/씬 수정 없이 단백질 구조·타깃 잔기·후보물질 목록을 전부 교체할 수 있다.
/// </summary>
[Serializable]
public class DockingQuestDefinition
{
    public string id;                 // 예: "kras_g12c"
    public string title;              // 패널/로그 표시용
    public string protein_json;       // StreamingAssets 상대 경로, 예: "structures/P01116.json"

    [Tooltip("공유결합 대상 잔기 res_id")]
    public int target_residue_id;
    [Tooltip("공유결합 대상 원자 이름 (예: SG). 없으면 element S → CA 순 폴백")]
    public string target_atom_name;
    public List<int> pocket_residue_ids;

    public string compounds_folder;   // 예: "compounds"
    public List<string> compound_files;

    [Tooltip("0이면 컨트롤러 기본값 사용")]
    public float entrance_offset;

    [Tooltip("이 구조에서 미리 지정한 Helix 구간 (StructureLevelController에 주입)")]
    public List<QuestHelixRegion> helix_regions;
}

[Serializable]
public class QuestHelixRegion
{
    public string label;
    public int start_res_id;
    public int end_res_id;
}

/// <summary>StreamingAssets/quests/index.json — 퀘스트 파일명 목록 (진행 순서대로).</summary>
[Serializable]
public class QuestCatalogData
{
    public List<string> quests;
}
