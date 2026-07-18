using UnityEngine;

public class PlayerBasicCombat : MonoBehaviour
{
    InputManager inputManager;
    AnimatorManager animatorManager;

    public float attackDamage = 20f;
    public float attackRange = 1.5f;
    public float attackCooldown = 0.8f;
    public LayerMask enemyLayers;

    public bool isAttacking { get; private set; }

    private float attackTimer;

    private void Awake()
    {
        inputManager = GetComponent<InputManager>();
        animatorManager = GetComponent<AnimatorManager>();
    }

    public void handleAttack()
    {
        attackTimer -= Time.deltaTime;

        if (!inputManager.attackInput || attackTimer > 0f) return;
        inputManager.attackInput = false;

        isAttacking = true;
        animatorManager.playAttackAnimation();
        attackTimer = attackCooldown;
    }

    // Called via Animation Event near the end of the attack clip (~75-80%)
    public void EndAttack()
    {
        isAttacking = false;
    }

    // Called via Animation Event at the hit frame of the attack animation
    public void DealDamage()
    {
        Vector3 hitOrigin = transform.position + transform.forward * 0.8f + Vector3.up * 0.5f;
        Collider[] hits = Physics.OverlapSphere(hitOrigin, attackRange, enemyLayers);
        foreach (Collider hit in hits)
        {
            IDamageable damageable = hit.GetComponent<IDamageable>();
            damageable?.TakeDamage(attackDamage);

            // Getting struck commits an enemy to Combat no matter what it could
            // see. Told explicitly rather than routed through TakeDamage, because
            // IDamageable carries no attacker and aggro is deliberately
            // player-only — a companion hitting the same enemy must not trigger
            // this. Order matters: damage first, so a killing blow leaves the
            // enemy dead and EnterCombat's own is-alive check silences its rally.
            hit.GetComponentInParent<EnemyFollowPlayer>()?.EnterCombat(transform.position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 hitOrigin = transform.position + transform.forward * 0.8f + Vector3.up * 0.5f;
        Gizmos.DrawWireSphere(hitOrigin, attackRange);
    }
}
