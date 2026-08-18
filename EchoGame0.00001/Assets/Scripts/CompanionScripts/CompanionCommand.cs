using UnityEngine;
using UnityEngine.AI;

// Who issued the attack that's currently running. The autonomous brain drives
// this same engine rather than forking it, so HasActiveCommand alone can no
// longer tell "the player told her to" from "she decided to" — and a player
// command must always win. APPEND-ONLY: Unity serializes enum values as ints.
// (Kept top-level, like AbilityTargetKind on CompanionAbility.)
public enum CommandOwner
{
    Player,
    Brain
}

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
    [Tooltip("Swings per ATTACK command before the companion goes back to follow. Leave at 1 on autonomous companions: the command self-terminates after each swing, so CompanionCombatBrain re-checks its leash and target priority every swing rather than every few.")]
    [SerializeField, Min(1)] private int attacksPerCommand = 3;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationDampTime = 0.1f;
    [Tooltip("Authored speed of the run animation — keeps footsteps synced while chasing.")]
    [SerializeField] private float chaseAnimSpeed = 6f;
    [Tooltip("Animator trigger fired on a swing. Must match the parameter name on the Animator Controller.")]
    [SerializeField] private string attackTrigger = "Attack";
    [Tooltip("Multiplier applied to animator.speed WHILE a swing is playing. Cancels out the CompanionAttack state's authored 2x speed when the clip is shorter than the original sword swing. Layla's fists run natively at 1x but the shared state is authored at 2x, so she sets this to 0.5 — final = state.m_Speed (2) x this (0.5) = 1x native. Keep at 1 for companions whose clip and state speeds already match.")]
    [SerializeField, Min(0.01f)] private float attackAnimationSpeed = 1f;

    [Header("Hit Volume")]
    [Tooltip("Attack box for the swing overlap. Auto-found on this GameObject if empty. Without one, DealDamage hits the current target directly.")]
    [SerializeField] private CompanionAttackBox attackBox;

    [Header("Debug")]
    [Tooltip("Log StartAttack / DealDamage / EndAttack. Handy when the animation isn't playing.")]
    [SerializeField] private bool logAttack = false;

    private NavMeshAgent agent;
    private BasicPlayerFollowScript follow;
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

    // Only meaningful while HasActiveCommand. Not cleared in CancelCommand — every
    // reader gates on targetEnemy first, so a stale value can't be observed, and
    // clearing it there would mean touching the two internal cancel call sites.
    public CommandOwner CurrentOwner { get; private set; }

    // The brain's OVERRIDE guard: it stands down entirely while this is true.
    public bool HasPlayerCommand => targetEnemy != null && CurrentOwner == CommandOwner.Player;

    // Who we're swinging at, so the brain can tell whether the current command
    // already covers the enemy it just picked.
    public Transform CurrentTarget => targetEnemy;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        follow = GetComponent<BasicPlayerFollowScript>();
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

    // Player-issued attack. Kept as its own overload rather than a defaulted
    // argument so the player path reads as the default identity, and so the three
    // existing call sites (PlayerCompanionCommander, AttackAbility, CommandWheel)
    // need no edit at all.
    public void CommandAttack(Transform enemy) => CommandAttack(enemy, CommandOwner.Player);

    public void CommandAttack(Transform enemy, CommandOwner owner)
    {
        // The brain can never overwrite a live player order. A player order always
        // wins, including over another player order (that's today's behaviour).
        if (owner == CommandOwner.Brain && targetEnemy != null && CurrentOwner == CommandOwner.Player)
            return;

        targetEnemy = enemy;
        CurrentOwner = owner;
        // Every new command starts with a full swing budget, even if the last one
        // got cut short.
        attacksRemaining = attacksPerCommand;
    }

    // Cancel only if the caller is the one who issued the order. The brain uses
    // this to drop its own command on a leash break without ever cancelling
    // something the player asked for. CancelCommand itself stays unconditional —
    // the internal callers below (dead target, budget spent) must still work.
    public bool CancelIfOwnedBy(CommandOwner owner)
    {
        if (targetEnemy == null || CurrentOwner != owner) return false;
        CancelCommand();
        return true;
    }

    public void CancelCommand()
    {
        targetEnemy = null;
        // Preempt an in-flight swing. Without this, IsAttacking stays true after
        // a cancel and the Update block freezes the agent (ResetPath every frame)
        // until EndAttack fires or maxAttackDuration times out — up to 1.5s of
        // dead time where whatever preempted us (a player ability, self-preserve,
        // a jump) can't steer. Safe for internal callers too: OnSwingFinished
        // arrives with IsAttacking already false; the target-inactive path calls
        // this before any StartAttack, so there's nothing to interrupt there.
        IsAttacking = false;
        attackElapsed = 0f;
        // Triggers stay switched on until a transition uses them. If one is still
        // queued up when the command ends, the animator plays one more swing while
        // the companion is already walking back to the player — clear it here.
        if (animator != null) animator.ResetTrigger(attackTriggerHash);
        if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

        // A link traversal writes transform.position directly every frame; running
        // any of our steering/attack logic during it fights the manual write.
        if (follow != null && follow.IsJumping) return;

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
            // Track a strafing target through the swing — StartAttack snapped the first frame.
            if (targetEnemy != null && targetEnemy.gameObject.activeInHierarchy)
            {
                Vector3 toEnemyDuringSwing = targetEnemy.position - transform.position;
                toEnemyDuringSwing.y = 0f;
                if (toEnemyDuringSwing.sqrMagnitude > 0.0001f)
                {
                    Quaternion swingLook = Quaternion.LookRotation(toEnemyDuringSwing.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, swingLook, rotationSpeed * Time.deltaTime);
                }
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
            // Path routed through a NavMeshLink (target on another mesh — e.g. a
            // lower level). Auto-traverse is off, so nothing crosses it during a
            // command unless we trigger the hop; without this the companion freezes
            // on the link.
            if (agent.isOnOffMeshLink && follow != null)
            {
                follow.BeginLinkTraversal();
                return;
            }
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
        // Snap facing on the swing frame — the Update Slerp only gets one tick before
        // the IsAttacking early-return locks the rotation for the rest of the swing.
        if (targetEnemy != null)
        {
            Vector3 toEnemy = targetEnemy.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toEnemy.normalized);
        }
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
        // While swinging, animator.speed lets us tune the CompanionAttack state's authored
        // m_Speed per companion. attackAnimationSpeed = 1 keeps the state speed as authored
        // (this was the original assumption when clip and state were in sync). Punches and
        // kicks are shorter than the original sword swing, so Layla runs 0.5 to bring the
        // effective playback back down to native rate.
        animator.speed = IsAttacking ? attackAnimationSpeed : (speed > 0.1f ? speed / chaseAnimSpeed : 1f);
    }
}
