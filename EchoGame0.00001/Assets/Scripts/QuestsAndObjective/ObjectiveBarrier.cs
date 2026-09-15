using UnityEngine;

// A door that stays shut until an objective is done. Put this on the empty GameObject
// whose collider blocks a doorway, and type the id of the objective that should open
// it into Open On Objective Id.
//
// The whole "which barrier opens when?" problem is solved by not having a manager
// decide. Each barrier only knows the one objective it's waiting on and listens to the
// QuestTracker — the same tracker QuestHud listens to. Finish "clear-room-1" and every
// barrier waiting on "clear-room-1" opens; nothing else reacts. Add a tenth door by
// dropping this on a wall and typing an id, and no other script changes.
//
// While it's shut and the player walks up to it, it shows a line on the BlockedMessageHud
// telling them what to do. No separate trigger volume needed — it checks the distance to
// the player itself, the way Interactable does.
public class ObjectiveBarrier : MonoBehaviour
{
    [Header("Objective")]
    [Tooltip("Id of the objective that opens this barrier, e.g. \"clear-room-1\". Matches the Id on an ObjectiveDefinition asset. When that objective completes, this opens.")]
    [SerializeField] private string openOnObjectiveId;
    [Tooltip("The tracker whose objectives this watches. Auto-found in the scene if left empty — only set it by hand if there's more than one.")]
    [SerializeField] private QuestTracker tracker;

    [Header("Barrier")]
    [Tooltip("The solid colliders that block the way. LEAVE EMPTY to auto-grab every non-trigger collider on this object and its children. Fill it in by hand only to open some colliders but not others.")]
    [SerializeField] private Collider[] blockingColliders;
    [Tooltip("Optional. The wall mesh or effect to hide when the barrier opens. Leave empty if the collider is invisible anyway.")]
    [SerializeField] private GameObject wallVisual;
    [Tooltip("Tick for an ENTRANCE that lets the party walk in and then seals behind them (a RoomSealTrigger calls Close() once everyone's inside). Untick for an EXIT that starts shut and opens when its objective is done. Either way it opens for good when Open On Objective Id completes.")]
    [SerializeField] private bool startOpen;

    [Header("Message")]
    [Tooltip("The banner this shows while shut and the player is near. Auto-found in the scene if left empty.")]
    [SerializeField] private BlockedMessageHud messageHud;
    [Tooltip("What the banner says while this door is shut, e.g. \"Defeat the enemies to open the way\".")]
    [TextArea] [SerializeField] private string blockedMessage = "Complete the current objective to proceed.";
    [Tooltip("How close the player has to get before the message shows, in metres.")]
    [SerializeField, Min(0f)] private float messageRange = 4f;
    [Tooltip("Tag on the player object, used for the distance check.")]
    [SerializeField] private string playerTag = "Player";

    [Header("Debug")]
    [Tooltip("Log when this barrier opens and why. Handy for confirming an objective id actually reaches the right door.")]
    [SerializeField] private bool logBarrier = true;

    private bool isOpen;
    // Set once the gating objective completes. From then on the door is open for good,
    // so a late RoomSealTrigger can't trap the player back in a room they've cleared.
    private bool openedByObjective;
    private Transform player;

    void Awake()
    {
        if (tracker == null) tracker = FindObjectOfType<QuestTracker>();
        if (messageHud == null) messageHud = FindObjectOfType<BlockedMessageHud>();

        // Auto-grab the walls if none were assigned. Only the solid ones — a trigger
        // collider on the same object (a future use) shouldn't be switched off as if
        // it were blocking the way.
        if (blockingColliders == null || blockingColliders.Length == 0)
        {
            Collider[] all = GetComponentsInChildren<Collider>();
            int solid = 0;
            for (int i = 0; i < all.Length; i++) if (!all[i].isTrigger) solid++;

            blockingColliders = new Collider[solid];
            int w = 0;
            for (int i = 0; i < all.Length; i++) if (!all[i].isTrigger) blockingColliders[w++] = all[i];
        }
    }

    // Subscribe in OnEnable like QuestHud does: the tracker starts its quests in Start,
    // and every OnEnable runs before any Start, so no objective can complete before this
    // barrier is listening.
    void OnEnable()
    {
        if (tracker != null) tracker.ObjectiveCompleted += OnObjectiveCompleted;
    }

    void OnDisable()
    {
        if (tracker != null) tracker.ObjectiveCompleted -= OnObjectiveCompleted;
        // Don't leave a message stuck on the banner if this barrier is switched off
        // while the player is standing at it.
        if (messageHud != null) messageHud.Hide(this);
    }

    void Start()
    {
        if (string.IsNullOrWhiteSpace(openOnObjectiveId))
        {
            if (startOpen)
                Debug.LogWarning($"[ObjectiveBarrier] '{name}' is Start Open with no Open On Objective Id, so once it seals it never reopens on its own. Fine for a one-way door — set an id if the party should be let back out when the room's cleared.", this);
            else
                Debug.LogWarning($"[ObjectiveBarrier] '{name}' has no Open On Objective Id, so nothing will ever open it — it's a permanent wall. Type the id of the objective that should open it, e.g. \"clear-room-1\".", this);
        }
        if (blockingColliders.Length == 0)
            Debug.LogWarning($"[ObjectiveBarrier] '{name}' found no solid colliders to block the way, so the player can already walk through it. Add a (non-trigger) collider to this object.", this);
        if (messageHud == null)
            Debug.LogWarning($"[ObjectiveBarrier] '{name}' found no BlockedMessageHud in the scene, so no message shows when the player hits this door. Add one to the HUD, or drag it into the Message Hud field.", this);

        AcquirePlayer();

        // Work out how to sit at load:
        //  - objective already done  -> open for good (a save past it shouldn't re-shut)
        //  - Start Open (an entrance) -> open, but seal-able, so the party can walk in
        //  - otherwise (an exit)      -> shut, so it blocks until the objective's done
        if (tracker != null && tracker.IsObjectiveComplete(openOnObjectiveId))
        {
            openedByObjective = true;
            Open("already complete at load");
        }
        else if (startOpen)
        {
            Open("starts open — entrance");
        }
        else
        {
            SetClosed();
        }
    }

    void Update()
    {
        if (isOpen) return;
        if (messageHud == null) return;

        if (player == null)
        {
            AcquirePlayer();
            if (player == null) return;
        }

        // Squared distance to skip the square root — this runs every frame on every
        // shut door.
        float sqr = (player.position - transform.position).sqrMagnitude;
        if (sqr <= messageRange * messageRange) messageHud.Show(this, blockedMessage);
        else messageHud.Hide(this);
    }

    private void OnObjectiveCompleted(QuestTracker.ObjectiveProgress objective)
    {
        if (objective.definition == null) return;
        if (objective.definition.id != openOnObjectiveId) return;

        // Lock it open even if it's an entrance that's currently open and never got
        // sealed — from here on Close() is a no-op, so nothing traps the player in a
        // room they've finished.
        openedByObjective = true;
        if (!isOpen) Open($"objective \"{openOnObjectiveId}\" completed");
    }

    // Shut the barrier. Called by RoomSealTrigger once the whole party is inside, to
    // seal an entrance for the fight. Refused once the objective's been cleared, so a
    // stray call can't re-trap the player.
    public void Close()
    {
        if (openedByObjective) return;
        if (!isOpen) return;

        SetClosed();
        if (logBarrier)
            Debug.Log($"[ObjectiveBarrier] '{name}' SEALED — party inside.", this);
    }

    private void Open(string reason)
    {
        isOpen = true;

        for (int i = 0; i < blockingColliders.Length; i++)
            if (blockingColliders[i] != null) blockingColliders[i].enabled = false;

        if (wallVisual != null) wallVisual.SetActive(false);
        if (messageHud != null) messageHud.Hide(this);

        if (logBarrier)
            Debug.Log($"[ObjectiveBarrier] '{name}' OPENED — {reason}.", this);
    }

    // Shut and blocking. Only used at load so a re-test always starts from closed,
    // even if a collider was left disabled in the scene.
    private void SetClosed()
    {
        isOpen = false;
        for (int i = 0; i < blockingColliders.Length; i++)
            if (blockingColliders[i] != null) blockingColliders[i].enabled = true;
        if (wallVisual != null) wallVisual.SetActive(true);
    }

    private void AcquirePlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) player = go.transform;
    }

    // Draw the message range so it's tunable in the scene view without pressing Play.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, messageRange);
    }
}
