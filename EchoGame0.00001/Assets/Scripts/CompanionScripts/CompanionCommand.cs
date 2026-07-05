using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CompanionCommand : MonoBehaviour
{
    [Header("Attack Approach")]
    [Tooltip("Companion stops moving once within this distance of the commanded enemy.")]
    [SerializeField] private float attackRange = 1.8f;
    [Tooltip("NavMeshAgent speed while charging a commanded target.")]
    [SerializeField] private float chaseSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Attack")]
    [Tooltip("Seconds between attack swings while the companion is in range of a commanded enemy.")]
    [SerializeField] private float attackCooldown = 1.5f;
    [Tooltip("Safety net — isAttacking auto-clears after this if the EndAttack animation event never fires. Set slightly longer than the attack clip.")]
    [SerializeField] private float maxAttackDuration = 1.5f;
    [Tooltip("Damage each swing does when a hit connects via the DealDamage animation event.")]
    [SerializeField] private float damage = 15f;
    [Tooltip("How many swings the companion performs per ATTACK command before returning to follow the player. Different companions can be tuned for burst vs sustained damage.")]
    [SerializeField, Min(1)] private int attacksPerCommand = 3;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;
    [Tooltip("Speed the run animation was authored for — used to keep footsteps synced while chasing.")]
    [SerializeField] private float chaseAnimSpeed = 6f;
    [Tooltip("Trigger fired on the animator when a swing starts. Match the parameter name on the companion's Animator Controller.")]
    [SerializeField] private string attackTrigger = "Attack";

    [Header("Hit Volume")]
    [Tooltip("Attack hit volume used on the DealDamage animation event. Auto-found on this GameObject if left empty; if none exists, DealDamage falls back to hitting the current target directly.")]
    [SerializeField] private CompanionAttackBox attackBox;

    [Header("Debug")]
    [Tooltip("Log a message every time StartAttack / DealDamage / EndAttack fires. Turn on when the attack animation isn't triggering to see where the pipeline breaks.")]
    [SerializeField] private bool logAttack = false;

    private NavMeshAgent agent;
    private Transform targetEnemy;
    private float cooldownRemaining;
    private float attackElapsed;
    private int attackTriggerHash;
    private int attacksRemaining;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public bool IsAttacking { get; private set; }
    // Exposed so future UI (wheel cooldown pips) can read the swing budget.
    public int AttacksRemaining => attacksRemaining;

    // Other companion scripts (follow, wander) check this to step aside so we don't
    // fight over SetDestination every frame.
    public bool HasActiveCommand => targetEnemy != null;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponent<Animator>();
        if (attackBox == null) attackBox = GetComponent<CompanionAttackBox>();
        attackTriggerHash = Animator.StringToHash(attackTrigger);

        // Warn early if the animator can't hear our trigger — SetTrigger on a
        // missing parameter fails silently, which is the #1 reason attacks
        // "look like they don't play" while everything else looks correct.
        if (animator != null && !HasAnimatorParameter(animator, attackTrigger))
            Debug.LogWarning($"[CompanionCommand] Animator on '{name}' has no parameter named '{attackTrigger}'. The attack animation won't trigger — add a Trigger with that exact name to the Animator Controller, or change the Attack Trigger field to match.", this);
    }

    private static bool HasAnimatorParameter(Animator a, string paramName)
    {
        AnimatorControllerParameter[] parameters = a.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == paramName) return true;
        return false;
    }

    public void CommandAttack(Transform enemy)
    {
        targetEnemy = enemy;
        // Refill the swing budget so every fresh command starts at full — even if
        // a previous command was interrupted with attacks left over.
        attacksRemaining = attacksPerCommand;
    }

    public void CancelCommand()
    {
        targetEnemy = null;
        if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

        if (IsAttacking)
        {
            attackElapsed += Time.deltaTime;
            // Freeze the agent while a swing plays so the companion doesn't slide
            // through the enemy mid-animation.
            if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
            if (attackElapsed >= maxAttackDuration)
            {
                IsAttacking = false;
                OnSwingFinished();
            }
            UpdateAnimation();
            return;
        }

        if (targetEnemy == null) return;
        if (!agent.isOnNavMesh) return;

        // Enemy destroyed since the command was issued (e.g. died mid-charge).
        if (!targetEnemy.gameObject.activeInHierarchy)
        {
            CancelCommand();
            return;
        }

        Vector3 toEnemy = targetEnemy.position - transform.position;
        toEnemy.y = 0f;
        float distance = toEnemy.magnitude;

        if (distance > attackRange)
        {
            agent.speed = chaseSpeed;
            agent.SetDestination(targetEnemy.position);
        }
        else
        {
            // In range: stop pathing, face the enemy, swing if the cooldown has expired.
            if (agent.hasPath) agent.ResetPath();
            if (cooldownRemaining <= 0f) StartAttack();
        }

        if (toEnemy.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(toEnemy.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
        }

        UpdateAnimation();
    }

    private void StartAttack()
    {
        IsAttacking = true;
        attackElapsed = 0f;
        cooldownRemaining = attackCooldown;
        // Consume one swing from the budget at the point the animation kicks off.
        // Ending the command after the last swing plays out happens in OnSwingFinished.
        attacksRemaining--;
        if (animator != null) animator.SetTrigger(attackTriggerHash);
        if (logAttack) Debug.Log($"[CompanionCommand] {name} StartAttack — swing {attacksPerCommand - attacksRemaining}/{attacksPerCommand}, SetTrigger('{attackTrigger}').", this);
    }

    // Shared cleanup that runs at the end of a swing regardless of whether the
    // Animation Event fired or the safety-net timeout tripped.
    private void OnSwingFinished()
    {
        if (attacksRemaining <= 0 && targetEnemy != null)
        {
            if (logAttack) Debug.Log($"[CompanionCommand] {name} finished attack sequence — returning to follow.", this);
            CancelCommand();
        }
    }

    // Called from the attack animation's Animation Event at the swing frame.
    // Preferred path is the CompanionAttackBox — it does an overlap check so
    // swings can miss / hit multiple enemies. Falls back to hitting the current
    // target directly if no attack box is attached, so an animation event still
    // deals damage while the box is being set up.
    public void DealDamage()
    {
        if (logAttack) Debug.Log($"[CompanionCommand] {name} DealDamage animation event fired.", this);
        if (attackBox != null)
        {
            attackBox.TryDealDamage(damage);
            return;
        }
        if (targetEnemy == null) return;
        IDamageable damageable = targetEnemy.GetComponentInParent<IDamageable>();
        damageable?.TakeDamage(damage);
    }

    // Called from an Animation Event near the end of the attack clip.
    public void EndAttack()
    {
        IsAttacking = false;
        if (logAttack) Debug.Log($"[CompanionCommand] {name} EndAttack animation event fired.", this);
        OnSwingFinished();
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;
        float speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);
        // Don't scale the animator during an attack — the swing clip should play at authored speed.
        animator.speed = IsAttacking ? 1f : (speed > 0.1f ? speed / chaseAnimSpeed : 1f);
    }
}
