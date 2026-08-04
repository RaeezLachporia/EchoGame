using UnityEngine;

// The temporary health boost Layla's taunt grants her.
//
// Same shape as Zara's AllyBuff: a runtime-added component that owns a timed
// effect and cleans itself up. The difference is that this one changes STATE
// (her max health) rather than being queried at damage time, so it has to revert
// what it did — hence the reverts in OnDestroy and OnDisable. A stranded bonus
// would silently inflate her max health for the rest of the run.
public class LaylaHealthBoost : MonoBehaviour, IStatusEffect
{
    private Comapnion body;
    private float appliedBonus;
    private float expiresAt;
    private float totalDuration;
    private bool logBoost;

    public float SecondsRemaining => Mathf.Max(0f, expiresAt - Time.time);

    // IStatusEffect — CompanionUI shows the buff icon and winds it down off these.
    public bool IsActive => Time.time < expiresAt;
    public StatusEffectKind Kind => StatusEffectKind.Buff;
    public float RemainingNormalized =>
        totalDuration <= 0f ? 0f : Mathf.Clamp01(SecondsRemaining / totalDuration);

    void Awake()
    {
        body = GetComponent<Comapnion>();
    }

    // Re-applying refreshes the timer WITHOUT stacking another bonus — taunting
    // again during the boost should extend it, not double her health each time.
    public void Apply(float bonus, float duration, bool log)
    {
        logBoost = log;
        if (body == null) return;

        if (appliedBonus <= 0f)
        {
            appliedBonus = bonus;
            body.ApplyMaxHealthBonus(appliedBonus);
        }

        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
        totalDuration = Mathf.Max(totalDuration, duration);

        if (logBoost)
            Debug.Log($"[LaylaHealthBoost] {name} health boosted by {appliedBonus} for {SecondsRemaining:F1}s " +
                      $"→ {body.CurrentHealth}/{body.MaxHealth}.", this);
    }

    void Update()
    {
        if (Time.time < expiresAt) return;
        if (logBoost) Debug.Log($"[LaylaHealthBoost] {name} health boost EXPIRED.", this);
        Destroy(this);
    }

    void OnDisable()
    {
        Revert();
    }

    void OnDestroy()
    {
        Revert();
    }

    // Idempotent: zeroing appliedBonus first means OnDisable followed by
    // OnDestroy can't take the bonus off twice.
    private void Revert()
    {
        if (appliedBonus <= 0f) return;
        float bonus = appliedBonus;
        appliedBonus = 0f;
        expiresAt = 0f;
        totalDuration = 0f;
        if (body != null) body.RemoveMaxHealthBonus(bonus);
    }
}
