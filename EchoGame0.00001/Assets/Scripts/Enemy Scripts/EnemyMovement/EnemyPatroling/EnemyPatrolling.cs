using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyPatrolling : MonoBehaviour
{
    [Header("Wander")]
    [Tooltip("Min/max distance from the spawn anchor when picking a wander point.")]
    [SerializeField] private Vector2 wanderRadius = new Vector2(2f, 6f);
    [Tooltip("Min/max seconds between picking a new wander point.")]
    [SerializeField] private Vector2 wanderInterval = new Vector2(3f, 6f);
    [SerializeField] private float wanderSpeed = 2f;
    [SerializeField] private float arriveTolerance = 0.5f;

    private NavMeshAgent agent;
    private EnemyFollowPlayer follow;
    private Vector3 anchor;
    private float nextWanderTime;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        follow = GetComponent<EnemyFollowPlayer>();
    }

    // The anchor is where this enemy was placed. That's the spawn point for pooled
    // enemies and wherever they sit in the scene for hand-placed ones. Reading it
    // from transform.position here works because Spawner.OnGetFromPool moves the
    // enemy BEFORE calling SetActive(true), so by the time OnEnable fires we're
    // already at the spawn point.
    void OnEnable()
    {
        anchor = transform.position;
        nextWanderTime = 0f;
    }

    void Update()
    {
        // Patrol only drives the agent while the brain is actually in Patrol —
        // Alert (and later Combat) own the agent. Same yield pattern as
        // ComapnionBehaviour yielding to CompanionCommand.HasActiveCommand.
        if (follow != null && follow.State != EnemyFollowPlayer.EnemyState.Patrol) return;
        if (!agent.isOnNavMesh) return;

        // EnemyFollowPlayer sets a non-zero stoppingDistance for chase — the agent
        // stops that far short of any destination. Fold it into the arrival check
        // or wander picks look permanent: the agent stops 1.5 units from the wander
        // point and remainingDistance never drops below the tolerance.
        bool arrived = !agent.pathPending
                       && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + arriveTolerance);
        if (!arrived) return;
        if (Time.time < nextWanderTime) return;

        PickWanderTarget();
        nextWanderTime = Time.time + Random.Range(wanderInterval.x, wanderInterval.y);
    }

    private void PickWanderTarget()
    {
        Vector2 dir = Random.insideUnitCircle.normalized;
        float distance = Random.Range(wanderRadius.x, wanderRadius.y);
        Vector3 candidate = anchor + new Vector3(dir.x * distance, 0f, dir.y * distance);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            agent.speed = wanderSpeed;
            agent.SetDestination(hit.position);
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw around the runtime anchor while playing (so you can see where an
        // enemy is bound to patrol), or around the current position in the editor.
        Vector3 c = Application.isPlaying ? anchor : transform.position;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(c, wanderRadius.y);
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(c, wanderRadius.x);
    }
}
