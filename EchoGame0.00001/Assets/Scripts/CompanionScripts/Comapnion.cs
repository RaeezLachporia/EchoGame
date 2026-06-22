using UnityEngine;

public class Comapnion : MonoBehaviour, IDamageable
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Companion";

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    private int hudSlot = -1;

    void Start()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        if (CompanionHealthHud.Instance != null)
            hudSlot = CompanionHealthHud.Instance.Claim(this, displayName, maxHealth, currentHealth);
    }

    void Update()
    {

    }

    public void TakeDamage(float damage)
    {
        currentHealth = Mathf.Max(0f, currentHealth - damage);
        if (CompanionHealthHud.Instance != null)
            CompanionHealthHud.Instance.SetHealth(hudSlot, currentHealth);

        if (currentHealth <= 0f)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (CompanionHealthHud.Instance != null)
            CompanionHealthHud.Instance.Release(hudSlot);
    }
}
