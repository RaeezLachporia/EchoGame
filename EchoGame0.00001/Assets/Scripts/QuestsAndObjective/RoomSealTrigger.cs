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
    [Tooltip("Log each time the player or a companion crosses into or out of the box, with a running count of who's inside and what's still holding the seal open. Turn this ON while getting the box size and position right, then off once it seals reliably.")]
    [SerializeField] private bool logMembership = true;

    private BoxCollider box;
    private Transform player;
    private bool hasSealed;

    // Debug bookkeeping so membership crossings only log on the frame they change,
    // rather than every frame.
    private bool prevPlayerInside;
    private bool warnedNoPlayer;
    private readonly HashSet<Comapnion> insidePrev = new HashSet<Comapnion>();
    private readonly HashSet<Comapnion> insideNow = new HashSet<Comapnion>();

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

        // The single most common reason it never seals is a box that doesn't actually
        // reach where the party stands, so print its real world size up front.
        if (logMembership && box != null)
            Debug.Log($"[RoomSealTrigger] '{name}' watching a box of world size {box.bounds.size} centred at {box.bounds.center}. Needs {(requireWholeParty ? "the player + every companion" : "just the player")} inside it to seal. Walk in and watch for 'entered the box' lines.", this);
    }

    void Update()
    {
        if (hasSealed) return;

        if (player == null)
        {
            AcquirePlayer();
            if (player == null)
            {
                // Log this once rather than staying silent — a mistyped Player Tag
                // means the trigger can NEVER seal, and nothing else would hint at why.
                if (logMembership && !warnedNoPlayer)
                {
                    Debug.LogWarning($"[RoomSealTrigger] '{name}' can't find any object tagged '{playerTag}', so it will never seal. Check the tag on your player object matches the Player Tag field.", this);
                    warnedNoPlayer = true;
                }
                return;
            }
        }

        Bounds bounds = box.bounds;

        if (logMembership) TrackMembership(bounds);

        // Player has to be in first — a companion wandering in alone shouldn't start
        // the fight.
        if (!bounds.Contains(player.position)) return;

        if (requireWholeParty && !WholePartyInside(bounds)) return;

        Seal();
    }

    // Logs the player and each companion crossing into or out of the box, but only on
    // the frame it changes. This reads position exactly the way the seal check does, so
    // what it prints is precisely what's keeping the room from sealing — if a companion
    // never shows an "entered" line, that's the one stuck outside.
    private void TrackMembership(Bounds bounds)
    {
        bool playerInside = bounds.Contains(player.position);
        if (playerInside != prevPlayerInside)
        {
            prevPlayerInside = playerInside;
            if (playerInside)
                Debug.Log($"[RoomSealTrigger] '{name}' — PLAYER entered the box. {InsideSummary(bounds)}", this);
            else
                Debug.Log($"[RoomSealTrigger] '{name}' — PLAYER left the box.", this);
        }

        insideNow.Clear();
        IReadOnlyList<Comapnion> party = Comapnion.Active;
        for (int i = 0; i < party.Count; i++)
        {
            Comapnion member = party[i];
            if (member == null) continue;
            if (bounds.Contains(member.transform.position)) insideNow.Add(member);
        }

        foreach (Comapnion member in insideNow)
            if (!insidePrev.Contains(member))
                Debug.Log($"[RoomSealTrigger] '{name}' — companion '{member.name}' entered the box. {InsideSummary(bounds)}", this);

        foreach (Comapnion member in insidePrev)
            if (member != null && !insideNow.Contains(member))
                Debug.Log($"[RoomSealTrigger] '{name}' — companion '{member.name}' left the box.", this);

        insidePrev.Clear();
        foreach (Comapnion member in insideNow) insidePrev.Add(member);
    }

    // "2/3 party inside (needs all to seal)" — the running tally shown on each crossing.
    private string InsideSummary(Bounds bounds)
    {
        IReadOnlyList<Comapnion> party = Comapnion.Active;
        int total = 1 + party.Count;
        int inside = bounds.Contains(player.position) ? 1 : 0;
        for (int i = 0; i < party.Count; i++)
            if (party[i] != null && bounds.Contains(party[i].transform.position)) inside++;

        return $"{inside}/{total} inside" + (requireWholeParty ? " (needs all to seal)" : " (seals on player alone)");
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
