using UnityEngine;
using UnityEngine.AI;

// Naledi's healing. She watches the team's health and runs over to heal
// whoever is hurt. If the player orders her to ATTACK, she drops the heal and
// goes back to healing afterwards. No heal animation needed yet — casting is
// just a short stand-still pause for now.
[RequireComponent(typeof(NavMeshAgent))]
public class NalediHealing : CompanionAbility
{
    [Header("Who To Heal")]
    [Tooltip("On = she heals hurt allies on her own when you haven't commanded her (anyone below Heal Threshold). Off = she only heals who you pick on the command wheel. A wheel-issued heal always works either way.")]
    [SerializeField] private bool autoHealWhenIdle = true;
    [Tooltip("Idle auto-heal only: she only steps in on her own once an ally drops below this — an emergency net under your wheel picks. 0.3 = below 30% health. She can heal herself too. Ignored when Auto Heal When Idle is off.")]
    [SerializeField, Range(0f, 1f)] private float healThreshold = 0.3f;
    [Tooltip("Tick = she heals the player too. Untick = companions only.")]
    [SerializeField] private bool healPlayer = true;
    [Tooltip("How often (in seconds) she checks if anyone is hurt.")]
    [SerializeField] private float scanInterval = 0.5f;

    [Header("Healing")]
    [Tooltip("How much health one heal gives back.")]
    [SerializeField] private float healAmount = 30f;
    [Tooltip("Seconds she has to wait between heals.")]
    [SerializeField] private float healCooldown = 4f;
    [Tooltip("How close she walks to stand next to the ally before healing — she runs right up to them instead of healing from across the gap. Smaller = closer. Keep it under her follow distance or she'll heal without stepping in.")]
    [SerializeField] private float approachDistance = 1.2f;
    [Tooltip("How long one heal takes. She stands still while casting and the health arrives at the end.")]
    [SerializeField] private float castDuration = 0.8f;

    [Header("Movement")]
    [Tooltip("How fast she runs to a hurt ally.")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float rotationSpeed = 10f;
    [Tooltip("The speed her run animation was made for — tweak until her feet stop sliding.")]
    [SerializeField] private float moveAnimSpeed = 4f;

    [Header("Animation")]
    [Tooltip("Can be left empty — it's found automatically. Healing works fine with no animation at all.")]
    [SerializeField] private Animator animator;
    [Tooltip("Name of the animation trigger for her heal. Only used once a heal animation exists in the Animator.")]
    [SerializeField] private string healTrigger = "Heal";
    [SerializeField] private float animationDampTime = 0.1f;

    [Header("Debug")]
    [Tooltip("Tick to see in the Console who she picks, when she casts, and how much she heals.")]
    [SerializeField] private bool logHealing = false;

    private NavMeshAgent agent;
    private CompanionCommand command;
    private PlayerHealth playerHealth;
    private IHealable target;
    private Transform targetTransform;
    private float scanTimer;
    private float cooldownRemaining;
    private float castRemaining;
    private bool isCasting;
    private bool hasHealTrigger;
    private int healTriggerHash;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public override bool IsBusy => target != null || isCasting;
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);

    // The player picks who she heals from the ally wheel — TryActivate below is fed
    // the chosen ally instead of the reticle enemy. The auto-heal in Update still
    // runs as the idle fallback when she hasn't been commanded.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.AllyPicker;

    void Reset()
    {
        abilityName = "Heal";
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        command = GetComponent<CompanionCommand>();
        if (animator == null) animator = GetComponent<Animator>();
        healTriggerHash = Animator.StringToHash(healTrigger);
        hasHealTrigger = animator != null && HasAnimatorParameter(animator, healTrigger);
    }

    void Start()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null) playerHealth = playerObj.GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

        // An attack order from the player beats healing — drop everything.
        if (command != null && command.HasActiveCommand)
        {
            // Clear a queued heal trigger too, so the heal animation doesn't
            // sneak out later while she's off attacking.
            if (isCasting && hasHealTrigger) animator.ResetTrigger(healTriggerHash);
            isCasting = false;
            DropTarget();
            return;
        }

        if (isCasting)
        {
            // Stand still while casting and keep facing the ally.
            if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
            FaceTarget();
            UpdateAnimation();
            castRemaining -= Time.deltaTime;
            if (castRemaining <= 0f) FinishCast();
            return;
        }

        if (target == null)
        {
            // No commanded target. Only go looking for someone to heal on our own if
            // the idle auto-heal is enabled — otherwise wait for a wheel command.
            if (!autoHealWhenIdle) return;
            scanTimer -= Time.deltaTime;
            if (scanTimer > 0f) return;
            scanTimer = scanInterval;
            FindWoundedAlly(healThreshold);
            if (target == null) return;
        }

        // Ally died, disappeared, or is back to full health — stop chasing them.
        if (!TargetStillValid())
        {
            DropTarget();
            return;
        }

        if (!agent.isOnNavMesh) return;

        Vector3 toAlly = targetTransform.position - transform.position;
        toAlly.y = 0f;

        if (toAlly.magnitude > approachDistance)
        {
            agent.speed = moveSpeed;
            agent.SetDestination(targetTransform.position);
        }
        else
        {
            if (agent.hasPath) agent.ResetPath();
            if (cooldownRemaining <= 0f) StartCast();
        }

        FaceTarget();
        UpdateAnimation();
    }

    // Runs when the player picks HEAL on the command wheel. The ally wheel hands us
    // the specific ally the player chose — honour it: heal if they're hurt, decline
    // if they're already full (the wheel keeps the picker open to choose again).
    public override bool TryActivate(Transform wheelTarget)
    {
        if (wheelTarget != null)
        {
            IHealable healable = wheelTarget.GetComponentInParent<IHealable>();
            if (healable != null)
            {
                if (healable.CurrentHealth < healable.MaxHealth)
                {
                    SetTarget(healable, wheelTarget);
                    return true;
                }
                if (logHealing) Debug.Log($"[NalediHealing] {wheelTarget.name} is already at full health — not healing.", this);
                return false;
            }
        }

        // No specific ally passed (legacy / non-ally target) — auto-pick whoever's hurt.
        FindWoundedAlly(1f);
        if (logHealing && target == null) Debug.Log("[NalediHealing] Heal commanded but everyone is at full health.", this);
        return target != null;
    }

    private void FindWoundedAlly(float threshold)
    {
        IHealable best = null;
        Transform bestTransform = null;
        float bestFraction = threshold;

        // This list includes Naledi herself, so she can heal herself too.
        foreach (Comapnion companion in FindObjectsOfType<Comapnion>())
            Consider(companion, companion.transform, ref best, ref bestTransform, ref bestFraction);

        if (healPlayer && playerHealth != null)
            Consider(playerHealth, playerHealth.transform, ref best, ref bestTransform, ref bestFraction);

        if (best != null) SetTarget(best, bestTransform);
    }

    private static void Consider(IHealable candidate, Transform candidateTransform,
        ref IHealable best, ref Transform bestTransform, ref float bestFraction)
    {
        if (candidate.MaxHealth <= 0f) return;
        float fraction = candidate.CurrentHealth / candidate.MaxHealth;
        if (fraction >= bestFraction) return;
        best = candidate;
        bestTransform = candidateTransform;
        bestFraction = fraction;
    }

    private void SetTarget(IHealable healable, Transform healableTransform)
    {
        target = healable;
        targetTransform = healableTransform;
        if (logHealing) Debug.Log($"[NalediHealing] Healing target → {healableTransform.name}", this);
    }

    private void DropTarget()
    {
        if (target == null) return;
        target = null;
        targetTransform = null;
        if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    private bool TargetStillValid()
    {
        return targetTransform != null
            && targetTransform.gameObject.activeInHierarchy
            && target.CurrentHealth < target.MaxHealth;
    }

    private void StartCast()
    {
        isCasting = true;
        castRemaining = castDuration;
        cooldownRemaining = healCooldown;
        if (hasHealTrigger) animator.SetTrigger(healTriggerHash);
        if (logHealing) Debug.Log($"[NalediHealing] Casting heal on {targetTransform.name}.", this);
    }

    private void FinishCast()
    {
        isCasting = false;
        if (targetTransform == null) return;
        target.Heal(healAmount);
        if (logHealing) Debug.Log($"[NalediHealing] Healed {targetTransform.name} for {healAmount} → {target.CurrentHealth}/{target.MaxHealth}.", this);
        if (target.CurrentHealth >= target.MaxHealth) DropTarget();
    }

    private void FaceTarget()
    {
        if (targetTransform == null) return;
        Vector3 dir = targetTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), rotationSpeed * Time.deltaTime);
    }

    private void UpdateAnimation()
    {
        // The follow/wander scripts pause while she's healing, so this script
        // has to update her animation itself — otherwise she'd slide around frozen.
        if (animator == null) return;
        float speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);
        animator.speed = !isCasting && speed > 0.1f ? speed / moveAnimSpeed : 1f;
    }

    private static bool HasAnimatorParameter(Animator a, string paramName)
    {
        AnimatorControllerParameter[] parameters = a.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == paramName) return true;
        return false;
    }
}
