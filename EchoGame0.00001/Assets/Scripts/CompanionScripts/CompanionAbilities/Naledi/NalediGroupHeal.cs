using UnityEngine;

// Naledi's group heal ("Pulse"): stands still, channels for castTime, then heals
// every hurt ally within radius in one burst. The reactive counterpart to her
// single-target NalediHealing — one is a picked-target save, this is the "burst
// damage just hit the whole party" answer.
//
// Fires immediately from the wheel with no picker (TargetKind = Self), same
// shape as LaylaTaunt. Cast bar shows automatically via IsCasting/CastProgress,
// picked up by the existing CompanionUI cast bar — no UI wiring needed.
//
// Deliberately no walk-in, no line-of-sight check, and no idle auto-fire. The
// radius is the tuning knob for "get in the middle of the fight before pressing".
public class NalediGroupHeal : CompanionAbility
{
    [Header("Area")]
    [Tooltip("Metres around Naledi that count as \"in the pulse\". She's at the centre so she's always in range.")]
    [SerializeField, Range(1f, 30f)] private float radius = 8f;
    [Tooltip("Tick = the player counts as an ally and gets healed too. Untick = companions only.")]
    [SerializeField] private bool healPlayer = true;

    [Header("Heal")]
    [Tooltip("How much health each hurt ally in range gets back. Overheal is clamped at max on the IHealable side, so a big value can't push anyone past their cap.")]
    [SerializeField, Range(0f, 100f)] private float healAmount = 25f;
    [Tooltip("Seconds before Group Heal can be used again, counted from the moment the cast finishes.")]
    [SerializeField, Min(0f)] private float cooldown = 18f;

    [Header("Cast")]
    [Tooltip("Seconds she stands still channelling before the burst lands. The cast bar fills over this time.")]
    [SerializeField, Range(0f, 3f)] private float castTime = 1f;

    [Header("Animation")]
    [Tooltip("Optional. Animator trigger fired when the cast starts — safe to leave as \"Heal\" so it reuses her existing trigger, or point it at a dedicated AoE animation once one exists.")]
    [SerializeField] private string healTrigger = "Heal";
    [SerializeField] private Animator animator;

    [Header("Debug")]
    [Tooltip("Log the cast starting, landing, being interrupted, or refused because everyone was full.")]
    [SerializeField] private bool logHeal = true;

    private CompanionCommand command;
    private PlayerHealth playerHealth;
    private float castRemaining;
    private float cooldownRemaining;
    private bool isCasting;
    private bool hasHealTrigger;
    private int healTriggerHash;

    // Fires immediately from the wheel — no picker, targets are found in-code.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.Self;
    // Busy while channelling, so follow / wander / brain yield the agent to us
    // and she can't be dragged around mid-cast.
    public override bool IsBusy => isCasting;
    public override bool IsCasting => isCasting;
    // 0 when the cast starts, 1 when it completes — CompanionUI reads this to
    // fill the cast bar. Same shape as ZaraBuffAlly.
    public override float CastProgress =>
        !isCasting ? 0f : (castTime <= 0f ? 1f : Mathf.Clamp01(1f - castRemaining / castTime));
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);

    void Reset()
    {
        abilityName = "Group Heal";
    }

    void Awake()
    {
        command = GetComponent<CompanionCommand>();
        if (animator == null) animator = GetComponent<Animator>();
        healTriggerHash = Animator.StringToHash(healTrigger);
        hasHealTrigger = animator != null && HasAnimatorParameter(animator, healTrigger);
    }

    void Start()
    {
        // Same lookup NalediHealing uses so the two abilities agree on who the
        // player is even if the tag setup changes.
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null) playerHealth = playerObj.GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;
        if (!isCasting) return;

        // A PLAYER-issued attack beats the heal — but a brain-issued one must not,
        // or an aggressive brain would cancel the cast the moment it has a target.
        // Matches NalediHealing / ZaraBuffAlly.
        if (command != null && command.HasPlayerCommand)
        {
            CancelCast("a player attack order came in");
            return;
        }

        castRemaining -= Time.deltaTime;
        if (castRemaining <= 0f) FinishCast();
    }

    // Fired by the wheel the instant the slice is pressed. Refuses on cooldown, and
    // refuses if there's nobody to heal — don't burn the cooldown on a wasted press.
    public override bool TryActivate(Transform ignored)
    {
        if (cooldownRemaining > 0f)
        {
            if (logHeal) Debug.Log($"[NalediGroupHeal] Still on cooldown — {cooldownRemaining:F1}s left.", this);
            return false;
        }

        // Wheel finds abilities via GetComponents, which returns DISABLED ones too.
        // Accepting a cast here would freeze it forever: isCasting gets set so the
        // bar shows, but Update never runs, so the timer never advances and the
        // burst never lands. Same guard ZaraBuffAlly uses.
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning($"[NalediGroupHeal] The component on '{name}' is disabled, so its cast could never tick. Tick its checkbox in the Inspector.", this);
            return false;
        }

        if (!AnyHurtAllyInRange())
        {
            if (logHeal) Debug.Log("[NalediGroupHeal] Everyone in range is at full health — not casting.", this);
            return false;
        }

        isCasting = true;
        castRemaining = castTime;
        if (hasHealTrigger) animator.SetTrigger(healTriggerHash);
        if (logHeal) Debug.Log($"[NalediGroupHeal] {name} started a {castTime}s group heal cast.", this);

        // Zero cast time means "land now" — don't wait a frame.
        if (castRemaining <= 0f) FinishCast();
        return true;
    }

    private void FinishCast()
    {
        isCasting = false;
        castRemaining = 0f;
        cooldownRemaining = cooldown;

        int healed = 0;
        float sqrRadius = radius * radius;
        Vector3 origin = transform.position;

        // Companions first. Comapnion.Active is the static registry — preferred
        // over FindObjectsOfType per the companion-conventions memory, and it
        // includes Naledi herself so she gets the burst too.
        System.Collections.Generic.IReadOnlyList<Comapnion> companions = Comapnion.Active;
        for (int i = 0; i < companions.Count; i++)
        {
            Comapnion c = companions[i];
            if (c == null || !c.gameObject.activeInHierarchy) continue;
            if ((c.transform.position - origin).sqrMagnitude > sqrRadius) continue;
            if (TryHealOne(c, c.name)) healed++;
        }

        if (healPlayer && playerHealth != null && playerHealth.gameObject.activeInHierarchy)
        {
            if ((playerHealth.transform.position - origin).sqrMagnitude <= sqrRadius)
                if (TryHealOne(playerHealth, playerHealth.name)) healed++;
        }

        if (logHeal)
            Debug.Log($"[NalediGroupHeal] {name} group-healed {healed} ally(s) for up to {healAmount:F0}.", this);
    }

    // Only counts a heal if the ally was actually below full — matches how
    // NalediHealing's targeted version refuses full-health picks. Heal itself
    // clamps at max on the IHealable side, so this filter is about the count and
    // log message rather than damage safety.
    private bool TryHealOne(IHealable ally, string displayName)
    {
        if (ally.MaxHealth <= 0f) return false;
        if (ally.CurrentHealth >= ally.MaxHealth) return false;
        ally.Heal(healAmount);
        if (logHeal)
            Debug.Log($"[NalediGroupHeal]   +{healAmount:F0} → {displayName} ({ally.CurrentHealth:F0}/{ally.MaxHealth:F0})", this);
        return true;
    }

    // The refuse-on-nobody-hurt scan. Same walk as FinishCast, but returns the
    // moment it finds one — cheap even with a big party.
    private bool AnyHurtAllyInRange()
    {
        float sqrRadius = radius * radius;
        Vector3 origin = transform.position;

        System.Collections.Generic.IReadOnlyList<Comapnion> companions = Comapnion.Active;
        for (int i = 0; i < companions.Count; i++)
        {
            Comapnion c = companions[i];
            if (c == null || !c.gameObject.activeInHierarchy) continue;
            if (c.MaxHealth <= 0f || c.CurrentHealth >= c.MaxHealth) continue;
            if ((c.transform.position - origin).sqrMagnitude <= sqrRadius) return true;
        }

        if (healPlayer && playerHealth != null && playerHealth.gameObject.activeInHierarchy
            && playerHealth.MaxHealth > 0f && playerHealth.CurrentHealth < playerHealth.MaxHealth
            && (playerHealth.transform.position - origin).sqrMagnitude <= sqrRadius)
            return true;

        return false;
    }

    private void CancelCast(string reason)
    {
        if (logHeal && isCasting)
            Debug.Log($"[NalediGroupHeal] Cast interrupted — {reason}. Cooldown NOT applied.", this);
        // Clear a queued trigger so the heal animation doesn't sneak out later
        // while she's already off doing something else.
        if (hasHealTrigger) animator.ResetTrigger(healTriggerHash);
        isCasting = false;
        castRemaining = 0f;
    }

    private static bool HasAnimatorParameter(Animator a, string paramName)
    {
        AnimatorControllerParameter[] parameters = a.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == paramName) return true;
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
