using UnityEngine;

// Zara's enemy debuff: marks one enemy so every hit from the player AND the
// companions lands harder for a while.
//
// Targeting comes from the command wheel's enemy-cycle layer (TargetKind =
// EnemyPicker): the player cycles nearby enemies with the d-pad and confirms,
// and the wheel hands the chosen enemy to TryActivate below.
//
// The debuff itself lives on the enemy as a runtime EnemyDebuff component, which
// EnemyHealth reads when damage arrives — so this script never has to know about
// who is swinging.
public class ZaraDebuffEnemy : CompanionAbility
{
    [Header("Debuff")]
    [Tooltip("Incoming damage multiplier while the debuff is on the enemy. 1.5 = the enemy takes 50% more damage from every hit, by anyone.")]
    [SerializeField, Min(1f)] private float damageMultiplier = 1.5f;
    [Tooltip("How long the debuff lasts, in seconds. Re-applying refreshes it.")]
    [SerializeField, Min(0.1f)] private float duration = 8f;
    [Tooltip("Seconds before Zara can debuff again.")]
    [SerializeField, Min(0f)] private float cooldown = 6f;

    [Header("Animation")]
    [Tooltip("Optional. Animator trigger fired when she debuffs. Only used if a parameter with this name exists — safe to leave as-is with no animation.")]
    [SerializeField] private string debuffTrigger = "Debuff";
    [SerializeField] private Animator animator;

    [Header("Debug")]
    [Tooltip("Log when the debuff is applied, refused, or expires. Pair with EnemyHealth's Log Damage to watch hits get amplified.")]
    [SerializeField] private bool logDebuff = true;

    private float cooldownRemaining;
    private bool hasDebuffTrigger;
    private int debuffTriggerHash;

    // Opens the wheel's enemy-cycle layer instead of firing on the reticle.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.EnemyPicker;
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);

    void Reset()
    {
        abilityName = "Debuff";
    }

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        debuffTriggerHash = Animator.StringToHash(debuffTrigger);
        hasDebuffTrigger = animator != null && HasAnimatorParameter(animator, debuffTrigger);
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;
    }

    // The wheel passes the enemy the player confirmed on the cycle layer. Cast is
    // instant and at range — she marks the target without walking to it.
    public override bool TryActivate(Transform target)
    {
        if (target == null)
        {
            if (logDebuff) Debug.Log("[ZaraDebuffEnemy] No enemy selected — nothing to debuff.", this);
            return false;
        }

        if (cooldownRemaining > 0f)
        {
            if (logDebuff) Debug.Log($"[ZaraDebuffEnemy] Still on cooldown — {cooldownRemaining:F1}s left.", this);
            return false;
        }

        // GetComponentInParent so a hit on a child collider or weapon mesh still
        // resolves up to the enemy root that owns the health.
        EnemyHealth health = target.GetComponentInParent<EnemyHealth>();
        if (health == null)
        {
            if (logDebuff) Debug.Log($"[ZaraDebuffEnemy] '{target.name}' has no EnemyHealth — can't debuff it.", this);
            return false;
        }

        EnemyDebuff debuff = health.GetComponent<EnemyDebuff>();
        if (debuff == null) debuff = health.gameObject.AddComponent<EnemyDebuff>();
        debuff.Apply(damageMultiplier, duration, logDebuff);

        cooldownRemaining = cooldown;
        if (hasDebuffTrigger) animator.SetTrigger(debuffTriggerHash);

        if (logDebuff)
            Debug.Log($"[ZaraDebuffEnemy] {name} DEBUFFED {health.name} — x{damageMultiplier:F2} damage for {duration}s. Hit it now and watch the damage log.", this);
        return true;
    }

    private static bool HasAnimatorParameter(Animator a, string paramName)
    {
        AnimatorControllerParameter[] parameters = a.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == paramName) return true;
        return false;
    }
}
