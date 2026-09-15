using System.Collections.Generic;
using UnityEngine;

// Seals a room once the whole party has walked into it — the missing half of an
// ObjectiveBarrier entrance. The barrier starts open so the party can get in; this
// watches the room and, once everyone's inside, tells the barrier(s) to Close() and
// (optionally) sets a wave fight going.
//
// Put it on an empty GameObject with a BOX that COVERS THE ROOM INTERIOR, not a thin
// strip across the doorway — it's checking who is standing inside the box, so the box
// has to be the space the party ends up in. The collider is only used for its shape,
// so it's left as a trigger and never blocks anything.
//
// "Everyone" is the player plus every live companion in Comapnion.Active. Membership
// is read from position each frame rather than from trigger events, so it doesn't
// matter whether the companions carry rigidbodies, and a companion that dies drops out
// of the party count on its own.
[RequireComponent(typeof(BoxCollider))]
public class RoomSealTrigger : MonoBehaviour
{
    [Header("What to seal")]
    [Tooltip("The entrance barrier(s) to shut once the party is inside. These should be ObjectiveBarriers with Start Open ticked.")]
    [SerializeField] private ObjectiveBarrier[] barriersToClose;
    [Tooltip("Optional. A wave spawner to set going the moment the room seals. Leave empty for a room with hand-placed enemies. If set, you don't also need a WaveStartTrigger — this starts the fight instead.")]
    [SerializeField] private WaveSpawner spawner;

    [Header("Party")]
    [Tooltip("Tag on the player object.")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("Ticked (normal): wait for the player AND every live companion to be inside before sealing, so nobody gets shut out. Untick to seal the moment the player is in, whatever the companions are doing.")]
    [SerializeField] private bool requireWholeParty = true;

    [Header("Debug")]
    [Tooltip("Log when the room seals and what it closed / started.")]
    [SerializeField] private bool logSeal = true;

    private BoxCollider box;
    private Transform player;
    private bool hasSealed;

    void Reset()
    {
        // A trigger from the start, so a box dropped into a room doesn't turn the whole
        // room into a solid block the moment the script is added.
        box = GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = true;
    }

    void Awake()
    {
        box = GetComponent<BoxCollider>();
    }

    void Start()
    {
        if (barriersToClose == null || barriersToClose.Length == 0)
            Debug.LogWarning($"[RoomSealTrigger] '{name}' has no Barriers To Close, so sealing the room won't actually shut anything. Drag the entrance ObjectiveBarrier(s) into the Barriers To Close list.", this);

        AcquirePlayer();
    }

    void Update()
    {
        if (hasSealed) return;

        if (player == null)
        {
            AcquirePlayer();
            if (player == null) return;
        }

        Bounds bounds = box.bounds;

        // Player has to be in first — a companion wandering in alone shouldn't start
        // the fight.
        if (!bounds.Contains(player.position)) return;

        if (requireWholeParty && !WholePartyInside(bounds)) return;

        Seal();
    }

    // True only when every live companion is inside the box. Reads Comapnion.Active
    // fresh each call so a companion that died or was dismissed doesn't hold the seal
    // open forever.
    private bool WholePartyInside(Bounds bounds)
    {
        IReadOnlyList<Comapnion> party = Comapnion.Active;
        for (int i = 0; i < party.Count; i++)
        {
            Comapnion member = party[i];
            if (member == null) continue;
            if (!bounds.Contains(member.transform.position)) return false;
        }
        return true;
    }

    private void Seal()
    {
        hasSealed = true;

        int closed = 0;
        if (barriersToClose != null)
        {
            for (int i = 0; i < barriersToClose.Length; i++)
            {
                if (barriersToClose[i] == null) continue;
                barriersToClose[i].Close();
                closed++;
            }
        }

        if (spawner != null) spawner.StartWaves();

        if (logSeal)
            Debug.Log($"[RoomSealTrigger] '{name}' SEALED — party inside, closed {closed} barrier(s){(spawner != null ? $", started waves on '{spawner.name}'" : "")}.", this);
    }

    private void AcquirePlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) player = go.transform;
    }

    // Draw the room box so it's clear it has to cover the interior, not the doorway.
    // Green while still watching, grey once it has sealed.
    void OnDrawGizmos()
    {
        BoxCollider b = box != null ? box : GetComponent<BoxCollider>();
        if (b == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = hasSealed
            ? new Color(0.5f, 0.5f, 0.5f, 0.15f)
            : new Color(0.3f, 0.6f, 1f, 0.15f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
