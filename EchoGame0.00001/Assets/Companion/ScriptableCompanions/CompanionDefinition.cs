using UnityEngine;

public enum CompanionRole
{
    Tank,
    Damage,
    Support,
    Controller
}

// One asset = one companion.
// Each asset stores a companion's name, stats, and which prefab to spawn.
// To make a new companion: right-click in the Project window >
// Create > EchoGame > Companion Definition, then fill in the fields.
[CreateAssetMenu(fileName = "NewCompanion", menuName = "EchoGame/Companion Definition")]
public class CompanionDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Short unique name used by the save system, e.g. \"layla\". Once the game has save files, don't change it.")]
    public string id;
    public string displayName;
    public CompanionRole role = CompanionRole.Damage;
    [TextArea] public string description;

    [Header("Visuals")]
    [Tooltip("Picture shown on the companion select screen.")]
    public Sprite portrait;

    [Header("Spawning")]
    [Tooltip("The prefab that gets spawned for this companion.")]
    public GameObject prefab;

    [Header("Stats")]
    public float maxHealth = 100f;
}
