using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private PlayerUi playerUi;

    public void TakeDamage(float damage)
    {
        Debug.Log($"[PlayerHealth] TakeDamage({damage}), playerUi={(playerUi == null ? "NULL" : playerUi.name)}");
        if (playerUi != null)
            playerUi.TakeDamage(damage);
    }
}
