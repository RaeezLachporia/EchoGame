using UnityEngine;

// Layla's tank opener: nearby enemies drop what they're doing and come for her,
// and she gains extra health to survive being the focus.
//
// Centred on herself, so it needs no target picker (TargetKind = Self) and the
// wheel fires it the moment the slice is pressed.
//
// Only enemies that are ALREADY engaged get pulled. Yanking an unaware patrol
// would blow a stealth approach the player never triggered — EnemyFollowPlayer
// enforces that in TryTaunt, and its PatrolTick still only ever watches for the
// player, so vision and aggro stay separate concerns.
public class LaylaTaunt : CompanionAbility
{
    [Header("Taunt")]
    [Tooltip("Enemies within this distance of Layla get pulled onto her. Keep it tight enough that she has to actually wade in.")]
    [SerializeField] private float tauntRadius = 15f;
    [Tooltip("How long enemies stay locked onto her before reverting to the player.")]
    [SerializeField] private float tauntDuration = 8f;
    [Tooltip("Seconds before Taunt can be used again.")]
    [SerializeField] private float cooldown = 25f;

    [Header("Health Boost")]
    [Tooltip("Extra max health for the duration, granted as real health immediately so it's usable the moment it lands.")]
    [SerializeField] private float healthBonus = 150f;
    [Tooltip("How long the health boost lasts. Usually matches the taunt so both end together.")]
    [SerializeField] private float boostDuration = 8f;

    [Header("Animation")]
    [Tooltip("Optional. Animator trigger fired on taunt. Only used if a parameter with this name exists — safe with no animation.")]
    [SerializeField] private string tauntTrigger = "Taunt";
    [SerializeField] private Animator animator;

    [Header("Debug")]
    [Tooltip("Log how many enemies were pulled and the health boost applied.")]
    [SerializeField] private bool logTaunt = true;

    private Comapnion body;
    private float cooldownRemaining;
    private bool hasTauntTrigger;
    private int tauntTriggerHash;

    // No picker — it goes off around her the instant the slice is pressed.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.Self;
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);

    void Reset()
    {
        abilityName = "Taunt";
    }

    void Awake()
    {
        body = GetComponent<Comapnion>();
        if (animator == null) animator = GetComponent<Animator>();
        tauntTriggerHash = Animator.StringToHash(tauntTrigger);
        hasTauntTrigger = animator != null && HasAnimatorParameter(animator, tauntTrigger);
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;
    }

    // Instant — this is a panic button. The wheel passes the reticle target,
    // which is irrelevant here and ignored.
    public override bool TryActivate(Transform ignored)
    {
        if (cooldownRemaining > 0f)
        {
            if (logTaunt) Debug.Log($"[LaylaTaunt] Still on cooldown — {cooldownRemaining:F1}s left.", this);
            return false;
        }

        int pulled = 0;
        int skipped = 0;
        float sqrRadius = tauntRadius * tauntRadius;

        // Enemy counts are small and this runs once per press, so a find-by-type
        // is cheap enough — same call PlayerLockOn makes for its lock sweep.
        EnemyFollowPlayer[] enemies = FindObjectsOfType<EnemyFollowPlayer>();
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyFollowPlayer enemy = enemies[i];
            if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;
            if ((enemy.transform.position - transform.position).sqrMagnitude > sqrRadius) continue;

            if (enemy.TryTaunt(transform, tauntDuration)) pulled++;
            else skipped++;
        }

        ApplyHealthBoost();

        cooldownRemaining = cooldown;
        if (hasTauntTrigger) animator.SetTrigger(tauntTriggerHash);

        if (logTaunt)
            Debug.Log($"[LaylaTaunt] {name} taunted {pulled} enemies for {tauntDuration}s " +
                      $"({skipped} in range ignored — not engaged yet).", this);

        // True even with nobody pulled: the health boost still landed, so the
        // ability did something and shouldn't read as a failed press.
        return true;
    }

    private void ApplyHealthBoost()
    {
        if (body == null || healthBonus <= 0f) return;
        LaylaHealthBoost boost = GetComponent<LaylaHealthBoost>();
        if (boost == null) boost = gameObject.AddComponent<LaylaHealthBoost>();
        boost.Apply(healthBonus, boostDuration, logTaunt);
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
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, tauntRadius);
    }
}
