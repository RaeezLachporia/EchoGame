using UnityEngine;
using UnityEngine.UI;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float currentHealth = 50f;

    [Header("UI (optional)")]
    [SerializeField] private Slider healthSlider;

    private EnemyFollowPlayer follow;
    private bool isDead;

    // EnemyFollowPlayer.EnterCombat checks this so a killing blow can't rally
    // the room — a takedown that drops an enemy should stay silent.
    public bool IsAlive => !isDead;

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

        currentHealth = Mathf.Max(0f, currentHealth - damage);
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
