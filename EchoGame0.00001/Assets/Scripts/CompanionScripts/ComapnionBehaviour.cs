using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ComapnionBehaviour : MonoBehaviour
{
    [Header("Player Idle Detection")]
    [Tooltip("Player must be moving slower than this (m/s) to count as standing still.")]
    [SerializeField] private float playerIdleThreshold = 0.3f;
    [Tooltip("Seconds the player must stay still before the companion starts wandering.")]
    [SerializeField] private float playerIdleDelay = 1.5f;

    [Header("Wander")]
    [Tooltip("Min/max seconds between picking a new wander point.")]
    [SerializeField] private Vector2 wanderInterval = new Vector2(2.5f, 5f);
    [Tooltip("Min/max distance from the player when picking a wander point. Keep max small so companions don't drift off.")]
    [SerializeField] private Vector2 wanderRadius = new Vector2(1.5f, 3.5f);
    [Tooltip("Movement speed while idling. Slower than follow walk speed for a relaxed feel.")]
    [SerializeField] private float wanderSpeed = 1.2f;
    [Tooltip("Considered 'arrived' at the wander point when the agent gets within this distance.")]
    [SerializeField] private float arriveTolerance = 0.25f;

    [Header("References")]
    [SerializeField] private Transform player;

    private NavMeshAgent agent;
    private BasicPlayerFollowScript follow;
    private CompanionCommand command;
    private CompanionAbility[] abilities;
    private Rigidbody playerRb;
    private float playerStillTime;
    private float nextWanderTime;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        follow = GetComponent<BasicPlayerFollowScript>();
        command = GetComponent<CompanionCommand>();
        abilities = GetComponents<CompanionAbility>();
    }

    void Start()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null) playerRb = player.GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (player == null) return;

        // Attack command takes precedence over wandering — otherwise we'd fight
        // CompanionCommand for the agent destination while the companion is trying
        // to charge an enemy.
        if (command != null && command.HasActiveCommand)
        {
            playerStillTime = 0f;
            return;
        }

        // If an ability is running (like Naledi off healing someone), don't wander.
        if (AnyAbilityBusy())
        {
            playerStillTime = 0f;
            return;
        }

        // Follow script is in charge while the player is moving away — back off.
        if (follow != null && follow.IsFollowing)
        {
            playerStillTime = 0f;
            return;
        }

        if (!PlayerIsIdle())
        {
            playerStillTime = 0f;
            return;
        }

        playerStillTime += Time.deltaTime;
        if (playerStillTime < playerIdleDelay) return;

        if (Time.time >= nextWanderTime && HasArrived())
        {
            PickWanderTarget();
            nextWanderTime = Time.time + Random.Range(wanderInterval.x, wanderInterval.y);
        }
    }

    private bool AnyAbilityBusy()
    {
        for (int i = 0; i < abilities.Length; i++)
            if (abilities[i] != null && abilities[i].IsBusy) return true;
        return false;
    }

    private bool PlayerIsIdle()
    {
        if (playerRb == null) return true;
        Vector3 v = playerRb.linearVelocity;
        // Ignore vertical (gravity / jumping) — only horizontal motion counts as "moving".
        v.y = 0f;
        return v.magnitude < playerIdleThreshold;
    }

    private bool HasArrived()
    {
        if (!agent.hasPath) return true;
        if (agent.pathPending) return false;
        return agent.remainingDistance <= arriveTolerance;
    }

    private void PickWanderTarget()
    {
        // Random point in an annulus around the player so companions don't all
        // pile on the same spot and don't sit directly on top of them either.
        Vector2 dir = Random.insideUnitCircle.normalized;
        float distance = Random.Range(wanderRadius.x, wanderRadius.y);
        Vector3 candidate = player.position + new Vector3(dir.x * distance, 0f, dir.y * distance);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
        {
            agent.speed = wanderSpeed;
            agent.SetDestination(hit.position);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (player == null) return;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(player.position, wanderRadius.y);
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(player.position, wanderRadius.x);
    }
}
