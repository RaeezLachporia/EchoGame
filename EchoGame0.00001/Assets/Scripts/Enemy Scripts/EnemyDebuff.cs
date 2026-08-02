using UnityEngine;

// A timed damage-amplification debuff sitting on an enemy.
//
// EnemyHealth asks this for a multiplier on every incoming hit, which is why ONE
// hook covers every attacker: player swings (PlayerBasicCombat), companion attack
// boxes (CompanionAttackBox), and companion direct hits (CompanionCommand) all
// route their damage through EnemyHealth.TakeDamage.
//
// The applying ability (e.g. ZaraDebuffEnemy) adds this at runtime, so enemy
// prefabs need no wiring — the component removes itself once the debuff expires.
public class EnemyDebuff : MonoBehaviour, IStatusEffect
{
    private float multiplier = 1f;
    private float expiresAt;
    private bool logDebuff;
    private EnemyStatusIcons statusIcons;

    void Awake()
    {
        // Resolved here rather than on demand: Awake runs the moment AddComponent
        // creates us, so the icon is ready before the first Apply call lands.
        // Include-inactive, since the panel may start hidden.
        statusIcons = GetComponentInChildren<EnemyStatusIcons>(true);
    }

    // What incoming damage gets multiplied by while this is active.
    public float Multiplier => multiplier;
    public float SecondsRemaining => Mathf.Max(0f, expiresAt - Time.time);

    // IStatusEffect — lets status-aware UI ask "is anything running on this
    // character?" without knowing what a damage debuff is.
    public bool IsActive => Time.time < expiresAt;

    // Re-applying refreshes the timer and keeps the STRONGER multiplier, so a
    // second debuff landing on the same enemy can never weaken the first one.
    public void Apply(float damageMultiplier, float duration, bool log)
    {
        multiplier = Mathf.Max(multiplier, damageMultiplier);
        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
        logDebuff = log;
        if (statusIcons != null) statusIcons.SetDebuffVisible(true);
        if (logDebuff)
            Debug.Log($"[EnemyDebuff] {name} is now DEBUFFED — incoming damage x{multiplier:F2} for {SecondsRemaining:F1}s.", this);
    }

    void Update()
    {
        if (Time.time < expiresAt) return;
        if (logDebuff)
            Debug.Log($"[EnemyDebuff] {name} debuff EXPIRED — damage back to normal.", this);
        Destroy(this);
    }

    // Pooled enemies keep their components across lives — without this, a reused
    // enemy would wake up still carrying the debuff from its previous life.
    void OnDisable()
    {
        multiplier = 1f;
        expiresAt = 0f;
        if (statusIcons != null) statusIcons.SetDebuffVisible(false);
    }

    // Covers every way this component goes away — expiry above, or the enemy being
    // destroyed outright — so the icon can't outlive the debuff.
    void OnDestroy()
    {
        if (statusIcons != null) statusIcons.SetDebuffVisible(false);
    }
}
