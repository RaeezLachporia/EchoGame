using UnityEngine;
using UnityEngine.AI;

// Autonomous fighting. Left alone in a fight, a companion with this holds a
// position near the player, hits what's hitting them, throws herself in front of
// incoming swings, and pulls back when she's nearly down.
//
// Deliberately a plain MonoBehaviour and NOT a CompanionAbility: an ability's
// whole contract is a command-wheel verb, component order IS the wheel layout, so
// a brain would silently claim a slice that looks pressable and does nothing. It
// would also deadlock — AttackAbility.IsBusy is true *because of* the brain's own
// command, so a brain that yielded to busy abilities would yield to itself.
//
// AGENT OWNERSHIP (this adds one rung to the existing chain):
//   0. player-issued CompanionCommand   (command.HasPlayerCommand)
//   1. any other CompanionAbility.IsBusy (Taunt / Heal / DoubleTap)
//   2. CompanionCombatBrain.OwnsAgent    <- here
//   3. BasicPlayerFollowScript.IsFollowing
//   4. ComapnionBehaviour wander
// The rule that keeps this working: whoever calls SetDestination this frame also
// writes the animator's Speed and animator.speed this frame, and nobody else does.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CompanionCommand))]
[RequireComponent(typeof(CompanionThreatSensor))]
[DisallowMultipleComponent]
// Runs BEFORE CompanionAbility subclasses (default 0) so the brain's hard
// guards (player command / ability busy) get to cancel any brain-owned
// CompanionCommand this frame before an ability's Update reads the command's
// state. Without this the update order is undefined and the "who cancels
// whom" race resolves differently frame to frame.
[DefaultExecutionOrder(-100)]
public class CompanionCombatBrain : MonoBehaviour
{
    public enum BrainState
    {
        Idle,          // follow/wander own the agent
        Anchor,        // holding the frontline
        Attack,        // a brain-issued CompanionCommand is running
        Intercept,     // moving onto the block line
        SelfPreserve   // pulling to the edge of the fight
    }

    [Header("Tuning")]
    [Tooltip("Overrides the profile on this companion's CompanionDefinition. Handy for testing one companion without editing the shared asset.")]
    [SerializeField] private CombatProfile profileOverride;

    [Header("Character Extension")]
    [Tooltip("Optional per-character brain layer. Called BEFORE the vanilla Evaluate and its decision wins outright — the profile is the foundation of the role, the extension is what makes the character themselves. Leave null for the default brain.")]
    [SerializeField] private CompanionBrainExtension extension;

    [Header("References")]
    [Tooltip("Found by tag at Start if left empty.")]
    [SerializeField] private Transform player;
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;

    [Header("Debug")]
    [Tooltip("Read-only mirror of the current state so it shows live in the inspector. Changing it by hand does nothing.")]
    [SerializeField] private BrainState stateDisplay = BrainState.Idle;
    [Tooltip("Log every state transition with a reason.")]
    [SerializeField] private bool logStateChanges = false;

    private NavMeshAgent agent;
    private CompanionCommand command;
    private CompanionThreatSensor sensor;
    private BasicPlayerFollowScript follow;
    private Comapnion self;
    private CombatProfile profile;

    // AttackAbility mirrors command.HasActiveCommand, which is true because of our
    // own command — treating it as "an ability is busy" would deadlock the brain
    // against itself the instant it threw its first punch. Held separately so the
    // busy check can skip exactly this one.
    private AttackAbility attackAbility;
    private CompanionAbility[] abilities;

    private BrainState state = BrainState.Idle;
    private BrainState pendingState = BrainState.Idle;
    private float pendingSince;
    private float tickTimer;
    private float scanInterval;

    // The fight is "on" once anything nearby actually reaches Combat. She never
    // starts one — this is what keeps her from punching a guard the player was
    // sneaking past.
    private bool combatLatched;
    private float lastCombatSignalAt;
    private float reEngageBlockedUntil;

    private Transform attackTarget;
    private Vector3 destination;
    // The unresolved point the destination came from. Compared against instead of
    // `destination`, because resolving snaps to the navmesh — comparing a raw
    // candidate to a snapped result would read as "moved" every single frame and
    // re-run CalculatePath forever.
    private Vector3 lastCandidate;
    private bool hasDestination;
    private Vector3 facingTarget;
    private bool hasFacingTarget;
    private NavMeshPath pathBuffer;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // Follow and wander check this to stand down. Deliberately covers every
    // non-Idle state, including Attack where CompanionCommand is also holding the
    // agent — the redundancy is free and survives someone refactoring either side.
    public bool OwnsAgent => state != BrainState.Idle;
    public BrainState State => state;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        command = GetComponent<CompanionCommand>();
        sensor = GetComponent<CompanionThreatSensor>();
        follow = GetComponent<BasicPlayerFollowScript>();
        self = GetComponent<Comapnion>();
        attackAbility = GetComponent<AttackAbility>();
        abilities = GetComponents<CompanionAbility>();
        if (animator == null) animator = GetComponent<Animator>();
        pathBuffer = new NavMeshPath();
    }

    void Start()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
        }

        profile = profileOverride != null ? profileOverride
                : (self != null && self.Definition != null ? self.Definition.combatProfile : null);

        if (profile == null)
        {
            Debug.LogWarning($"[CompanionCombatBrain] No CombatProfile on '{name}' — assign one to the CompanionDefinition, or to Profile Override. Disabling autonomous combat for this companion.", this);
            enabled = false;
            return;
        }

        if (player == null)
        {
            Debug.LogWarning($"[CompanionCombatBrain] No GameObject tagged 'Player' found for '{name}'. Disabling autonomous combat.", this);
            enabled = false;
            return;
        }

        sensor.Initialize(self, profile, player);

        // Jitter the tick so several companions never scan on the same frame.
        // Same trick BasicPlayerFollowScript uses for its speed variance.
        scanInterval = profile.scanInterval * Random.Range(0.8f, 1.2f);
        tickTimer = Random.Range(0f, scanInterval);
    }

    void OnDisable()
    {
        // Never leave the agent held by a brain that's no longer running.
        if (state != BrainState.Idle) ExitToIdle("disabled");
    }

    void Update()
    {
        if (profile == null || player == null) return;

        // ---- 1. Hard guards. Every frame, never on the tick: a 0.15s tick is
        // longer than a short hop, and slower than a player expects a command to
        // land. Anything above us in the ownership chain wins immediately.
        if (command.HasPlayerCommand) { ExitToIdle("player command"); return; }
        if (AnyOtherAbilityBusy()) { ExitToIdle("ability busy"); return; }
        if (follow != null && follow.IsJumping) { ExitToIdle("jumping"); return; }

        // ---- 2. Self-preservation is reflex, not a decision. A companion who
        // hesitates before saving herself reads as broken.
        if (state != BrainState.SelfPreserve && combatLatched && HealthFraction <= profile.selfPreserveEnter)
            Enter(BrainState.SelfPreserve, "health critical");

        // ---- 3. Decisions on the tick.
        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = scanInterval;
            sensor.Scan(OwnsAgent ? profile.disengageRadius : profile.senseRadius);
            UpdateCombatLatch();
            Decide();
        }

        // ---- 4. Actuation. Every frame.
        Drive();
    }

    // Split radii: she notices a fight inside senseRadius but doesn't let go of one
    // until it leaves the larger disengageRadius. Without the gap, a threat parked
    // exactly on the boundary toggles her in and out every single tick.
    private void UpdateCombatLatch()
    {
        if (sensor.AnyEngaged)
        {
            combatLatched = true;
            lastCombatSignalAt = Time.time;
            return;
        }

        if (!combatLatched) return;

        // Still enemies around, just none swinging yet — an Alert straggler walking
        // in is still the same fight. Keep the latch warm.
        if (sensor.Count > 0) lastCombatSignalAt = Time.time;
        else if (Time.time - lastCombatSignalAt >= profile.disengageLinger) combatLatched = false;
    }

    private void Decide()
    {
        BrainState want = Evaluate();

        if (want == state)
        {
            pendingState = state;
            return;
        }

        // Instant in both directions. Delaying self-preservation looks broken;
        // delaying the hand-back is invisible to the player anyway.
        if (want == BrainState.SelfPreserve || want == BrainState.Idle)
        {
            Enter(want, want == BrainState.Idle ? "no threats" : "health critical");
            return;
        }

        // Everything else waits out the reaction delay, so she doesn't snap to
        // every twitch on frame zero. Because Decide only runs on the tick, the
        // real lag is one to two ticks — and that lag is the whole point.
        if (want != pendingState)
        {
            pendingState = want;
            pendingSince = Time.time;
            return;
        }

        if (Time.time - pendingSince >= profile.reactionDelay) Enter(want, "reacted");
    }

    private BrainState Evaluate()
    {
        // She never opens a fight. Until something is genuinely in Combat, she's
        // just a companion walking around.
        if (!combatLatched) return BrainState.Idle;
        if (state == BrainState.Idle && Time.time < reEngageBlockedUntil) return BrainState.Idle;

        // 1. SELF_PRESERVE — with hysteresis, so chip damage across the line
        // doesn't flicker her in and out of the pull-back every swing.
        if (HealthFraction <= profile.selfPreserveEnter) return BrainState.SelfPreserve;
        if (state == BrainState.SelfPreserve && HealthFraction < profile.selfPreserveExit)
            return BrainState.SelfPreserve;

        // 1.5. CHARACTER EXTENSION — sits between the safety net (SelfPreserve
        // must always win, a dead healer helps no one) and the vanilla decision
        // stack (Intercept/Attack/Anchor). When the extension has an opinion it
        // wins outright: the profile is the FOUNDATION of the role, the
        // extension is what makes the character themselves, and the character
        // gets the final say — otherwise it'd just be advisory.
        if (extension != null)
        {
            BrainContext ctx = new BrainContext(this, transform, player, profile, sensor,
                                                HealthFraction, combatLatched, state);
            if (extension.TryEvaluate(in ctx, out BrainState extState, out Transform extTarget))
            {
                if (extState == BrainState.Attack && extTarget != null) attackTarget = extTarget;
                return extState;
            }
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // 2. INTERCEPT — get in the way of something about to land a hit on
        // someone she's covering.
        if (profile.interceptEnabled && sensor.HasImminent)
        {
            CompanionThreatSensor.Threat threat = sensor.MostImminent;
            if (threat.distanceToVictim <= profile.interceptTriggerRange
                && threat.transform != null
                && distanceToPlayer <= profile.leashRadius)
                return BrainState.Intercept;
        }

        // 3. ANCHOR + ATTACK — punch the priority target, but only one the player
        // is actually near, and only while she's inside her own leash.
        if (sensor.HasPrimary && sensor.Primary.transform != null)
        {
            CompanionThreatSensor.Threat primary = sensor.Primary;
            bool engageable = primary.enemy != null
                           && primary.enemy.State != EnemyFollowPlayer.EnemyState.Patrol;
            float targetToPlayer = Vector3.Distance(primary.transform.position, player.position);
            float allowedRange = state == BrainState.Attack
                ? profile.engageRadius + profile.targetLeashSlack
                : profile.engageRadius;
            float allowedLeash = state == BrainState.Attack ? profile.leashRadius : profile.leashResumeRadius;

            if (engageable && targetToPlayer <= allowedRange && distanceToPlayer <= allowedLeash)
            {
                attackTarget = primary.transform;
                return BrainState.Attack;
            }
        }

        // 4. Threats exist but none is worth closing on — hold the line rather
        // than running at the nearest thing. This is what makes her a wall.
        if (sensor.Count > 0) return BrainState.Anchor;

        return BrainState.Idle;
    }

    private void Enter(BrainState next, string reason)
    {
        if (next == state) return;

        if (next == BrainState.Idle)
        {
            ExitToIdle(reason);
            return;
        }

        // Leaving Attack for anything else means letting go of our own command
        // first — but never one the player issued.
        if (state == BrainState.Attack) command.CancelIfOwnedBy(CommandOwner.Brain);

        BrainState previous = state;
        state = next;
        stateDisplay = next;
        pendingState = next;
        hasDestination = false;
        // Cleared so Anchor faces where she's walking instead of inheriting
        // whatever Intercept or SelfPreserve was staring at.
        hasFacingTarget = false;

        if (logStateChanges)
            Debug.Log($"[CompanionCombatBrain] {name}: {previous} -> {next} ({reason})", this);
    }

    private void ExitToIdle(string reason)
    {
        if (state == BrainState.Idle) return;

        command.CancelIfOwnedBy(CommandOwner.Brain);
        if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
        agent.velocity = Vector3.zero;

        // Follow only restores animator.speed once it's nearly stopped, so handing
        // back mid-stride would leave a scaled playback rate behind for its first
        // few frames. Put it back ourselves.
        if (animator != null) animator.speed = 1f;

        BrainState previous = state;
        state = BrainState.Idle;
        stateDisplay = BrainState.Idle;
        pendingState = BrainState.Idle;
        attackTarget = null;
        hasDestination = false;
        hasFacingTarget = false;
        reEngageBlockedUntil = Time.time + profile.reEngageCooldown;

        if (logStateChanges)
            Debug.Log($"[CompanionCombatBrain] {name}: {previous} -> Idle ({reason})", this);
    }

    private void Drive()
    {
        switch (state)
        {
            case BrainState.Idle:
                return;

            case BrainState.Attack:
                DriveAttack();
                return;

            case BrainState.Anchor:
                SetDestinationOnce(CombatPositioning.Anchor(
                    player.position, player.forward, sensor.EngagedCentroid, sensor.AnyEngaged, profile));
                break;

            case BrainState.Intercept:
                DriveIntercept();
                break;

            case BrainState.SelfPreserve:
                SetDestinationOnce(CombatPositioning.SelfPreserveEdge(
                    transform.position, player.position, sensor.EngagedCentroid, profile));
                // Back off facing the fight, not facing away from it.
                facingTarget = sensor.EngagedCentroid;
                hasFacingTarget = true;
                break;
        }

        agent.speed = profile.moveSpeed;
        if (hasDestination && agent.isOnNavMesh) agent.SetDestination(destination);
        UpdateRotation();
        UpdateAnimation();
    }

    private void DriveAttack()
    {
        // The target died or was pooled — Evaluate will move us on next tick;
        // don't re-issue a command at a corpse in the meantime.
        if (attackTarget == null || !attackTarget.gameObject.activeInHierarchy) return;

        // Re-issue between swings rather than waiting for the next tick, so there's
        // no dead frame where nobody owns the agent. attacksPerCommand is 1 on
        // autonomous companions, so this fires once per swing — which is exactly
        // where the leash and priority get re-checked.
        if (!command.HasActiveCommand && !command.IsAttacking)
            command.CommandAttack(attackTarget, CommandOwner.Brain);

        // CompanionCommand owns steering, facing and the animator while its command
        // is live. Writing any of them here would fight it, last-writer-wins, every
        // frame. Only cover the gap when it has nothing running.
        if (!command.HasActiveCommand)
        {
            UpdateRotation();
            UpdateAnimation();
        }
    }

    private void DriveIntercept()
    {
        if (!sensor.HasImminent || sensor.MostImminent.transform == null || sensor.MostImminent.victim == null)
            return;

        CompanionThreatSensor.Threat threat = sensor.MostImminent;
        EnemyCombat enemyCombat = threat.enemy != null ? threat.enemy.GetComponent<EnemyCombat>() : null;
        float reach = enemyCombat != null
            ? enemyCombat.AttackRange + enemyCombat.HitForwardOffset
            : profile.fallbackEnemyReach;

        if (CombatPositioning.TryIntercept(threat.transform.position, threat.victim.position, reach, profile, out Vector3 point))
            SetDestinationOnce(point);

        // Face the thing she's blocking, so the body-block reads as facing it down.
        facingTarget = threat.transform.position;
        hasFacingTarget = true;
    }

    // Resolving a point runs CalculatePath, which isn't free — only redo it when
    // the target has actually moved somewhere meaningfully different.
    private void SetDestinationOnce(Vector3 candidate)
    {
        if (hasDestination && (candidate - lastCandidate).sqrMagnitude < 0.0625f) return; // 0.25m
        lastCandidate = candidate;

        if (CombatPositioning.TryResolve(agent, candidate, player.position, pathBuffer, 2f, out Vector3 resolved))
        {
            destination = resolved;
            hasDestination = true;
        }
        else if (!hasDestination)
        {
            // Nothing reachable at all — stand on the player's position rather than
            // freezing with follow already stood down.
            destination = player.position;
            hasDestination = true;
        }
    }

    private void UpdateRotation()
    {
        Vector3 direction;
        if (hasFacingTarget)
        {
            direction = facingTarget - transform.position;
        }
        else
        {
            direction = agent.velocity;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion look = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, profile.rotationSpeed * Time.deltaTime);
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;

        float speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);
        animator.speed = speed > 0.1f ? speed / Mathf.Max(0.01f, profile.moveAnimSpeed) : 1f;
    }

    private bool AnyOtherAbilityBusy()
    {
        for (int i = 0; i < abilities.Length; i++)
        {
            CompanionAbility ability = abilities[i];
            if (ability == null) continue;
            // See the field comment: skipping AttackAbility is what stops the brain
            // standing down because of its own punch.
            if (ReferenceEquals(ability, attackAbility)) continue;
            if (ability.IsBusy) return true;
        }
        return false;
    }

    private float HealthFraction
    {
        get
        {
            if (self == null || self.MaxHealth <= 0f) return 1f;
            return self.CurrentHealth / self.MaxHealth;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || profile == null || player == null) return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(player.position, profile.engageRadius);
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(player.position, profile.leashRadius);

        if (hasDestination)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(destination + Vector3.up * 0.1f, new Vector3(0.4f, 0.1f, 0.4f));
            Gizmos.DrawLine(transform.position, destination);
        }
    }
}
