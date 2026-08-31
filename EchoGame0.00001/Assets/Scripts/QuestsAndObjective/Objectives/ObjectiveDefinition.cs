using UnityEngine;

// How an objective knows it's finished.
// Add new kinds to the END of this list, or saved data will point at the wrong one.
public enum ObjectiveKind
{
    KillCount,   // kill a set number of enemies
    ClearGroup,  // kill every enemy in a group
}

// Where an objective is up to. Add new states at the END.
// There's no Failed state yet because nothing in the game can fail a mission.
public enum ObjectiveState
{
    Inactive,
    Active,
    Complete,
}

// One asset = one objective, e.g. "Clear the warehouse".
// Objectives get added to a Quest, and the quest's list is the order they happen in.
// To make a new objective: right-click in the Project window >
// Create > EchoGame > Objective, then fill in the fields.
//
// This asset is just data. QuestTracker keeps the progress, never this.
[CreateAssetMenu(fileName = "NewObjective", menuName = "EchoGame/Objective")]
public class ObjectiveDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Short unique name used by the save system and by QuestTracker.CompleteObjective(), e.g. \"clear-warehouse\". Once the game has save files, don't change it.")]
    public string id;
    [Tooltip("The line the player reads on the HUD, e.g. \"Clear the warehouse\".")]
    public string title;
    [TextArea]
    [Tooltip("Longer text for a quest log later. NOT shown on the HUD.")]
    public string description;

    [Header("Completion")]
    [Tooltip("KILL COUNT: finishes after Required Count kills. CLEAR GROUP: finishes when every matching enemy in the level is dead, and works its own total out from the enemies actually there.")]
    public ObjectiveKind kind = ObjectiveKind.KillCount;
    [Tooltip("Which enemies count. Matches the Group Id on an enemy's ObjectiveTarget component. LEAVE EMPTY and every enemy counts — handy for a plain \"kill 5 enemies\". Clear Group needs this filled in, since it can only see enemies carrying an ObjectiveTarget.")]
    public string targetGroup;
    [Tooltip("KILL COUNT only — how many kills finish this. Ignored by Clear Group.")]
    [Min(1)] public int requiredCount = 1;

    [Header("Options")]
    [Tooltip("Tick for a side objective: it shows on the HUD and can still be completed, but the quest finishes without it.")]
    public bool optional;

    // Checks whether an enemy counts toward this objective.
    // An empty Target Group is a wildcard, so every enemy counts.
    public bool Matches(string groupId)
    {
        if (string.IsNullOrWhiteSpace(targetGroup)) return true;
        if (string.IsNullOrWhiteSpace(groupId)) return false;
        return string.Equals(targetGroup.Trim(), groupId.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }
}
