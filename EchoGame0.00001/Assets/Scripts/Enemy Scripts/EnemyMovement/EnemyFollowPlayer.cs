using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyFollowPlayer : MonoBehaviour
{
    // Top-level enemy brain.
    //   Patrol — EnemyPatrolling drives the agent; we just watch for the player.
    //   Alert  — spotted the player: stalk them (chase + last-known grip) but
    //            never swing. EnemyCombat holds its attacks outside Combat.
    //   Combat — fully committed: chase and attack. The vision cone switches off
    //            (stealth is over, so the readout stops being useful) and entering
    //            it rallies nearby enemies into Combat as well.
    //
    // Escalation into Combat has three routes, all through EnterCombat():
    //   1. Alert holds line of sight for secondsToEngage.
    //   2. The player lands a hit (PlayerBasicCombat calls it directly).
    //   3. A nearby enemy rallies us when IT enters Combat.
    //
    // Only the PLAYER drives any of this. Companions are deliberately invisible
    // here — they roam enough that they'd trip alerts constantly and drain the
    // tension out of sneaking, and companion-dealt damage doesn't aggro either.
    // The intended future hook is the player ORDERING a companion to attack,
    // which would call EnterCombat explicitly — player intent, not companion
    // proximity. That's why combat entry is an explicit call rather than
    // something inferred from the damage path.
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
    [Tooltip("Seconds of line of sight during Alert before the enemy commits to Combat. Time only accrues while the player is actually visible, so ducking behind cover stalls the countdown rather than resetting it.")]
    [SerializeField] private float secondsToEngage = 2.5f;
    [Tooltip("Seconds after losing sight in Combat before de-escalating back to Alert. Longer than the Alert give-up — a committed enemy searches harder before losing interest.")]
    [SerializeField] private float combatGiveUpDelay = 4f;
    [Tooltip("Radius of the rally shout fired when this enemy enters Combat: every enemy inside it is pulled into Combat too. Deliberately ignores line of sight — it's a yell, it carries through walls. Set to 0 to make this enemy fight alone.")]
    [SerializeField] private float rallyRadius = 12f;
    [Tooltip("Layers searched for enemies to rally. Narrow this to your Enemy layer so the overlap check isn't sifting through level geometry.")]
    [SerializeField] private LayerMask rallyLayers = ~0;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;

    [Header("State Tracking")]
    [Tooltip("Read-only mirror of the current state so it shows live in the inspector during Play mode. Changing it by hand does nothing — State is driven from code.")]
    [SerializeField] private EnemyState stateDisplay = EnemyState.Patrol;
    [Tooltip("Log every state transition to the console with the enemy's name and timestamp.")]
    [SerializeField] private bool logStateChanges = false;

    private Transform player;
    private Vector3 lastKnownPosition;
    // The Time.time at which the current state gives up on a player it can't
    // see. Shared by Alert and Combat — they're mutually exclusive, and each
    // arms it with its own delay.
    private float giveUpAt;
    // Accumulated seconds of line of sight during the current Alert. Escalates
    // to Combat at secondsToEngage; reset whenever Alert is (re-)entered.
    private float sightedDuration;
    private EnemyCombat combat;
    private EnemyHealth health;
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

    // True while Alert or Combat has live line of sight on the player. In Alert
    // this drives the cone's red-vs-orange tint; in Combat the cone is hidden
    // and it just gates facing and the give-up timer. Always false in Patrol.
    public bool PlayerInSight { get; private set; }

    // EnemyVisionCone reads these to build and drive the ground-fan visual.
    public float DetectionRange => detectionRange;
    public float FovAngle => fovAngle;
    public LayerMask SightObstacles => lineOfSightObstacles;
    public float EyeHeight => eyeHeight;

    void Awake()
    {
        combat = GetComponent<EnemyCombat>();
        health = GetComponent<EnemyHealth>();
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
        sightedDuration = 0f;
    }

    void Start()
    {
        AcquirePlayer();
    }

    void Update()
    {
        if (player == null) AcquirePlayer();

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
        sightedDuration = 0f;
        lastKnownPosition = player.position;
        // Patrol left the agent on wander speed — bump to chase speed.
        agent.speed = moveSpeed;
    }

    // Alert: stalk the player and nothing else. Live sight -> chase their
    // position. Sight broken -> grip the last-known position for giveUpDelay,
    // then drop back to Patrol. No FOV check here: an alerted enemy is actively
    // hunting, so LOS alone retains the player.
    private void AlertTick()
    {
        if (player == null)
        {
            ReturnToPatrol();
            return;
        }

        bool inSight = InRange(player) && HasLineOfSight(player);
        if (inSight)
        {
            PlayerInSight = true;
            lastKnownPosition = player.position;
            agent.SetDestination(player.position);

            // Commit once we've held eyes on them long enough. The timer only
            // advances while they're actually visible, so breaking sight stalls
            // it rather than clearing it — repeated peeking still escalates.
            sightedDuration += Time.deltaTime;
            if (sightedDuration >= secondsToEngage) EnterCombat(player.position);
        }
        else
        {
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

    // The single door into Combat, for all three routes: the Alert sighting
    // timer, the player landing a hit, and a rally from a nearby enemy. Public
    // so callers don't need to know anything about Alert or the sight timer —
    // they just hand over where the player is known to be.
    public void EnterCombat(Vector3 knownPosition)
    {
        // Already committed. This early-out is also what stops the rally from
        // ping-ponging: A pulls in B, B's own rally finds A already in Combat
        // and stops there, so a cluster is swept exactly once.
        if (State == EnemyState.Combat) return;

        // A killing blow must not rally. Without this, a silent takedown would
        // scream to everyone in rallyRadius and stealth kills would be pointless.
        // Callers damage first, then call this, so isDead is already settled.
        if (health != null && !health.IsAlive) return;

        lastKnownPosition = knownPosition;
        agent.speed = moveSpeed;
        // Arm the give-up window up front: Combat can be entered blind (hit from
        // behind, or rallied from across the room), and the enemy needs its full
        // hunting window even having never laid eyes on the player.
        giveUpAt = Time.time + combatGiveUpDelay;
        SetState(EnemyState.Combat);
        Rally();
    }

    // Combat: run the player down and let EnemyCombat swing when it's close
    // enough. No FOV gate and no sighting timer — this enemy is past deciding.
    private void CombatTick()
    {
        if (player == null)
        {
            ReturnToPatrol();
            return;
        }

        bool inSight = InRange(player) && HasLineOfSight(player);
        if (inSight)
        {
            PlayerInSight = true;
            lastKnownPosition = player.position;
            // Refresh rather than arm-on-transition (which is what Alert does):
            // Combat can start without sight, so there's no reliable "frame we
            // lost them" to hang the timer off.
            giveUpAt = Time.time + combatGiveUpDelay;
            agent.SetDestination(player.position);
        }
        else
        {
            PlayerInSight = false;
            if (Time.time < giveUpAt) agent.SetDestination(lastKnownPosition);
            else DeEscalateToAlert();
        }
    }

    // Combat -> Alert rather than straight to Patrol. A committed enemy that
    // loses the player should sweep the last known position first (the cone
    // reappears in its orange searching tint), and Alert's own give-up handles
    // the drop back to Patrol from there.
    private void DeEscalateToAlert()
    {
        SetState(EnemyState.Alert);
        PlayerInSight = false;
        sightedDuration = 0f;
        giveUpAt = Time.time + giveUpDelay;
    }

    // The shout. Every enemy in range joins the fight and inherits what we know
    // about the player's position, so they converge on somewhere useful instead
    // of milling around. No line-of-sight filter: this is noise, not sight.
    private void Rally()
    {
        if (rallyRadius <= 0f) return;

        Collider[] nearby = Physics.OverlapSphere(transform.position, rallyRadius, rallyLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < nearby.Length; i++)
        {
            EnemyFollowPlayer other = nearby[i].GetComponentInParent<EnemyFollowPlayer>();
            if (other == null || other == this) continue;
            other.EnterCombat(lastKnownPosition);
        }
    }

    private void ReturnToPatrol()
    {
        SetState(EnemyState.Patrol);
        PlayerInSight = false;
        sightedDuration = 0f;
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
        // velocity nonzero and the enemy circles while staring at the path
        // tangent. PlayerInSight is only ever true in Alert or Combat, so this
        // covers both without naming them. It matters more in Combat: the attack
        // hitbox projects straight out of transform.forward.
        if (PlayerInSight && player != null)
        {
            FaceTarget(player.position);
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

        // Rally reach — who this enemy drags into a fight when it commits.
        if (rallyRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, rallyRadius);
        }

        if (State != EnemyState.Patrol && player != null)
        {
            // Red = live sight line, orange = walking the last-known grip.
            Gizmos.color = PlayerInSight ? Color.red : new Color(1f, 0.55f, 0.1f);
            Vector3 to = PlayerInSight ? player.position : lastKnownPosition;
            Gizmos.DrawLine(transform.position + Vector3.up * eyeHeight, to + Vector3.up * eyeHeight);
        }
    }
}
