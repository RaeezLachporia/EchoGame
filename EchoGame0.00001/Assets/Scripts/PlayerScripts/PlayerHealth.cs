using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable, IHealable
{
    [SerializeField] private PlayerUi playerUi;

    // Health actually lives in PlayerUi — this just forwards, same as TakeDamage.
    public float CurrentHealth => playerUi != null ? playerUi.CurrentHealth : 0f;
    public float MaxHealth => playerUi != null ? playerUi.MaxHealth : 0f;

    public void TakeDamage(float damage)
    {
        Debug.Log($"[PlayerHealth] TakeDamage({damage}), playerUi={(playerUi == null ? "NULL" : playerUi.name)}");
        if (playerUi != null)
            playerUi.TakeDamage(damage);
    }

    public void Heal(float amount)
    {
        if (playerUi != null)
            playerUi.Heal(amount);
    }
}
