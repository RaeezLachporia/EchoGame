using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyFollowPlayer : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string companionTag = "Comapnion";

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotationSpeed = 8f;
    [SerializeField] private float stoppingDistance = 1.5f;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 15f;
    [SerializeField] private LayerMask lineOfSightObstacles;
    [SerializeField] private float eyeHeight = 1.5f;

    [Header("Aggro")]
    [Tooltip("Seconds after losing line of sight before we forget the target. During this window we walk to the last known position — not the target's live position, or we'd path through walls like an aimbot.")]
    [SerializeField] private float giveUpDelay = 2f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;

    private Transform player;
    private Transform companion;
    private Transform currentTarget;
    private Vector3 lastKnownPosition;
    // -1 = target in sight right now. Any other value is the Time.time at which we give up
    // if line of sight isn't reacquired first.
    private float loseTargetAt = -1f;
    private EnemyCombat combat;
    private NavMeshAgent agent;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // EnemyPatrolling reads this to yield the agent while chase/grip is active.
    public bool HasTarget => currentTarget != null;

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
    }

    // Pooled enemies keep their fields across lives. Without this, a reused enemy
    // wakes up remembering the last target it saw before dying and chases its ghost.
    void OnEnable()
    {
        currentTarget = null;
        loseTargetAt = -1f;
    }

    void Start()
    {
        AcquirePlayer();
        AcquireCompanion();
    }

    void Update()
    {
        if (player == null) AcquirePlayer();
        if (companion == null) AcquireCompanion();

        // If the agent failed to spawn on the NavMesh, do nothing — SetDestination
        // would just log warnings and the animation would jitter on garbage velocity.
        if (!agent.isOnNavMesh)
        {
            if (animator != null) animator.SetFloat(SpeedHash, 0f);
            return;
        }

        bool attacking = combat != null && combat.isAttacking;

        if (attacking)
        {
            // Freeze pathing/rotation during the swing so the hitbox lands where committed.
            if (agent.hasPath) agent.ResetPath();
        }
        else
        {
            Transform visible = ChooseTarget();
            if (visible != null)
            {
                // Sighted — commit to chase and update where they were last seen. If
                // EnemyPatrolling was driving up to this frame the agent is on wander
                // speed, so bump it to chase speed on the transition.
                if (currentTarget == null) agent.speed = moveSpeed;
                currentTarget = visible;
                lastKnownPosition = visible.position;
                loseTargetAt = -1f;
                agent.SetDestination(visible.position);
            }
            else if (currentTarget != null)
            {
                // Just lost sight — arm the give-up timer once and grip on the last
                // known position. Not the target's live position; pathing to that
                // through walls would look like an aimbot.
                if (loseTargetAt < 0f) loseTargetAt = Time.time + giveUpDelay;

                if (Time.time < loseTargetAt)
                {
                    agent.SetDestination(lastKnownPosition);
                }
                else
                {
                    // Give up. Clear the path once so EnemyPatrolling can drive from
                    // next frame — not every frame after, or patrol's destination gets
                    // wiped the moment it sets one.
                    currentTarget = null;
                    loseTargetAt = -1f;
                    if (agent.hasPath) agent.ResetPath();
                }
            }
            // else: no target, no retention → don't touch the agent. Patrol drives.

            HandleRotation();
        }

        UpdateAnimation();
    }
    private IObjectPool<EnemyFollowPlayer> EnemyPool;
    public void SetPool(IObjectPool<EnemyFollowPlayer>pool)
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
        // If we have a target, always pivot to face them — the enemy is "locked
        // on", so movement and facing are decoupled. Otherwise the agent's
        // loop-around-stoppingDistance path keeps velocity nonzero and the
        // enemy would circle the player while staring at the path tangent.
        if (currentTarget != null)
        {
            FaceTarget(currentTarget.position);
            return;
        }

        // No target — face the direction we're wandering, if any.
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

    // Fresh acquisition requires line of sight — enemies patrol until they actually
    // see a target. Retention (chasing after LOS breaks briefly) is handled in Update
    // via loseTargetAt/lastKnownPosition, not here.
    private Transform ChooseTarget()
    {
        bool playerVisible = player != null && InRange(player) && HasLineOfSight(player);
        bool companionVisible = companion != null && InRange(companion) && HasLineOfSight(companion);

        if (playerVisible && companionVisible)
            return Closer(player, companion);
        if (playerVisible) return player;
        if (companionVisible) return companion;

        return null;
    }

    private bool InRange(Transform t)
    {
        return Vector3.Distance(transform.position, t.position) <= detectionRange;
    }

    private Transform Closer(Transform a, Transform b)
    {
        float da = Vector3.Distance(transform.position, a.position);
        float db = Vector3.Distance(transform.position, b.position);
        return da <= db ? a : b;
    }

    private bool HasLineOfSight(Transform target)
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPoint = target.position + Vector3.up * eyeHeight;
        Vector3 dir = targetPoint - origin;
        float dist = dir.magnitude;

        if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, dist, lineOfSightObstacles, QueryTriggerInteraction.Ignore))
        {
            // Something opaque is between us and the target.
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

    private void AcquireCompanion()
    {
        GameObject go = GameObject.FindGameObjectWithTag(companionTag);
        if (go != null) companion = go.transform;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        if (currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up * eyeHeight,
                            currentTarget.position + Vector3.up * eyeHeight);
        }
    }
}
