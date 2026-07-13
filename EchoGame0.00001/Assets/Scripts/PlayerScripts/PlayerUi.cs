using UnityEngine;
using UnityEngine.UI;

public class PlayerUi : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    [Header("UI")]
    [SerializeField] private HealthBarUi healthBar;
    [SerializeField] private Slider healthSlider;

    void Start()
    {
        if (healthBar != null)
            healthBar.Initialize(maxHealth, currentHealth);

        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = maxHealth;
            healthSlider.value = currentHealth;
            healthSlider.interactable = false;
        }
    }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    public void TakeDamage(float damage)
    {
        Debug.Log($"[PlayerUi] TakeDamage({damage}), currentHealth {currentHealth} -> {currentHealth - damage}");
        SetHealth(currentHealth - damage);
    }

    public void Heal(float amount)
    {
        SetHealth(currentHealth + amount);
    }

    public void SetHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        if (healthBar != null) healthBar.SetHealth(currentHealth);
        if (healthSlider != null) healthSlider.value = currentHealth;
    }

    public void SetMaxHealth(float value, bool refill = true)
    {
        maxHealth = Mathf.Max(0f, value);
        if (refill) currentHealth = maxHealth;
        if (healthBar != null) healthBar.SetMaxHealth(maxHealth, refill);
        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHealth;
            healthSlider.value = currentHealth;
        }
    }
}
