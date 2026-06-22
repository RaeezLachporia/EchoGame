using UnityEngine;

public class PlayerUi : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    [Header("UI")]
    [SerializeField] private HealthBarUi healthBar;

    void Start()
    {
        if (healthBar != null)
            healthBar.Initialize(maxHealth, currentHealth);
    }

    public void SetHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        if (healthBar != null) healthBar.SetHealth(currentHealth);
    }

    public void SetMaxHealth(float value, bool refill = true)
    {
        maxHealth = Mathf.Max(0f, value);
        if (refill) currentHealth = maxHealth;
        if (healthBar != null) healthBar.SetMaxHealth(maxHealth, refill);
    }
}
