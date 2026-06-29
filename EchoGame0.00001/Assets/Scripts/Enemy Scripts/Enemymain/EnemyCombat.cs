using UnityEngine;

public class EnemyCombat : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string companionTag = "Comapnion";

    [Header("Attack")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private float attackCooldown = 1.5f;

    private float cooldownRemaining;
    private float nextDiagLog;

    void Update()
    {
        if (Time.time >= nextDiagLog)
        {
            nextDiagLog = Time.time + 1f;
            DiagnosticLog();
        }

        if (cooldownRemaining > 0f)
        {
            cooldownRemaining -= Time.deltaTime;
            return;
        }

        IDamageable target = FindTargetInRange();
        if (target == null) return;

        Debug.Log($"[EnemyCombat] {name} swung at {((MonoBehaviour)target).name} for {damage}");
        target.TakeDamage(damage);
        cooldownRemaining = attackCooldown;
    }

    private void DiagnosticLog()
    {
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player == null)
        {
            Debug.LogWarning($"[EnemyCombat] {name}: no GameObject tagged '{playerTag}' in scene.");
            return;
        }
        float d = Vector3.Distance(transform.position, player.transform.position);
        bool hasDmg = player.GetComponent<IDamageable>() != null;
        Debug.Log($"[EnemyCombat] {name}: player dist={d:F2}, attackRange={attackRange}, inRange={d <= attackRange}, hasIDamageable={hasDmg}");
    }

    private IDamageable FindTargetInRange()
    {
        IDamageable best = null;
        float bestDist = attackRange;

        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player != null)
            TryConsider(player, ref best, ref bestDist);

        GameObject[] companions = GameObject.FindGameObjectsWithTag(companionTag);
        for (int i = 0; i < companions.Length; i++)
            TryConsider(companions[i], ref best, ref bestDist);

        return best;
    }

    private void TryConsider(GameObject candidate, ref IDamageable best, ref float bestDist)
    {
        float d = Vector3.Distance(transform.position, candidate.transform.position);
        if (d > bestDist) return;

        IDamageable dmg = candidate.GetComponent<IDamageable>();
        if (dmg == null) return;

        best = dmg;
        bestDist = d;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, attackRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
