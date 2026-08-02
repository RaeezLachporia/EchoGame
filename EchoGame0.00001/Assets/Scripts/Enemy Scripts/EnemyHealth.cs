using UnityEngine;
using UnityEngine.UI;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float currentHealth = 50f;

    [Header("UI (optional)")]
    [SerializeField] private Slider healthSlider;

    [Header("Debug")]
    [Tooltip("Log every hit this enemy takes, showing whether a debuff amplified it. Handy for confirming the debuff actually changes incoming damage.")]
    [SerializeField] private bool logDamage = true;

    private EnemyFollowPlayer follow;
    private bool isDead;

    void Awake()
    {
        follow = GetComponent<EnemyFollowPlayer>();
    }

    // OnEnable, not Start: a pooled enemy is re-activated rather than recreated, so
    // Start only ever runs on its first life. Without this it comes back at the 0
    // health it died with and dies again to the first hit.
    void OnEnable()
    {
        isDead = false;
        currentHealth = maxHealth;
        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = maxHealth;
            healthSlider.value = currentHealth;
            healthSlider.interactable = false;
        }
    }

    public void TakeDamage(float damage)
    {
        // Attacks resolve with an overlap check, so two colliders on this enemy can
        // both land in one frame. Both calls would see zero health and release, and
        // the pool's collectionCheck throws on the second.
        if (isDead) return;

        // Every attacker funnels through here — player swings, companion attack
        // boxes, and companion direct hits — so scaling the damage at this one
        // point makes a debuff apply to all of them without touching any of them.
        float incoming = damage;
        EnemyDebuff debuff = GetComponent<EnemyDebuff>();
        if (debuff != null && debuff.Multiplier > 1f)
        {
            incoming = damage * debuff.Multiplier;
            if (logDamage)
                Debug.Log($"[EnemyHealth] {name} DEBUFFED hit: {damage} x{debuff.Multiplier:F2} = {incoming} damage ({debuff.SecondsRemaining:F1}s of debuff left).", this);
        }
        else if (logDamage)
        {
            Debug.Log($"[EnemyHealth] {name} normal hit: {incoming} damage.", this);
        }

        currentHealth = Mathf.Max(0f, currentHealth - incoming);
        if (healthSlider != null) healthSlider.value = currentHealth;

        if (currentHealth <= 0f)
        {
            isDead = true;
            // ReturnToPool owns the pool-vs-destroy decision. Only fall back here if
            // this is on something that isn't a follow-enemy at all.
            if (follow != null) follow.ReturnToPool();
            else Destroy(gameObject);
        }
    }
}
