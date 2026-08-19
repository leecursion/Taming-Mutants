using System;
using UnityEngine;

/// <summary>
/// 인트로에서 고를 수 있는 퀘스트 목록.
/// <see cref="QuestSelectionBoard"/>가 이 순서대로 카드를 만든다.
///
/// 만들기: 프로젝트 창 우클릭 > Create > Taming Mutants > Quest Catalog
/// </summary>
[CreateAssetMenu(fileName = "QuestCatalog", menuName = "Taming Mutants/Quest Catalog")]
public class QuestCatalog : ScriptableObject
{
    public QuestDefinition[] quests = Array.Empty<QuestDefinition>();

    public int Count => quests != null ? quests.Length : 0;

    public QuestDefinition Get(int index)
    {
        if (quests == null || index < 0 || index >= quests.Length) return null;
        return quests[index];
    }

    public QuestDefinition Find(string questId)
    {
        if (quests == null || string.IsNullOrEmpty(questId)) return null;

        foreach (QuestDefinition quest in quests)
            if (quest != null && quest.questId == questId) return quest;

        return null;
    }
}
