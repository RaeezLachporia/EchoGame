using UnityEngine;
using UnityEngine.AI;

public class BasicPlayerFollowScript : MonoBehaviour
{
    [Header("Follow Settings")]
    public float followDistance = 2.5f;   // stop moving when within this range
    public float runDistance = 6f;         // start running when farther than this
    public float walkSpeed = 2f;
    public float runSpeed = 6f;
    public float rotationSpeed = 10f;

    [Header("References")]
    public Transform player;

    private NavMeshAgent agent;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;      // we handle rotation manually for smoothness
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
            else
                Debug.LogWarning("BasicPlayerFollowScript: No GameObject tagged 'Player' found.");
        }
    }

    private void Update()
    {
        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= followDistance)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            return;
        }

        agent.speed = distanceToPlayer > runDistance ? runSpeed : walkSpeed;
        agent.SetDestination(player.position);

        RotateTowardMovementDirection();
    }

    private void RotateTowardMovementDirection()
    {
        if (agent.velocity.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(agent.velocity.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}
