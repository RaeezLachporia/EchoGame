using UnityEngine;

// Put this on an empty GameObject with a TRIGGER collider stretched across the
// doorway into a wave room. The moment the player walks through it, the WaveSpawner
// it points at starts its waves.
//
// Kept separate from WaveSpawner because the two belong in different places: the
// spawner sits where the enemies come from, this sits in the doorway the player
// crosses. One spawner can be started by several of these if a room has two ways in.
// BoxCollider rather than the abstract Collider: Unity can actually add a box for
// you when the script goes on a bare GameObject, and a box is the right shape for a
// doorway. Swap it for another collider afterwards if you need to — everything below
// reads GetComponent<Collider>(), so any shape works.
[RequireComponent(typeof(BoxCollider))]
public class WaveStartTrigger : MonoBehaviour
{
    [Header("Spawner")]
    [Tooltip("The wave spawner this trigger sets off. Nothing happens without it.")]
    [SerializeField] private WaveSpawner spawner;

    [Header("Trigger")]
    [Tooltip("Tag on the player object. Anything else walking through is ignored, so a companion running ahead can't set the fight off early.")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("Ticked (normal) this fires once and then switches itself off. Untick only if you want it to keep firing — the spawner ignores repeat starts anyway.")]
    [SerializeField] private bool fireOnce = true;

    [Header("Debug")]
    [Tooltip("Log when the player crosses this trigger and which spawner it started.")]
    [SerializeField] private bool logTrigger = true;

    private bool fired;

    // Runs when the component is first added in the editor. Makes the collider a
    // trigger straight away, so a box dropped in a doorway doesn't become a wall the
    // player walks into.
    void Reset()
    {
        Collider box = GetComponent<Collider>();
        if (box != null) box.isTrigger = true;
    }

    void Start()
    {
        if (spawner == null)
            Debug.LogWarning($"[WaveStartTrigger] '{name}' has no Spawner assigned, so walking through it will never start any waves. Drag the room's WaveSpawner into the Spawner field.", this);

        // The usual cause of "I walk into the doorway and nothing happens", and it
        // looks like a broken script rather than a checkbox.
        Collider box = GetComponent<Collider>();
        if (box != null && !box.isTrigger)
            Debug.LogWarning($"[WaveStartTrigger] '{name}' has a collider with Is Trigger UNTICKED, so the player bumps into it like a wall instead of passing through and setting it off. Tick Is Trigger on the collider.", this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (fireOnce && fired) return;
        if (!other.CompareTag(playerTag)) return;
        if (spawner == null) return;

        fired = true;

        if (logTrigger)
            Debug.Log($"[WaveStartTrigger] '{name}' — player entered, starting waves on '{spawner.name}'.", this);

        spawner.StartWaves();
    }

    // No renderer on a trigger volume, so draw it. Green while it can still fire,
    // grey once it has gone off.
    void OnDrawGizmos()
    {
        Collider box = GetComponent<Collider>();
        if (box == null) return;

        Gizmos.color = fired
            ? new Color(0.5f, 0.5f, 0.5f, 0.25f)
            : new Color(0.3f, 1f, 0.4f, 0.25f);

        Bounds bounds = box.bounds;
        Gizmos.DrawCube(bounds.center, bounds.size);
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
}
