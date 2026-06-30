using UnityEngine;

public class EnemyCombat : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string companionTag = "Comapnion";

    [Header("Attack")]
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private float attackCooldown = 1.5f;
    [Tooltip("Hit volume is centered this far in front of the enemy.")]
    [SerializeField] private float hitForwardOffset = 0.8f;
    [Tooltip("Vertical offset of the hit volume from the enemy's origin.")]
    [SerializeField] private float hitVerticalOffset = 0.5f;
    [Tooltip("Vertical extent of the hit volume (capsule height). 0 = sphere of radius attackRange.")]
    [SerializeField] private float attackHeight = 0f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [Tooltip("Safety net: isAttacking is force-cleared after this long even if the EndAttack animation event never fires. Set a bit longer than the attack clip.")]
    [SerializeField] private float maxAttackDuration = 2f;

    public bool isAttacking { get; private set; }

    private static readonly int AttackHash = Animator.StringToHash("Attack");

    private float cooldownRemaining;
    private float attackElapsed;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

        if (isAttacking)
        {
            attackElapsed += Time.deltaTime;
            if (attackElapsed >= maxAttackDuration) isAttacking = false;
            return;
        }

        if (cooldownRemaining > 0f) return;
        if (!TargetInHitbox()) return;

        StartAttack();
    }

    private void StartAttack()
    {
        isAttacking = true;
        attackElapsed = 0f;
        cooldownRemaining = attackCooldown;
        if (animator != null) animator.SetTrigger(AttackHash);
    }

    // Called from the attack animation's Animation Event at the swing frame.
    public void DealDamage()
    {
        Collider[] hits = OverlapHitVolume();
        foreach (Collider hit in hits)
        {
            if (!hit.CompareTag(playerTag) && !hit.CompareTag(companionTag)) continue;
            IDamageable damageable = hit.GetComponent<IDamageable>();
            damageable?.TakeDamage(damage);
        }
    }

    // Called from an Animation Event near the end of the attack clip.
    public void EndAttack()
    {
        isAttacking = false;
    }

    private bool TargetInHitbox()
    {
        Collider[] hits = OverlapHitVolume();
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].CompareTag(playerTag) || hits[i].CompareTag(companionTag))
                return true;
        }
        return false;
    }

    private Collider[] OverlapHitVolume()
    {
        Vector3 hitOrigin = HitOrigin();
        Vector3 halfHeight = Vector3.up * (attackHeight * 0.5f);
        return Physics.OverlapCapsule(hitOrigin - halfHeight, hitOrigin + halfHeight, attackRange);
    }

    private Vector3 HitOrigin()
    {
        return transform.position + transform.forward * hitForwardOffset + Vector3.up * hitVerticalOffset;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 hitOrigin = HitOrigin();
        Vector3 halfHeight = Vector3.up * (attackHeight * 0.5f);
        Gizmos.DrawWireSphere(hitOrigin - halfHeight, attackRange);
        if (attackHeight > 0f)
            Gizmos.DrawWireSphere(hitOrigin + halfHeight, attackRange);
    }
}
