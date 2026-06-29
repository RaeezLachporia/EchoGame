using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthBarUi : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private GameObject root;

    private float maxHealth;
    private float currentHealth;

    void Awake()
    {
        if (root == null) root = gameObject;
        if (slider != null) slider.interactable = false;
    }

    public void Initialize(float max, float current)
    {
        maxHealth = Mathf.Max(0f, max);
        currentHealth = Mathf.Clamp(current, 0f, maxHealth);
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = maxHealth;
            slider.value = currentHealth;
        }
        Refresh();
    }

    public void SetHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        if (slider != null) slider.value = currentHealth;
        Refresh();
    }

    public void SetMaxHealth(float value, bool refill = true)
    {
        maxHealth = Mathf.Max(0f, value);
        if (slider != null) slider.maxValue = maxHealth;
        if (refill) currentHealth = maxHealth;
        if (slider != null) slider.value = currentHealth;
        Refresh();
    }

    public void SetName(string displayName)
    {
        if (nameLabel != null) nameLabel.text = displayName;
    }

    public void Show() { if (root != null) root.SetActive(true); }
    public void Hide() { if (root != null) root.SetActive(false); }

    void Refresh()
    {
        if (label != null)
            label.text = Mathf.CeilToInt(currentHealth) + " / " + Mathf.CeilToInt(maxHealth);
    }
}
