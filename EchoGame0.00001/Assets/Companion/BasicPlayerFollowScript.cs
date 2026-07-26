using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class BasicPlayerFollowScript : MonoBehaviour
{
    [Header("Follow Settings")]
    public float followDistance = 2.5f;
    [Tooltip("Must exceed this distance before the companion starts moving again after stopping")]
    public float resumeDistance = 3.5f;
    public float runDistance = 6f;
    public float sprintDistance = 12f;
    public float walkSpeed = 2f;
    public float runSpeed = 6f;
    public float sprintSpeed = 10f;
    public float rotationSpeed = 10f;

    [Header("Jump Settings")]
    [Tooltip("Base time (seconds) to cross a link. Horizontal distance adds more on top (jumpTimePerMeter) so short steps hop quickly and long leaps aren't rushed.")]
    public float jumpBaseDuration = 0.4f;
    [Tooltip("Seconds added per metre of horizontal link distance.")]
    public float jumpTimePerMeter = 0.06f;
    [Tooltip("Arc peak height above the higher end of the link. Kept small so short steps read as hops; wide gaps arc higher via jumpArcPerMeter. Also floored so up-jumps always clear the lip.")]
    public float jumpArcHeight = 0.5f;
    [Tooltip("Extra arc height per metre of horizontal distance.")]
    public float jumpArcPerMeter = 0.12f;
    [Tooltip("A downward link deeper than this plays the fall pose during the descent; shallower drops read as a quick hop. Keep below hardLandDropHeight so a big drop still shows the fall before the crouch landing.")]
    public float fallStartDrop = 1.2f;
    [Tooltip("A downward link deeper than this plays the hard-landing (crouch) animation on touchdown; shallower drops use the soft fall.")]
    public float hardLandDropHeight = 2f;
    [Tooltip("Seconds the companion is frozen in place after a hard landing while the crouch recovery plays. Set near the hard-landing clip length.")]
    public float hardLandLockTime = 1.2f;
    [Tooltip("Seconds the companion is frozen after a soft-fall landing so it can't slide around while the landing recovery plays. Set near the landing portion of the fall clip that plays after touchdown. Hard landings use hardLandLockTime instead.")]
    public float fallLockTime = 0.5f;

    [Header("Animation")]
    public Animator animator;
    public float animationDampTime = 0.1f;
    [Tooltip("Speed the walk animation was authored for — tweak until footsteps match")]
    public float walkAnimSpeed = 2f;
    [Tooltip("Speed the run animation was authored for — tweak until footsteps match")]
    public float runAnimSpeed = 6f;
    [Tooltip("Speed the sprint animation was authored for — tweak until footsteps match")]
    public float sprintAnimSpeed = 10f;

    [Header("Teleport Fallback")]
    public bool teleportEnabled = true;
    [Tooltip("Teleport to the player if the path is blocked and they are this far away")]
    public float teleportDistance = 18f;
    [Tooltip("How far behind the player to land when teleporting")]
    public float teleportOffset = 2f;

    [Header("Formation & Stagger")]
    [Tooltip("0..3 — each slot is a different angle/distance behind the player. Set 0 on companion A, 1 on B, etc. so they don't all target the same point.")]
    [SerializeField] private int formationSlot = 0;
    [Tooltip("How far from the player the formation slot sits.")]
    [SerializeField] private float formationRadius = 1.5f;
    [Tooltip("Per-companion speed jitter. 0.15 = each companion's speed varies ±15% from the configured value, so multiple companions desync naturally.")]
    [SerializeField, Range(0f, 0.5f)] private float speedVariance = 0.15f;

    [Header("References")]
    public Transform player;

    [Header("Debug")]
    [SerializeField] private float currentSpeed;

    private NavMeshAgent agent;
    private InputManager playerInput;
    private CompanionCommand command;
    private CompanionAbility[] abilities;
    private bool isFollowing = false;
    private bool isJumping = false;

    public bool IsFollowing => isFollowing;
    public NavMeshAgent Agent => agent;

    // Local-space offsets behind the player for slots 0..3 (x = right, z = forward).
    // Negative z = behind the player. Two close-behind slots and two wider flanking slots.
    private static readonly Vector3[] SlotDirections =
    {
        new Vector3(-0.5f, 0f, -1f), // behind-left
        new Vector3( 0.5f, 0f, -1f), // behind-right
        new Vector3(-1f,   0f, -0.4f), // left flank
        new Vector3( 1f,   0f, -0.4f), // right flank
    };
    private float speedMultiplier = 1f;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int JumpHash = Animator.StringToHash("IsJumping");
    private static readonly int FallHash = Animator.StringToHash("Fall");
    private static readonly int HardLandHash = Animator.StringToHash("HardLand");

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = false;
        command = GetComponent<CompanionCommand>();
        abilities = GetComponents<CompanionAbility>();

        if (animator == null)
            animator = GetComponent<Animator>();

        // Lock in a per-instance speed offset once so this companion always moves
        // slightly faster or slower than its siblings — they drift in and out of
        // sync instead of marching in lockstep.
        speedMultiplier = 1f + Random.Range(-speedVariance, speedVariance);
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                playerInput = playerObj.GetComponent<InputManager>();
            }
            else
            {
                Debug.LogWarning("BasicPlayerFollowScript: No GameObject tagged 'Player' found.");
            }
        }
        else
        {
            playerInput = player.GetComponent<InputManager>();
        }
    }

    private void Update()
    {
        if (player == null) return;

        if (isJumping) return;

        // CompanionCommand owns the agent while a player-issued attack command is
        // active. Bailing here (and clearing isFollowing) stops us from calling
        // SetDestination toward the player every frame and yanking the companion
        // away from the target.
        if (command != null && command.HasActiveCommand)
        {
            isFollowing = false;
            return;
        }

        // If an ability is running (like Naledi off healing someone), don't follow —
        // the ability is steering the companion right now.
        if (AnyAbilityBusy())
        {
            isFollowing = false;
            return;
        }

        if (agent.isOnOffMeshLink)
        {
            StartCoroutine(JumpAcrossLink());
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        bool wasFollowing = isFollowing;
        if (isFollowing && distanceToPlayer <= followDistance)
            isFollowing = false;
        else if (!isFollowing && distanceToPlayer > resumeDistance)
            isFollowing = true;

        if (!isFollowing)
        {
            // Clear our follow path exactly once on the transition so an idle/wander
            // script (ComapnionBehaviour) can SetDestination without us wiping it
            // every frame. Animation/rotation still tick from real agent.velocity
            // below so wander movement animates and faces the right way.
            if (wasFollowing)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            currentSpeed = agent.velocity.magnitude;
            UpdateAnimation(currentSpeed);
            RotateTowardMovementDirection();
            return;
        }

        if (teleportEnabled && distanceToPlayer > teleportDistance && PathIsBlocked())
        {
            TeleportToPlayer();
            return;
        }

        bool playerIsSprinting = playerInput != null && playerInput.isSprinting;
        bool tooFarBehind = distanceToPlayer > sprintDistance;

        if (playerIsSprinting || tooFarBehind)
            agent.speed = sprintSpeed * speedMultiplier;
        else if (distanceToPlayer > runDistance)
            agent.speed = runSpeed * speedMultiplier;
        else
            agent.speed = walkSpeed * speedMultiplier;
        agent.SetDestination(GetFollowTarget());

        currentSpeed = agent.velocity.magnitude;
        UpdateAnimation(currentSpeed);
        RotateTowardMovementDirection();
    }

    private bool AnyAbilityBusy()
    {
        for (int i = 0; i < abilities.Length; i++)
            if (abilities[i] != null && abilities[i].IsBusy) return true;
        return false;
    }

    private void UpdateAnimation(float speed)
    {
        if (animator == null) return;

        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);

        if (speed < 0.1f)
        {
            animator.speed = 1f;
            return;
        }

        // Scale playback rate so footsteps stay locked to ground movement
        float designedSpeed;
        if (agent.speed >= sprintSpeed)
            designedSpeed = sprintAnimSpeed;
        else if (agent.speed >= runSpeed)
            designedSpeed = runAnimSpeed;
        else
            designedSpeed = walkAnimSpeed;

        animator.speed = speed / designedSpeed;
    }

    private IEnumerator JumpAcrossLink()
    {
        isJumping = true;

        OffMeshLinkData link = agent.currentOffMeshLinkData;
        Vector3 start = transform.position;
        // A bidirectional NavMeshLink does not reliably swap startPos/endPos to match
        // the direction we cross it, so link.endPos can be the side we're already
        // standing on (this is what breaks DOWNward jumps: end == start, drop == 0, so
        // we hop in place instead of falling). Pick whichever endpoint is farther from
        // us as the real destination — correct going up or down.
        Vector3 destination = Vector3.Distance(start, link.startPos) > Vector3.Distance(start, link.endPos)
            ? link.startPos
            : link.endPos;
        // That endpoint sits on the navmesh surface, but the agent normally rests
        // baseOffset above it (transform.position already includes that offset). Add it
        // back or the companion lands baseOffset units into the floor and pops out when
        // CompleteOffMeshLink snaps it up.
        Vector3 end = destination + Vector3.up * agent.baseOffset;

        // Size the hop to the actual link: a short step gets a small, quick hop
        // while a wide gap gets a longer, higher arc. Horizontal distance drives
        // both, so a 0.5 m step doesn't get the same floaty leap as a 4 m jump.
        Vector3 flatDelta = new Vector3(end.x - start.x, 0f, end.z - start.z);
        float horizontalDistance = flatDelta.magnitude;
        float rise = end.y - start.y;

        // How far we drop (0 when going up or level). Drives the fall/hard-land
        // choice and stretches a tall drop out so the fall actually reads.
        float drop = Mathf.Max(0f, -rise);

        // TEMP debug: log the real drop per link so we can confirm it matches the
        // visual ledge height — i.e. that a "low" ledge isn't computing a big drop
        // and mis-firing the fall/crouch. Remove once the tuning is dialled in.
        Debug.Log($"[Companion Jump] {name}: start.y={start.y:F2} end.y={end.y:F2} rise={rise:F2} drop={drop:F2} horiz={horizontalDistance:F2} | linkStart.y={link.startPos.y:F2} linkEnd.y={link.endPos.y:F2} -> {(drop > fallStartDrop ? "FALL" : "HOP")}, hardLand={drop > hardLandDropHeight}", this);

        float duration = jumpBaseDuration + jumpTimePerMeter * (horizontalDistance + drop);

        float arc;
        if (drop > 0.05f)
            // Dropping off a ledge — only a small step-off bump, then let the lerp
            // carry us down. No big loft up before falling.
            arc = jumpArcHeight * 0.4f + jumpArcPerMeter * horizontalDistance;
        else
            // Hopping up or across — floor the arc above rise*0.5 so we clear the lip.
            arc = Mathf.Max(jumpArcHeight + jumpArcPerMeter * horizontalDistance,
                            rise * 0.5f + 0.15f);

        if (animator != null)
        {
            animator.speed = 1f; // play the clip at its authored rate, not the scaled run rate
            // Fall pose for a real drop (deeper than fallStartDrop); a quick hop for
            // small step-downs or going up/level. The crouch hard-landing is punched
            // in on touchdown below for deep drops.
            animator.SetTrigger(drop > fallStartDrop ? FallHash : JumpHash);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            Vector3 pos = Vector3.Lerp(start, end, t);
            pos.y += arc * Mathf.Sin(t * Mathf.PI);
            transform.position = pos;

            // face the direction of travel during the jump
            if (flatDelta.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatDelta), rotationSpeed * Time.deltaTime);

            yield return null;
        }

        transform.position = end;

        // Big drop: punch the crouch landing right as we touch down (the fall
        // pose played during the descent above).
        bool hardLanding = drop > hardLandDropHeight;
        if (hardLanding && animator != null)
            animator.SetTrigger(HardLandHash);

        agent.CompleteOffMeshLink();

        // Freeze through the landing recovery so the companion can't slide around
        // while the fall/crouch clip finishes, then hand control back. A hard landing
        // holds for the longer crouch recovery; a soft fall holds for its shorter
        // landing; a plain hop has no recovery to wait on. isJumping stays true for the
        // whole wait, so Update() keeps bailing and won't resume following meanwhile.
        float landingLock = hardLanding ? hardLandLockTime
                          : drop > fallStartDrop ? fallLockTime
                          : 0f;
        if (landingLock > 0f)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            yield return new WaitForSeconds(landingLock);
            agent.isStopped = false;
        }

        isJumping = false;
    }

    private Vector3 GetFollowTarget()
    {
        // Pick the slot offset, rotate it into the player's facing so "behind-left"
        // stays behind-left as the player turns, and place it relative to the player.
        int slot = Mathf.Clamp(formationSlot, 0, SlotDirections.Length - 1);
        Vector3 localOffset = SlotDirections[slot].normalized * formationRadius;
        Quaternion playerYaw = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
        return player.position + playerYaw * localOffset;
    }

    private bool PathIsBlocked()
    {
        return agent.pathStatus == NavMeshPathStatus.PathPartial
            || agent.pathStatus == NavMeshPathStatus.PathInvalid;
    }

    private void TeleportToPlayer()
    {
        Vector3 behindPlayer = player.position - player.forward * teleportOffset;
        if (NavMesh.SamplePosition(behindPlayer, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    private void RotateTowardMovementDirection()
    {
        if (agent.velocity.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(agent.velocity.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}
