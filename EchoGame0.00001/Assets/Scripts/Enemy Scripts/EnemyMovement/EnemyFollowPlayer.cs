using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyFollowPlayer : MonoBehaviour
{
    // Top-level enemy brain.
    //   Patrol — EnemyPatrolling drives the agent; we just watch for the player.
    //   Alert  — spotted the player: stalk them (chase + last-known grip) but
    //            never swing. EnemyCombat checks this state and holds its attacks.
    //            Escalates to Combat once the player stays in sight for timeToEngage.
    //   Combat — committed to the fight: chase on LOS and let EnemyCombat swing.
    //            Loses the player the same way Alert does (last-known grip, then
    //            back to Patrol).
    // Only the PLAYER triggers Patrol -> Alert. Companions are deliberately
    // invisible to this script — they roam enough that they'd trip alerts
    // constantly and drain the tension out of sneaking. How enemies respond to
    // companions is a combat-state decision, not a vision one.
    //
    // TAUNT is the one exception, and it's a targeting override rather than a
    // vision one: an already-engaged enemy can be redirected onto a companion
    // (Layla) for a while. PatrolTick still only ever looks for the player, so
    // taunting can't wake a guard who never noticed anyone — everything after
    // that first sighting goes through CurrentTarget instead.
    //
    // RETARGETING (see RetargetTick) is the general form of the same idea: once
    // in COMBAT, and only then, a companion who plants themselves closer than the
    // player becomes the thing we swing at. That's what makes a body-block read as
    // peeling instead of as a companion politely standing in the way. It stays out
    // of Patrol and Alert on purpose — those are the stealth-sensitive states, and
    // a companion who can't be seen can't blow an approach.
    public enum EnemyState { Patrol, Alert, Combat }

    [Header("Targets")]
    [SerializeField] private string playerTag = "Player";

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotationSpeed = 8f;
    [SerializeField] private float stoppingDistance = 1.5f;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 15f;
    [Tooltip("Total horizontal cone angle (degrees) used for the FRESH spot out of Patrol. Once alerted, LOS alone keeps the player — angle only gates the initial sighting, so Alert doesn't flicker every time you circle behind.")]
    [SerializeField] private float fovAngle = 110f;
    [SerializeField] private LayerMask lineOfSightObstacles;
    [SerializeField] private float eyeHeight = 1.5f;

    [Header("Alert")]
    [Tooltip("Seconds after losing line of sight before Alert gives up and drops back to Patrol. During this window the enemy walks to the last known position — not the player's live position, or it would path through walls like an aimbot.")]
    [SerializeField] private float giveUpDelay = 2f;

    [Header("Combat")]
    [Tooltip("Seconds the player must stay continuously in sight while Alert before the enemy commits to Combat and starts attacking. This is the 'spotted in the cone for this long' engage delay. A blink out of sight resets it.")]
    [SerializeField] private float timeToEngage = 1.5f;

    [Header("Combat Retargeting")]
    [Tooltip("Once fighting, a companion this close can steal our attention off the player. Never applies in Patrol or Alert — companions stay invisible until the fight has actually started.")]
    [SerializeField] private float companionNoticeRadius = 12f;
    [Tooltip("Seconds between re-picking who to fight. Slow on purpose: this is a decision, not a tracker.")]
    [SerializeField] private float retargetInterval = 0.5f;
    [Tooltip("A challenger must be this many times closer than the current target to steal it. 1 = switch to whoever is nearest and flip-flop constantly when two of them are shoulder to shoulder.")]
    [SerializeField, Min(1f)] private float switchAdvantage = 1.4f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;

    [Header("State Tracking")]
    [Tooltip("Read-only mirror of the current state so it shows live in the inspector during Play mode. Changing it by hand does nothing — State is driven from code.")]
    [SerializeField] private EnemyState stateDisplay = EnemyState.Patrol;
    [Tooltip("Log every state transition to the console with the enemy's name and timestamp.")]
    [SerializeField] private bool logStateChanges = false;

    private Transform player;
    // Taunt override (Layla's). While it holds, this enemy chases the taunter
    // instead of the player. Everything except PatrolTick goes through
    // CurrentTarget, so the redirect is total once they're already engaged.
    private Transform tauntTarget;
    private float tauntExpiresAt;
    // The Focus system — general framework for "this enemy has been marked to
    // hunt a specific ally". Callers: enemy AI types that pick priority victims
    // (a hunter that seeks the healer), story scripts that designate a target
    // for a scripted moment, abilities beyond taunt. Kept separate from taunt on
    // purpose: taunt is a player-owned override and outranks focus, focus is
    // AI/script-owned and outranks the passive combat peel.
    // A zero expiresAt means "never expires" — the permanent-until-cleared mode.
    private Transform focusTarget;
    private float focusExpiresAt;
    // Set only by RetargetTick, and only while in Combat. null means "the player",
    // which is both the default and the thing we fall back to whenever the chosen
    // companion dies, disappears, or walks off — see CurrentTarget.
    private Transform combatTarget;
    private float nextRetargetAt;
    private Vector3 lastKnownPosition;
    // Valid only while (Alert or Combat) && !PlayerInSight: the Time.time at which
    // the last-known grip gives up if sight isn't reacquired first.
    private float giveUpAt;
    // Seconds the player has been continuously in sight during Alert. Escalates to
    // Combat at timeToEngage; reset whenever sight breaks or Alert re-enters.
    private float continuousSightTime;
    private EnemyCombat combat;
    private NavMeshAgent agent;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // EnemyPatrolling yields the agent unless this is Patrol; EnemyCombat holds
    // its swings unless this is Combat; EnemyVisionCone tints off it.
    public EnemyState State { get; private set; } = EnemyState.Patrol;

    // Fired on every real transition (not on pooled respawn resets), with
    // (previous, next). Hook point for audio stingers, alert UI, and the
    // upcoming Combat state — subscribe instead of polling State in Update.
    public event System.Action<EnemyState, EnemyState> StateChanged;

    // Seconds spent in the current state. Transition rules like "in Alert for
    // 3s before escalating" read this instead of keeping their own timers.
    public float TimeInState => Time.time - stateEnteredAt;
    private float stateEnteredAt;

    // Every transition funnels through here so tracking can't be bypassed:
    // inspector mirror, entry timestamp, optional log, and the event all stay
    // in sync no matter which state initiated the change.
    private void SetState(EnemyState next)
    {
        if (next == State) return;
        EnemyState previous = State;
        State = next;
        stateDisplay = next;
        stateEnteredAt = Time.time;
        if (logStateChanges)
            Debug.Log($"[{name}] {previous} -> {next} @ {Time.time:F2}s", this);
        StateChanged?.Invoke(previous, next);
    }

    // True while Alert has live line of sight of the CURRENT target (cone: red).
    // False during the last-known grip (cone: orange). Always false in Patrol.
    // Named for the player because that's the usual target — while taunted it
    // tracks the taunter instead.
    public bool PlayerInSight { get; private set; }

    // True while a taunt is holding this enemy. Expires on its own, and drops
    // instantly if the taunter dies or is disabled — no cleanup needed elsewhere.
    public bool IsTaunted => tauntTarget != null
                          && Time.time < tauntExpiresAt
                          && tauntTarget.gameObject.activeInHierarchy;

    // True while a shove has this enemy knocked back or helpless. Fetched rather
    // than cached: EnemyStagger is added at runtime by LaylaShove and destroys
    // itself on expiry, so any cached reference would be stale in both directions.
    // Same per-call GetComponent EnemyHealth does for EnemyDebuff.
    public bool IsStaggered
    {
        get
        {
            EnemyStagger stagger = GetComponent<EnemyStagger>();
            return stagger != null && stagger.IsStaggered;
        }
    }

    // True while a focus target is live. A zero expiresAt = permanent-until-cleared
    // (story scripts); a positive one = timed (AI behaviours), matching taunt.
    public bool HasFocus => focusTarget != null
                         && (focusExpiresAt <= 0f || Time.time < focusExpiresAt)
                         && focusTarget.gameObject.activeInHierarchy;

    public Transform FocusTarget => HasFocus ? focusTarget : null;

    // Who this enemy is actually hunting. Everything past the initial Patrol
    // sighting reads this rather than `player`, so a taunt / focus / peel redirect
    // the chase, the facing, and the attack all at once.
    //
    // Priority: Taunt > Focus > combat-peel > player.
    //   Taunt is player-owned and outranks everything else.
    //   Focus is AI/script-owned and outranks the passive proximity peel.
    //   combatTarget's null-check doubles as the death handler — Comapnion
    //   destroys its GameObject on death, and Unity's fake-null reads as null
    //   here, so a killed companion silently hands us back to the player.
    public Transform CurrentTarget => IsTaunted ? tauntTarget
                                    : HasFocus ? focusTarget
                                    : (combatTarget != null ? combatTarget : player);

    // Layla's taunt. Refuses enemies that haven't noticed anyone yet: pulling a
    // patrolling guard would blow a stealth approach the player never triggered.
    // Accepting forces Combat so they commit immediately rather than re-walking
    // the Alert escalation.
    public bool TryTaunt(Transform source, float duration)
    {
        if (source == null) return false;
        if (State != EnemyState.Alert && State != EnemyState.Combat) return false;

        tauntTarget = source;
        tauntExpiresAt = Time.time + duration;
        lastKnownPosition = source.position;
        PlayerInSight = true;
        SetState(EnemyState.Combat);
        return true;
    }

    // Point this enemy at a specific victim until ClearFocusTarget is called.
    // Meant for story scripts and permanent AI designations. Callers wanting a
    // timed focus use the duration overload; the two write the same fields so a
    // timed focus can be extended into permanence with SetFocusTarget(victim).
    public void SetFocusTarget(Transform victim)
    {
        focusTarget = victim;
        focusExpiresAt = 0f;
    }

    // Timed variant. Auto-clears via HasFocus once the window passes — same
    // pattern as tauntExpiresAt, and callers don't need to schedule a cleanup.
    public void SetFocusTarget(Transform victim, float duration)
    {
        focusTarget = victim;
        focusExpiresAt = duration > 0f ? Time.time + duration : 0f;
    }

    public void ClearFocusTarget()
    {
        focusTarget = null;
        focusExpiresAt = 0f;
    }

    // EnemyVisionCone reads these to build and drive the ground-fan visual.
    public float DetectionRange => detectionRange;
    public float FovAngle => fovAngle;
    public LayerMask SightObstacles => lineOfSightObstacles;
    public float EyeHeight => eyeHeight;

    void Awake()
    {
        combat = GetComponent<EnemyCombat>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        agent = GetComponent<NavMeshAgent>();
        // We drive rotation manually — facing the velocity direction while moving
        // and the target while stopped gives smoother chase + hitbox alignment
        // than letting the agent twist toward each path waypoint.
        agent.updateRotation = false;
        agent.speed = moveSpeed;
        agent.stoppingDistance = stoppingDistance;

        // A dynamic Rigidbody fights the agent: a bump from the player imparts
        // velocity, the agent's path desyncs from its actual position, and both
        // bodies slide. Kinematic still lets OnCollision* events fire (needed
        // for the hitbox/damage to work), but leaves position fully under the
        // agent's control. Same trick the companion uses.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Auto-attach the vision cone visualizer if it isn't wired on the prefab
        // yet. Users who want to hide the cone can disable the component in the
        // inspector without touching this script.
        if (GetComponent<EnemyVisionCone>() == null) gameObject.AddComponent<EnemyVisionCone>();
    }

    // Pooled enemies keep their fields across lives. Without this, a reused enemy
    // wakes up still Alert from its previous life and stalks a ghost. Direct
    // reset, not SetState — a respawn isn't a gameplay transition, so it
    // shouldn't fire StateChanged or show up in the transition log.
    void OnEnable()
    {
        State = EnemyState.Patrol;
        stateDisplay = EnemyState.Patrol;
        stateEnteredAt = Time.time;
        PlayerInSight = false;
        continuousSightTime = 0f;
        // Pooled enemies keep fields across lives — without this a reused enemy
        // wakes up still taunted by a companion from its previous life, or still
        // fixated on the companion it was fighting when it died.
        tauntTarget = null;
        tauntExpiresAt = 0f;
        focusTarget = null;
        focusExpiresAt = 0f;
        combatTarget = null;
        nextRetargetAt = 0f;
    }

    void Start()
    {
        AcquirePlayer();
    }

    void Update()
    {
        if (player == null) AcquirePlayer();

        // Shoved: no chasing, no turning, no thinking. Checked before the NavMesh
        // guard below because EnemyStagger disables the agent outright for the
        // knockback slide, which would otherwise read as "failed to spawn".
        // EnemyCombat has the matching gate — stopping movement alone would leave
        // a staggered enemy rooted in place but still swinging at whoever's close.
        if (IsStaggered)
        {
            if (animator != null) animator.SetFloat(SpeedHash, 0f);
            return;
        }

        // If the agent failed to spawn on the NavMesh, do nothing — SetDestination
        // would just log warnings and the animation would jitter on garbage velocity.
        if (!agent.isOnNavMesh)
        {
            if (animator != null) animator.SetFloat(SpeedHash, 0f);
            return;
        }

        if (combat != null && combat.isAttacking)
        {
            // Freeze pathing/rotation during the swing so the hitbox lands where
            // committed. Dormant until the Combat state re-enables attacks.
            if (agent.hasPath) agent.ResetPath();
        }
        else
        {
            switch (State)
            {
                case EnemyState.Patrol: PatrolTick(); break;
                case EnemyState.Alert: AlertTick(); break;
                case EnemyState.Combat: CombatTick(); break;
            }
            HandleRotation();
        }

        UpdateAnimation();
    }

    // Patrol: EnemyPatrolling owns the agent. Our only job here is spotting the
    // player — range, then FOV, then the LOS raycast, cheapest check first.
    private void PatrolTick()
    {
        if (player == null) return;
        if (InRange(player) && InFov(player) && HasLineOfSight(player))
            EnterAlert();
    }

    private void EnterAlert()
    {
        SetState(EnemyState.Alert);
        PlayerInSight = true;
        lastKnownPosition = player.position;
        // Fresh alert — the engage dwell starts counting from zero this sighting.
        continuousSightTime = 0f;
        // Patrol left the agent on wander speed — bump to chase speed.
        agent.speed = moveSpeed;
    }

    // Alert: stalk the player and nothing else. Live sight -> chase their
    // position. Sight broken -> grip the last-known position for giveUpDelay,
    // then drop back to Patrol. No FOV check here: an alerted enemy is actively
    // hunting, so LOS alone retains the player.
    private void AlertTick()
    {
        // CurrentTarget, not player — a taunt redirects the hunt from here on.
        Transform hunted = CurrentTarget;
        if (hunted == null)
        {
            ReturnToPatrol();
            return;
        }

        bool inSight = InRange(hunted) && HasLineOfSight(hunted);
        if (inSight)
        {
            PlayerInSight = true;
            lastKnownPosition = hunted.position;
            agent.SetDestination(hunted.position);

            // Commit to Combat once the player has stayed in sight continuously for
            // timeToEngage — stalking turns into a fight. A blink out of sight resets
            // the timer (else branch), so brief cover doesn't count toward engaging.
            continuousSightTime += Time.deltaTime;
            if (continuousSightTime >= timeToEngage)
            {
                EnterCombat();
                return;
            }
        }
        else
        {
            continuousSightTime = 0f;

            // Arm the give-up timer once, on the frame sight breaks — not every
            // frame after, or the timer would never expire.
            if (PlayerInSight) giveUpAt = Time.time + giveUpDelay;
            PlayerInSight = false;

            if (Time.time < giveUpAt)
            {
                agent.SetDestination(lastKnownPosition);
            }
            else
            {
                ReturnToPatrol();
            }
        }
    }

    private void EnterCombat()
    {
        SetState(EnemyState.Combat);
        PlayerInSight = true;
        lastKnownPosition = player.position;
        // Every fight opens on the player. Companions have to earn the switch.
        combatTarget = null;
        nextRetargetAt = 0f;
    }

    // Combat: committed to the fight. Keep chasing so EnemyCombat can land its
    // swings (it only attacks while State == Combat). Losing sight grips the last
    // known position for giveUpDelay — the same forgiving window as Alert — then
    // drops back to Patrol. No FOV check: a committed enemy tracks on LOS alone.
    private void CombatTick()
    {
        // A taunt is a hard override — don't let proximity argue with it.
        if (!IsTaunted) RetargetTick();

        Transform hunted = CurrentTarget;
        if (hunted == null)
        {
            ReturnToPatrol();
            return;
        }

        bool inSight = InRange(hunted) && HasLineOfSight(hunted);
        if (inSight)
        {
            PlayerInSight = true;
            lastKnownPosition = hunted.position;
            agent.SetDestination(hunted.position);
        }
        else
        {
            if (PlayerInSight) giveUpAt = Time.time + giveUpDelay;
            PlayerInSight = false;

            if (Time.time < giveUpAt)
            {
                agent.SetDestination(lastKnownPosition);
            }
            else
            {
                ReturnToPatrol();
            }
        }
    }

    // Combat only: pick who to actually fight. The player is who we came for and
    // stays the default; a companion has to plant themselves clearly closer, in
    // the open, to take their place. That single rule is the whole aggro model —
    // no threat table, no damage accounting. Standing in the way IS the pull.
    private void RetargetTick()
    {
        if (Time.time < nextRetargetAt) return;
        nextRetargetAt = Time.time + retargetInterval;

        if (player == null)
        {
            combatTarget = null;
            return;
        }

        Transform current = combatTarget != null ? combatTarget : player;

        // Holding a companion we can no longer see or reach? Fall straight back to
        // the player — no advantage test, or we'd keep chasing a ghost around a
        // corner while the player stands behind us.
        if (combatTarget != null &&
            (Vector3.Distance(transform.position, combatTarget.position) > companionNoticeRadius
             || !HasLineOfSight(combatTarget)))
        {
            combatTarget = null;
            return;
        }

        float currentDistance = Vector3.Distance(transform.position, current.position);
        Transform challenger = null;
        float challengerDistance = float.MaxValue;

        // The player is always in the running, so a companion who peeled us off
        // can lose us again by letting the player get closer.
        if (current != player && HasLineOfSight(player))
        {
            challenger = player;
            challengerDistance = Vector3.Distance(transform.position, player.position);
        }

        IReadOnlyList<Comapnion> companions = Comapnion.Active;
        for (int i = 0; i < companions.Count; i++)
        {
            Comapnion companion = companions[i];
            if (companion == null) continue;

            Transform candidate = companion.transform;
            if (candidate == current) continue;

            float distance = Vector3.Distance(transform.position, candidate.position);
            if (distance > companionNoticeRadius) continue;
            if (distance >= challengerDistance) continue;
            if (!HasLineOfSight(candidate)) continue;

            challenger = candidate;
            challengerDistance = distance;
        }

        if (challenger == null) return;
        // Clearly closer, not merely closer — otherwise two targets shoulder to
        // shoulder make us pivot back and forth every half second and land nothing.
        if (challengerDistance * switchAdvantage >= currentDistance) return;

        combatTarget = challenger == player ? null : challenger;
    }

    private void ReturnToPatrol()
    {
        SetState(EnemyState.Patrol);
        PlayerInSight = false;
        // Losing the fight resets who it was against. Without this a re-alerted
        // enemy would resume hunting the companion it fought last time, from
        // across the level, having never re-spotted anyone.
        combatTarget = null;
        // Clear the path once so EnemyPatrolling can drive from next frame — not
        // every frame after, or patrol's destination gets wiped the moment it
        // sets one.
        if (agent.hasPath) agent.ResetPath();
    }

    private IObjectPool<EnemyFollowPlayer> EnemyPool;
    public void SetPool(IObjectPool<EnemyFollowPlayer> pool)
    {
        EnemyPool = pool;
    }

    // Death routes here instead of Destroy so the enemy can be reused. Safe to
    // call on an enemy that never came from a pool — see the fallback below.
    public void ReturnToPool()
    {
        // Clear the path while we're still on the navmesh. A released enemy that
        // keeps its path walks toward the old target for a frame after it's reused,
        // and the leftover velocity leaks into the animator's Speed.
        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }

        // Enemies dropped into the scene by hand have no pool — destroying is the
        // only way for them to die.
        if (EnemyPool != null) EnemyPool.Release(this);
        else Destroy(gameObject);
    }

    private void HandleRotation()
    {
        // Locked on: pivot to face the player even while the body is still
        // moving — otherwise the agent's loop-around-stoppingDistance path keeps
        // velocity nonzero and the enemy circles while staring at the path tangent.
        // Combat wants the same facing so swings land on the player, not the tangent.
        if ((State == EnemyState.Alert || State == EnemyState.Combat) && PlayerInSight && CurrentTarget != null)
        {
            FaceTarget(CurrentTarget.position);
            return;
        }

        // Otherwise face the direction we're walking (wander, or last-known grip).
        Vector3 velocity = agent.velocity;
        velocity.y = 0f;
        if (velocity.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
        }
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;
        float speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);
    }

    private bool InRange(Transform t)
    {
        return Vector3.Distance(transform.position, t.position) <= detectionRange;
    }

    private bool InFov(Transform t)
    {
        Vector3 toTarget = t.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(transform.forward, toTarget) <= fovAngle * 0.5f;
    }

    private bool HasLineOfSight(Transform target)
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPoint = target.position + Vector3.up * eyeHeight;
        Vector3 dir = targetPoint - origin;
        float dist = dir.magnitude;

        if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, dist, lineOfSightObstacles, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == target || hit.transform.IsChildOf(target);
        }
        return true;
    }

    private void FaceTarget(Vector3 position)
    {
        Vector3 dir = position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
    }

    private void AcquirePlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) player = go.transform;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Two spokes at ± halfFov showing the cone edges in the scene view,
        // so FOV is visible before Play mode where the mesh spawns.
        float half = fovAngle * 0.5f;
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        Vector3 leftEdge = Quaternion.AngleAxis(-half, Vector3.up) * transform.forward * detectionRange;
        Vector3 rightEdge = Quaternion.AngleAxis(half, Vector3.up) * transform.forward * detectionRange;
        Gizmos.DrawLine(origin, origin + leftEdge);
        Gizmos.DrawLine(origin, origin + rightEdge);

        if ((State == EnemyState.Alert || State == EnemyState.Combat) && CurrentTarget != null)
        {
            // Red = live sight line, orange = walking the last-known grip.
            Gizmos.color = PlayerInSight ? Color.red : new Color(1f, 0.55f, 0.1f);
            Vector3 to = PlayerInSight ? CurrentTarget.position : lastKnownPosition;
            Gizmos.DrawLine(transform.position + Vector3.up * eyeHeight, to + Vector3.up * eyeHeight);
        }
    }
}
