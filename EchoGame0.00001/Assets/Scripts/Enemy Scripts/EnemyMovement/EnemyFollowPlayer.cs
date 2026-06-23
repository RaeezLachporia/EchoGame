using UnityEngine;

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

    private Transform player;
    private Transform companion;
    private Transform currentTarget;

    void Start()
    {
        AcquirePlayer();
        AcquireCompanion();
    }

    void Update()
    {
        if (player == null) AcquirePlayer();
        if (companion == null) AcquireCompanion();

        currentTarget = ChooseTarget();
        if (currentTarget == null) return;

        float distance = Vector3.Distance(transform.position, currentTarget.position);
        if (distance > stoppingDistance)
            MoveToward(currentTarget.position);

        FaceTarget(currentTarget.position);
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

    private void MoveToward(Vector3 position)
    {
        Vector3 next = Vector3.MoveTowards(transform.position, position, moveSpeed * Time.deltaTime);
        next.y = transform.position.y; // keep on current ground plane
        transform.position = next;
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
