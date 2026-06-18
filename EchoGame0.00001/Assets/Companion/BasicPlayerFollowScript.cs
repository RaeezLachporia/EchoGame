using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class BasicPlayerFollowScript : MonoBehaviour
{
    [Header("Follow Settings")]
    public float followDistance = 2.5f;
    [Tooltip("Must exceed this distance before the companion starts moving again after stopping")]
    public float resumeDistance = 3.5f;
    public float runDistance = 6f;
    public float sprintDistance = 12f;
    public float walkSpeed = 2f;
    public float runSpeed = 6f;
    public float sprintSpeed = 10f;
    public float rotationSpeed = 10f;

    [Header("Jump Settings")]
    public float jumpDuration = 0.6f;
    public float jumpHeight = 1.5f;

    [Header("Animation")]
    public Animator animator;
    public float animationDampTime = 0.1f;
    [Tooltip("Speed the walk animation was authored for — tweak until footsteps match")]
    public float walkAnimSpeed = 2f;
    [Tooltip("Speed the run animation was authored for — tweak until footsteps match")]
    public float runAnimSpeed = 6f;
    [Tooltip("Speed the sprint animation was authored for — tweak until footsteps match")]
    public float sprintAnimSpeed = 10f;

    [Header("Teleport Fallback")]
    public bool teleportEnabled = true;
    [Tooltip("Teleport to the player if the path is blocked and they are this far away")]
    public float teleportDistance = 18f;
    [Tooltip("How far behind the player to land when teleporting")]
    public float teleportOffset = 2f;

    [Header("References")]
    public Transform player;

    [Header("Debug")]
    [SerializeField] private float currentSpeed;

    private NavMeshAgent agent;
    private InputManager playerInput;
    private bool isFollowing = false;
    private bool isJumping = false;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int JumpHash = Animator.StringToHash("IsJumping");

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = false;

        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                playerInput = playerObj.GetComponent<InputManager>();
            }
            else
            {
                Debug.LogWarning("BasicPlayerFollowScript: No GameObject tagged 'Player' found.");
            }
        }
        else
        {
            playerInput = player.GetComponent<InputManager>();
        }
    }

    private void Update()
    {
        if (player == null) return;

        if (isJumping) return;

        if (agent.isOnOffMeshLink)
        {
            StartCoroutine(JumpAcrossLink());
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (isFollowing && distanceToPlayer <= followDistance)
            isFollowing = false;
        else if (!isFollowing && distanceToPlayer > resumeDistance)
            isFollowing = true;

        if (!isFollowing)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            currentSpeed = 0f;
            UpdateAnimation(0f);
            return;
        }

        if (teleportEnabled && distanceToPlayer > teleportDistance && PathIsBlocked())
        {
            TeleportToPlayer();
            return;
        }

        bool playerIsSprinting = playerInput != null && playerInput.isSprinting;
        bool tooFarBehind = distanceToPlayer > sprintDistance;

        if (playerIsSprinting || tooFarBehind)
            agent.speed = sprintSpeed;
        else if (distanceToPlayer > runDistance)
            agent.speed = runSpeed;
        else
            agent.speed = walkSpeed;
        agent.SetDestination(player.position);

        currentSpeed = agent.velocity.magnitude;
        UpdateAnimation(currentSpeed);
        RotateTowardMovementDirection();
    }

    private void UpdateAnimation(float speed)
    {
        if (animator == null) return;

        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);

        if (speed < 0.1f)
        {
            animator.speed = 1f;
            return;
        }

        // Scale playback rate so footsteps stay locked to ground movement
        float designedSpeed;
        if (agent.speed >= sprintSpeed)
            designedSpeed = sprintAnimSpeed;
        else if (agent.speed >= runSpeed)
            designedSpeed = runAnimSpeed;
        else
            designedSpeed = walkAnimSpeed;

        animator.speed = speed / designedSpeed;
    }

    private IEnumerator JumpAcrossLink()
    {
        isJumping = true;

        OffMeshLinkData link = agent.currentOffMeshLinkData;
        Vector3 start = transform.position;
        Vector3 end = link.endPos;

        if (animator != null)
            animator.SetTrigger(JumpHash);

        float elapsed = 0f;
        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / jumpDuration;
            Vector3 pos = Vector3.Lerp(start, end, t);
            pos.y += jumpHeight * Mathf.Sin(t * Mathf.PI);
            transform.position = pos;

            // face the direction of travel during the jump
            Vector3 dir = (end - start);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), rotationSpeed * Time.deltaTime);

            yield return null;
        }

        transform.position = end;
        agent.CompleteOffMeshLink();
        isJumping = false;
    }

    private bool PathIsBlocked()
    {
        return agent.pathStatus == NavMeshPathStatus.PathPartial
            || agent.pathStatus == NavMeshPathStatus.PathInvalid;
    }

    private void TeleportToPlayer()
    {
        Vector3 behindPlayer = player.position - player.forward * teleportOffset;
        if (NavMesh.SamplePosition(behindPlayer, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    private void RotateTowardMovementDirection()
    {
        if (agent.velocity.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(agent.velocity.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}
