using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CompanionCommand : MonoBehaviour
{
    [Header("Attack Approach")]
    [Tooltip("Companion stops moving once within this distance of the commanded enemy.")]
    [SerializeField] private float attackRange = 1.8f;
    [Tooltip("NavMeshAgent speed while charging a commanded target.")]
    [SerializeField] private float chaseSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;
    [Tooltip("Speed the run animation was authored for — used to keep footsteps synced while chasing.")]
    [SerializeField] private float chaseAnimSpeed = 6f;

    private NavMeshAgent agent;
    private Transform targetEnemy;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // Other companion scripts (follow, wander) check this to step aside so we don't
    // fight over SetDestination every frame.
    public bool HasActiveCommand => targetEnemy != null;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponent<Animator>();
    }

    public void CommandAttack(Transform enemy)
    {
        targetEnemy = enemy;
    }

    public void CancelCommand()
    {
        targetEnemy = null;
        if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    void Update()
    {
        if (targetEnemy == null) return;
        if (!agent.isOnNavMesh) return;

        // Enemy destroyed since the command was issued (e.g. died mid-charge).
        if (!targetEnemy.gameObject.activeInHierarchy)
        {
            CancelCommand();
            return;
        }

        Vector3 toEnemy = targetEnemy.position - transform.position;
        toEnemy.y = 0f;
        float distance = toEnemy.magnitude;

        if (distance > attackRange)
        {
            agent.speed = chaseSpeed;
            agent.SetDestination(targetEnemy.position);
        }
        else
        {
            // In range: stop pathing and face the enemy. Attack trigger would fire
            // here once damage is wired up — kept out for now per design.
            if (agent.hasPath) agent.ResetPath();
        }

        if (toEnemy.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(toEnemy.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
        }

        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;
        float speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);
        animator.speed = speed > 0.1f ? speed / chaseAnimSpeed : 1f;
    }
}
