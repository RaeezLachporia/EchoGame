using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable, IHealable
{
    [SerializeField] private PlayerUi playerUi;

    // Health actually lives in PlayerUi — this just forwards, same as TakeDamage.
    public float CurrentHealth => playerUi != null ? playerUi.CurrentHealth : 0f;
    public float MaxHealth => playerUi != null ? playerUi.MaxHealth : 0f;

    // Placeholder until the downed-but-revivable state exists. Same shape as
    // Comapnion.IsDown — brain party-emergency triggers can read this today and
    // start firing correctly the moment the real downed state is wired in.
    public bool IsDown => false;

    public void TakeDamage(float damage)
    {
        // A damage-resistance buff (Zara's) scales the hit down before it lands.
        // The player's health lives in PlayerUi, so resist here — on the object the
        // buff is actually attached to — then forward the reduced amount.
        float incoming = damage;
        AllyBuff buff = GetComponent<AllyBuff>();
        if (buff != null && buff.DamageMultiplier < 1f)
        {
            incoming = damage * buff.DamageMultiplier;
            Debug.Log($"[PlayerHealth] BUFFED hit: {damage} x{buff.DamageMultiplier:F2} = {incoming} damage ({buff.SecondsRemaining:F1}s of buff left).");
        }
        else
        {
            Debug.Log($"[PlayerHealth] TakeDamage({incoming}), playerUi={(playerUi == null ? "NULL" : playerUi.name)}");
        }

        if (playerUi != null)
            playerUi.TakeDamage(incoming);
    }

    public void Heal(float amount)
    {
        if (playerUi != null)
            playerUi.Heal(amount);
    }
}
