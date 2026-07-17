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
    //   Combat — placeholder. Nothing transitions into it yet; attacking returns
    //            when the combat state is built.
    // Only the PLAYER triggers Patrol -> Alert. Companions are deliberately
    // invisible to this script — they roam enough that they'd trip alerts
    // constantly and drain the tension out of sneaking. How enemies respond to
    // companions is a combat-state decision, not a vision one.
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
    // Valid only while State == Alert && !PlayerInSight: the Time.time at which
    // Alert gives up if sight isn't reacquired first.
    private float giveUpAt;
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

    // True while Alert has live line of sight (cone: red). False during the
    // last-known grip (cone: orange). Always false in Patrol.
    public bool PlayerInSight { get; private set; }

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
                // Combat tick arrives with the combat-state work.
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

    private void ReturnToPatrol()
    {
        SetState(EnemyState.Patrol);
        PlayerInSight = false;
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
        if (State == EnemyState.Alert && PlayerInSight && player != null)
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

        if (State == EnemyState.Alert && player != null)
        {
            // Red = live sight line, orange = walking the last-known grip.
            Gizmos.color = PlayerInSight ? Color.red : new Color(1f, 0.55f, 0.1f);
            Vector3 to = PlayerInSight ? player.position : lastKnownPosition;
            Gizmos.DrawLine(transform.position + Vector3.up * eyeHeight, to + Vector3.up * eyeHeight);
        }
    }
}
