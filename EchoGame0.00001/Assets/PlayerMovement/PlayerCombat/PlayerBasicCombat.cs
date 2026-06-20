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
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 hitOrigin = transform.position + transform.forward * 0.8f + Vector3.up * 0.5f;
        Gizmos.DrawWireSphere(hitOrigin, attackRange);
    }
}
