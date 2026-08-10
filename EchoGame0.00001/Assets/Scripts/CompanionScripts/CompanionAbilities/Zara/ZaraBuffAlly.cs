using UnityEngine;
using UnityEngine.AI;

// Zara's ally buff: shields one ally so every hit they take lands softer for a
// while. The other half of her Controller kit alongside ZaraDebuffEnemy.
//
// Targeting comes from the command wheel's ally layer (TargetKind = AllyPicker):
// the player picks a companion (Zara included, so she can shield herself) or the
// player via the dedicated button, and the wheel hands the ally to TryActivate.
//
// She's a caster: she does NOT walk to the ally. Instead she stands still for
// castTime and the buff lands at the end, so it can be seen coming and reacted to.
// The resistance itself lives on the ally as a runtime AllyBuff component, which
// Comapnion / PlayerHealth read when damage arrives.
public class ZaraBuffAlly : CompanionAbility
{
    [Header("Buff")]
    [Tooltip("How much incoming damage is removed while the buff is up. 0.4 = the ally takes 40% less damage from every hit.")]
    [SerializeField, Range(0f, 0.9f)] private float damageReduction = 0.4f;
    [Tooltip("How long the resistance lasts once it lands, in seconds. Re-applying refreshes it.")]
    [SerializeField, Min(0.1f)] private float buffDuration = 8f;
    [Tooltip("Seconds before Zara can buff again, counted from the moment the cast finishes.")]
    [SerializeField, Min(0f)] private float cooldown = 10f;

    [Header("Cast")]
    [Tooltip("Seconds she stands still channelling before the buff lands. The cast bar fills over this time.")]
    [SerializeField, Min(0f)] private float castTime = 4f;
    [Tooltip("How fast she turns to face the ally she's buffing.")]
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Animation")]
    [Tooltip("Optional. Animator trigger fired when the cast starts. Only used if a parameter with this name exists — safe to leave with no animation.")]
    [SerializeField] private string buffTrigger = "Buff";
    [SerializeField] private Animator animator;

    [Header("Debug")]
    [Tooltip("Log the cast starting, landing, being interrupted, or refused.")]
    [SerializeField] private bool logBuff = true;

    private NavMeshAgent agent;
    private CompanionCommand command;
    private Transform castTarget;
    private float castRemaining;
    private float cooldownRemaining;
    private bool hasBuffTrigger;
    private int buffTriggerHash;

    // Opens the wheel's ally layer instead of firing on the reticle.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.AllyPicker;
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);

    // Busy while channelling, so the follow and wander scripts yield and she holds
    // position instead of being dragged around mid-cast.
    public override bool IsBusy => castTarget != null;
    public override bool IsCasting => castTarget != null;
    public override float CastProgress =>
        castTarget == null ? 0f : (castTime <= 0f ? 1f : Mathf.Clamp01(1f - castRemaining / castTime));

    void Reset()
    {
        abilityName = "Buff";
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        command = GetComponent<CompanionCommand>();
        if (animator == null) animator = GetComponent<Animator>();
        buffTriggerHash = Animator.StringToHash(buffTrigger);
        hasBuffTrigger = animator != null && HasAnimatorParameter(animator, buffTrigger);
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;
        if (castTarget == null) return;

        // An attack order from the player beats a buff — drop the cast.
        if (command != null && command.HasActiveCommand)
        {
            CancelCast("an attack order came in");
            return;
        }

        // Ally died or was disabled mid-cast — nothing left to shield.
        if (!castTarget.gameObject.activeInHierarchy)
        {
            CancelCast("the target is gone");
            return;
        }

        // Hold position and keep facing them while channelling.
        if (agent != null && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
        FaceTarget();

        castRemaining -= Time.deltaTime;
        if (castRemaining <= 0f) FinishCast();
    }

    // The wheel passes the ally the player chose. This only STARTS the cast — the
    // resistance lands when the channel completes.
    public override bool TryActivate(Transform target)
    {
        if (target == null)
        {
            if (logBuff) Debug.Log("[ZaraBuffAlly] No ally selected — nothing to buff.", this);
            return false;
        }

        if (cooldownRemaining > 0f)
        {
            if (logBuff) Debug.Log($"[ZaraBuffAlly] Still on cooldown — {cooldownRemaining:F1}s left.", this);
            return false;
        }

        // The wheel finds abilities via GetComponents, which returns DISABLED ones
        // too. Accepting a cast here would freeze it forever: castTarget gets set so
        // the bar shows, but Update never runs, so the timer never advances and the
        // buff never lands. Refuse loudly instead.
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning($"[ZaraBuffAlly] The component on '{name}' is disabled, so its cast could never tick. Tick its checkbox in the Inspector.", this);
            return false;
        }

        // Only something that can actually take damage benefits from resistance.
        if (target.GetComponentInParent<IDamageable>() == null)
        {
            if (logBuff) Debug.Log($"[ZaraBuffAlly] '{target.name}' can't take damage, so a resistance buff would do nothing.", this);
            return false;
        }

        castTarget = target;
        castRemaining = castTime;
        if (hasBuffTrigger) animator.SetTrigger(buffTriggerHash);
        if (logBuff) Debug.Log($"[ZaraBuffAlly] {name} started a {castTime}s cast on {target.name}.", this);
        return true;
    }

    private void FinishCast()
    {
        Transform target = castTarget;
        castTarget = null;
        castRemaining = 0f;
        cooldownRemaining = cooldown;

        if (target == null) return;

        // Put the buff on the object that owns the health, so that same object's
        // TakeDamage finds it — landing it on a child collider would do nothing.
        Component health = target.GetComponentInParent<IDamageable>() as Component;
        GameObject host = health != null ? health.gameObject : target.gameObject;

        AllyBuff buff = host.GetComponent<AllyBuff>();
        if (buff == null) buff = host.AddComponent<AllyBuff>();
        buff.Apply(damageReduction, buffDuration, logBuff);

        if (logBuff)
            Debug.Log($"[ZaraBuffAlly] {name} BUFFED {host.name} — {damageReduction * 100f:F0}% damage resistance for {buffDuration}s.", this);
    }

    private void CancelCast(string reason)
    {
        if (logBuff && castTarget != null)
            Debug.Log($"[ZaraBuffAlly] Cast on {castTarget.name} interrupted — {reason}.", this);
        // Clear a queued trigger so the buff animation doesn't sneak out later while
        // she's already off doing something else.
        if (hasBuffTrigger) animator.ResetTrigger(buffTriggerHash);
        castTarget = null;
        castRemaining = 0f;
    }

    private void FaceTarget()
    {
        if (castTarget == null) return;
        Vector3 dir = castTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), rotationSpeed * Time.deltaTime);
    }

    private static bool HasAnimatorParameter(Animator a, string paramName)
    {
        AnimatorControllerParameter[] parameters = a.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == paramName) return true;
        return false;
    }
}
