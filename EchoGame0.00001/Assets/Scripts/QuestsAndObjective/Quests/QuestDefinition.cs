using System.Collections.Generic;
using UnityEngine;

// How a quest gives out its objectives.
// Add new flows to the END of this list, or saved data will point at the wrong one.
public enum ObjectiveFlow
{
    Sequential,  // one at a time, in list order
    AllAtOnce,   // all of them from the moment the quest starts
}

// Where a quest is up to. Add new states at the END.
// There's no Failed state yet, same as ObjectiveState.
public enum QuestState
{
    Inactive,
    Active,
    Complete,
}

// One asset = one quest, e.g. "Secure the docks".
// A quest holds a list of objectives. With Sequential flow, the list order is the
// order the player gets them in.
// To make a new quest: right-click in the Project window >
// Create > EchoGame > Quest, then fill in the fields.
//
// This asset is just data. QuestTracker keeps the progress, never this.
[CreateAssetMenu(fileName = "NewQuest", menuName = "EchoGame/Quest")]
public class QuestDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Short unique name used by the save system and by QuestTracker.StartQuest(), e.g. \"secure-docks\". Once the game has save files, don't change it.")]
    public string id;
    [Tooltip("Shown as the header above this quest's objectives on the HUD.")]
    public string title;
    [TextArea]
    [Tooltip("Longer text for a quest log later. NOT shown on the HUD.")]
    public string description;

    [Header("Objectives")]
    [Tooltip("The objectives this quest is made of. WITH SEQUENTIAL FLOW THE ORDER MATTERS — the player gets them top to bottom.")]
    public List<ObjectiveDefinition> objectives = new List<ObjectiveDefinition>();
    [Tooltip("SEQUENTIAL: one objective at a time, in list order. ALL AT ONCE: the whole list is active from the start, finishable in any order.")]
    public ObjectiveFlow flow = ObjectiveFlow.Sequential;

    [Header("Starting")]
    [Tooltip("Tick and this quest begins as soon as the level loads. Untick for a quest something else starts — QuestTracker.StartQuest(id), or the Next Quest of an earlier one.")]
    public bool startOnLoad;
    [Tooltip("Optional. The quest that starts automatically when this one completes, so a chain of quests is authored in assets with no code. Leave empty for the last quest in a chain.")]
    public QuestDefinition nextQuest;

    // Finds one of this quest's objectives by its id.
    // Same as CompanionDatabase.FindById.
    public ObjectiveDefinition FindObjective(string objectiveId)
    {
        foreach (ObjectiveDefinition objective in objectives)
        {
            if (objective != null && objective.id == objectiveId)
                return objective;
        }
        return null;
    }
}
