using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CompanionCommand : MonoBehaviour
{
    [Header("Attack Approach")]
    [Tooltip("Companion stops moving once within this range of the target.")]
    [SerializeField] private float attackRange = 1.8f;
    [Tooltip("Move speed while charging a target.")]
    [SerializeField] private float chaseSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Attack")]
    [Tooltip("Seconds between swings while in range.")]
    [SerializeField] private float attackCooldown = 1.5f;
    [Tooltip("Safety net if the EndAttack animation event never fires. Set slightly longer than the attack clip.")]
    [SerializeField] private float maxAttackDuration = 1.5f;
    [Tooltip("Damage per landed swing.")]
    [SerializeField] private float damage = 15f;
    [Tooltip("Swings per ATTACK command before the companion goes back to follow.")]
    [SerializeField, Min(1)] private int attacksPerCommand = 3;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;
    [Tooltip("Authored speed of the run animation — keeps footsteps synced while chasing.")]
    [SerializeField] private float chaseAnimSpeed = 6f;
    [Tooltip("Animator trigger fired on a swing. Must match the parameter name on the Animator Controller.")]
    [SerializeField] private string attackTrigger = "Attack";

    [Header("Hit Volume")]
    [Tooltip("Attack box for the swing overlap. Auto-found on this GameObject if empty. Without one, DealDamage hits the current target directly.")]
    [SerializeField] private CompanionAttackBox attackBox;

    [Header("Debug")]
    [Tooltip("Log StartAttack / DealDamage / EndAttack. Handy when the animation isn't playing.")]
    [SerializeField] private bool logAttack = false;

    private NavMeshAgent agent;
    private Transform targetEnemy;
    private float cooldownRemaining;
    private float attackElapsed;
    private int attackTriggerHash;
    private int attacksRemaining;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public bool IsAttacking { get; private set; }
    // Future UI (wheel cooldown pips) can read this.
    public int AttacksRemaining => attacksRemaining;

    // Follow/wander scripts check this so they step aside while a command is running,
    // instead of fighting us over SetDestination each frame.
    public bool HasActiveCommand => targetEnemy != null;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponent<Animator>();
        if (attackBox == null) attackBox = GetComponent<CompanionAttackBox>();
        attackTriggerHash = Animator.StringToHash(attackTrigger);

        // SetTrigger on a missing parameter fails silently — the biggest reason
        // "attacks look broken" when everything else looks fine. Warn now rather
        // than let it fail invisibly at runtime.
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
        // Every new command starts with a full swing budget, even if the last one
        // got cut short.
        attacksRemaining = attacksPerCommand;
    }

    public void CancelCommand()
    {
        targetEnemy = null;
        // Triggers stay switched on until a transition uses them. If one is still
        // queued up when the command ends, the animator plays one more swing while
        // the companion is already walking back to the player — clear it here.
        if (animator != null) animator.ResetTrigger(attackTriggerHash);
        if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

        if (IsAttacking)
        {
            attackElapsed += Time.deltaTime;
            // Freeze the agent while swinging so we don't slide through the enemy
            // mid-animation.
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

        // Enemy died or was disabled mid-charge — bail out.
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
            // In range: stop pathing, face the enemy, swing if the cooldown's up.
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
        // One swing off the budget. If it hits 0, OnSwingFinished ends the command
        // after this swing finishes playing.
        attacksRemaining--;
        if (animator != null) animator.SetTrigger(attackTriggerHash);
        if (logAttack) Debug.Log($"[CompanionCommand] {name} StartAttack — swing {attacksPerCommand - attacksRemaining}/{attacksPerCommand}, SetTrigger('{attackTrigger}').", this);
    }

    // Runs at the end of every swing — whether EndAttack fired from an Animation
    // Event or the safety-net timeout tripped.
    private void OnSwingFinished()
    {
        if (attacksRemaining <= 0 && targetEnemy != null)
        {
            if (logAttack) Debug.Log($"[CompanionCommand] {name} finished attack sequence — returning to follow.", this);
            CancelCommand();
        }
    }

    // Called from the Animation Event on the swing frame. Prefer the AttackBox —
    // it overlaps so swings can miss or hit multiple enemies. If there isn't one
    // yet, fall back to hitting the current target directly so damage still lands
    // while the box is being set up.
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
        // Don't scale the animator while attacking — the swing should play at its authored speed.
        animator.speed = IsAttacking ? 1f : (speed > 0.1f ? speed / chaseAnimSpeed : 1f);
    }
}
