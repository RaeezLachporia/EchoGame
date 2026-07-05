using UnityEngine;

// Per-companion hit volume — different companions get different reach, height,
// or offset. Kept separate from CompanionCommand so we can swap the box shape
// on a prefab without touching the attack orchestration (chase / cooldown / anim).
public class CompanionAttackBox : MonoBehaviour
{
    [Header("Hit Volume")]
    [Tooltip("Radius of the overlap capsule / sphere used to find things to damage on a swing.")]
    [SerializeField] private float attackRange = 1.5f;
    [Tooltip("How far in front of the companion the hit volume is centered.")]
    [SerializeField] private float hitForwardOffset = 0.8f;
    [Tooltip("Vertical offset of the hit volume from the companion's origin.")]
    [SerializeField] private float hitVerticalOffset = 0.5f;
    [Tooltip("Vertical extent of the hit volume (capsule height). 0 = plain sphere of radius attackRange.")]
    [SerializeField] private float attackHeight = 0f;

    [Header("Targeting")]
    [Tooltip("Colliders with this tag are the only ones the swing damages. Default 'Enemy'.")]
    [SerializeField] private string enemyTag = "Enemy";

    [Header("Debug")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 1f, 0.4f, 0.8f);

    // Called by CompanionCommand from its DealDamage animation event. Returns
    // the number of enemies that took damage so the caller can log / react.
    public int TryDealDamage(float damage)
    {
        Collider[] hits = OverlapHitVolume();
        int landed = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            if (!hits[i].CompareTag(enemyTag)) continue;
            // GetComponentInParent so child colliders (weapon meshes, hitboxes)
            // still resolve back to the enemy root that holds the health.
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
