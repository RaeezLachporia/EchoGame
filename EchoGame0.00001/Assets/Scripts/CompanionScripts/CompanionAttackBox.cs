using UnityEngine;

// Per-companion hit volume. Different companions want different reach and shape,
// so keeping the box separate from CompanionCommand lets us tweak it per prefab
// without touching the attack orchestration (chase / cooldown / anim).
public class CompanionAttackBox : MonoBehaviour
{
    [Header("Hit Volume")]
    [Tooltip("Overlap radius of the swing.")]
    [SerializeField] private float attackRange = 1.5f;
    [Tooltip("How far in front of the companion the hit volume sits.")]
    [SerializeField] private float hitForwardOffset = 0.8f;
    [Tooltip("Vertical offset from the companion's origin.")]
    [SerializeField] private float hitVerticalOffset = 0.5f;
    [Tooltip("Capsule height of the hit volume. 0 = plain sphere.")]
    [SerializeField] private float attackHeight = 0f;

    [Header("Targeting")]
    [Tooltip("Only colliders with this tag take damage.")]
    [SerializeField] private string enemyTag = "Enemy";

    [Header("Debug")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 1f, 0.4f, 0.8f);

    // Called by CompanionCommand's DealDamage animation event. Returns how many
    // enemies got hit so the caller can log / react.
    public int TryDealDamage(float damage)
    {
        Collider[] hits = OverlapHitVolume();
        int landed = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            if (!hits[i].CompareTag(enemyTag)) continue;
            // GetComponentInParent so weapon meshes / child hitboxes still resolve
            // up to the enemy root that owns the health.
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null) continue;
            damageable.TakeDamage(damage);
            landed++;
        }
        return landed;
    }

    private Collider[] OverlapHitVolume()
    {
        Vector3 origin = HitOrigin();
        Vector3 halfHeight = Vector3.up * (attackHeight * 0.5f);
        return Physics.OverlapCapsule(origin - halfHeight, origin + halfHeight, attackRange);
    }

    private Vector3 HitOrigin()
    {
        return transform.position + transform.forward * hitForwardOffset + Vector3.up * hitVerticalOffset;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = gizmoColor;
        Vector3 origin = HitOrigin();
        Vector3 halfHeight = Vector3.up * (attackHeight * 0.5f);
        Gizmos.DrawWireSphere(origin - halfHeight, attackRange);
        if (attackHeight > 0f)
            Gizmos.DrawWireSphere(origin + halfHeight, attackRange);
    }
}
