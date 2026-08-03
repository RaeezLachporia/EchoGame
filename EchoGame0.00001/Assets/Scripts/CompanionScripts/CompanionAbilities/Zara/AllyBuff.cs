using UnityEngine;

// A timed damage-resistance buff sitting on an ally (companion or player).
//
// The mirror image of EnemyDebuff: where that amplifies incoming damage on an
// enemy, this reduces it on a friend. Allies have TWO damage entry points rather
// than the enemies' one, so both Comapnion.TakeDamage and PlayerHealth.TakeDamage
// ask this for a multiplier before applying a hit.
//
// The applying ability (ZaraBuffAlly) adds this at runtime, so ally prefabs need
// no wiring — the component removes itself once the buff expires.
public class AllyBuff : MonoBehaviour, IStatusEffect
{
    private float damageMultiplier = 1f;
    private float expiresAt;
    private float totalDuration;
    private bool logBuff;

    // Incoming damage is multiplied by this. 0.6 = takes 60% of the damage,
    // i.e. 40% resistance.
    public float DamageMultiplier => damageMultiplier;
    public float SecondsRemaining => Mathf.Max(0f, expiresAt - Time.time);

    // IStatusEffect — status panels show the buff icon and wind it down off these.
    public bool IsActive => Time.time < expiresAt;
    public StatusEffectKind Kind => StatusEffectKind.Buff;
    public float RemainingNormalized =>
        totalDuration <= 0f ? 0f : Mathf.Clamp01(SecondsRemaining / totalDuration);

    // Re-applying refreshes the timer and keeps the STRONGER resistance (the lower
    // multiplier), so a second buff landing on the same ally can never weaken the
    // first one.
    public void Apply(float damageReduction, float duration, bool log)
    {
        damageMultiplier = Mathf.Min(damageMultiplier, Mathf.Clamp01(1f - damageReduction));
        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
        // Track the longest duration applied so the countdown measures against the
        // full bar rather than whatever was left when it refreshed.
        totalDuration = Mathf.Max(totalDuration, duration);
        logBuff = log;
        if (logBuff)
            Debug.Log($"[AllyBuff] {name} is now BUFFED — incoming damage x{damageMultiplier:F2} " +
                      $"({(1f - damageMultiplier) * 100f:F0}% resist) for {SecondsRemaining:F1}s.", this);
    }

    void Update()
    {
        if (Time.time < expiresAt) return;
        if (logBuff)
            Debug.Log($"[AllyBuff] {name} buff EXPIRED — damage back to normal.", this);
        Destroy(this);
    }

    // Companions can be destroyed and respawned; never let a reused object wake up
    // still carrying resistance from a previous life.
    void OnDisable()
    {
        damageMultiplier = 1f;
        expiresAt = 0f;
        totalDuration = 0f;
    }
}
