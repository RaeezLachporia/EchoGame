using UnityEngine;
using UnityEngine.AI;

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

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;

    private Transform player;
    private Transform companion;
    private Transform currentTarget;
    private EnemyCombat combat;
    private NavMeshAgent agent;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

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
            currentTarget = ChooseTarget();
            if (currentTarget != null)
                agent.SetDestination(currentTarget.position);
            else if (agent.hasPath)
                agent.ResetPath();

            HandleRotation();
        }

        UpdateAnimation();
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

    private Transform ChooseTarget()
    {
        bool playerInRange = player != null && InRange(player);
        bool companionInRange = companion != null && InRange(companion);

        bool playerVisible = playerInRange && HasLineOfSight(player);
        bool companionVisible = companionInRange && HasLineOfSight(companion);

        // Prefer the closer target among those in line of sight.
        if (playerVisible && companionVisible)
            return Closer(player, companion);
        if (playerVisible) return player;
        if (companionVisible) return companion;

        // Fallback: nothing in LOS — chase the closer one that's still in detection range.
        if (playerInRange && companionInRange)
            return Closer(player, companion);
        if (playerInRange) return player;
        if (companionInRange) return companion;

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
