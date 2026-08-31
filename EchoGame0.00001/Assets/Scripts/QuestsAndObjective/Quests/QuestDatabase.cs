using System.Collections.Generic;
using UnityEngine;

// The list of every quest in the game.
// QuestTracker starts the ones marked Start On Load, and looks quests up here when
// something calls StartQuest("some-id").
// When you create a new quest asset, add it to this list.
[CreateAssetMenu(fileName = "QuestDatabase", menuName = "EchoGame/Quest Database")]
public class QuestDatabase : ScriptableObject
{
    [Tooltip("Every quest in the game. Add new quests here.")]
    public List<QuestDefinition> allQuests = new List<QuestDefinition>();

    // Finds a quest by its id, e.g. "secure-docks". Used by StartQuest, and later
    // when loading a save.
    public QuestDefinition FindById(string id)
    {
        foreach (QuestDefinition quest in allQuests)
        {
            if (quest != null && quest.id == id)
                return quest;
        }
        return null;
    }
}
