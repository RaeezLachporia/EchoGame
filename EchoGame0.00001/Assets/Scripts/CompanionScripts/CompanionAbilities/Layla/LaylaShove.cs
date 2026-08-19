using UnityEngine;
using UnityEngine.AI;

// Layla's shove: she runs at the enemy the player picked, plays the kick animation,
// and knocks them back the frame her foot connects.
//
// There is deliberately NO cast time. The two phases exist purely to line the
// knockback up with the animation:
//   ARRIVE  → fire the kick trigger, stash the target, stand still waiting.
//   IMPACT  → the animation clip calls OnShoveImpact() via an Animation Event on
//             the connect frame. That's when the target actually flies back.
// Same pattern EnemyCombat.DealDamage uses — the clip owns the timing, this script
// stays dumb about frame counts, and swapping in a new kick animation only means
// moving the event on the new clip.
//
// She applies a STAGGER, not a debuff: EnemyStagger is its own component with its
// own icon, and it doesn't change incoming damage. A shoved enemy can be carrying
// Zara's debuff at the same time and both icons show.
[RequireComponent(typeof(NavMeshAgent))]
public class LaylaShove : CompanionAbility
{
    [Header("Approach")]
    [Tooltip("How close she gets before the shove lands. She runs until she's this near, then hits immediately — no cast, no wind-up.")]
    [SerializeField, Range(0.5f, 5f)] private float approachDistance = 1.5f;
    [Tooltip("How fast she runs at the target.")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f;
    [Tooltip("The speed her run animation was made for — tweak until her feet stop sliding.")]
    [SerializeField] private float moveAnimSpeed = 4f;
    [Tooltip("Give up and hand the agent back if the target gets this far away. Stops her sprinting across the level after someone who fled.")]
    [SerializeField] private float giveUpDistance = 30f;

    [Header("Knockback")]
    [Tooltip("How far she shoves the target. The actual push comes up short if a wall gets in the way — that shortfall is what Wall Damage charges for.")]
    [SerializeField, Range(0f, 20f)] private float knockbackDistance = 6f;
    [Tooltip("How long the slide takes. Short reads as an impact; long reads as a gentle nudge.")]
    [SerializeField, Range(0.05f, 1f)] private float knockbackDuration = 0.25f;
    [Tooltip("How long the target is helpless after landing — can't chase, can't swing. The stagger icon shows for this long.")]
    [SerializeField, Range(0f, 5f)] private float staggerDuration = 1.5f;

    [Header("Wall Detection")]
    [Tooltip("Layers that stop a shove — walls, terrain, cover. Same layers as Piet's Shot Blockers. Leave empty and the shove never hits anything, so it always travels the full distance.")]
    [SerializeField] private LayerMask wallMask;
    [Tooltip("Width of the sweep that looks for a wall behind the target. Roughly the enemy's own radius — too small and they clip corners, too large and they stop short of everything.")]
    [SerializeField, Range(0.1f, 2f)] private float sweepRadius = 0.5f;
    [Tooltip("Height above the enemy's feet the sweep runs at, so it tests against walls rather than scraping along the floor.")]
    [SerializeField, Range(0f, 3f)] private float sweepHeight = 1f;

    [Header("Timing")]
    [Tooltip("Seconds before Shove can be used again.")]
    [SerializeField] private float cooldown = 8f;
    [Tooltip("How long she stays committed AFTER the impact frame, so the rest of the kick animation plays out before follow/wander take the agent back. 0 = release the instant it connects.")]
    [SerializeField, Range(0f, 2f)] private float recoveryTime = 0.3f;
    [Tooltip("Safety net for a missing/skipped OnShoveImpact animation event. If the event never fires within this many seconds, the impact resolves anyway — so a mis-wired clip whiffs loudly instead of hanging her forever. Set a bit longer than the kick clip.")]
    [SerializeField, Range(0.5f, 5f)] private float maxStrikeDuration = 2f;

    [Header("Wall Damage")]
    [Tooltip("Off until the numbers feel right. When on, slamming a target into a wall deals damage for the distance the wall stole from the shove.")]
    [SerializeField] private bool dealWallDamage = false;
    [Tooltip("Damage per metre of shove the wall cut short. A 6m shove stopped at 2m deals 4 x this.")]
    [SerializeField] private float damagePerBlockedMetre = 4f;
    [Tooltip("Ignore scrapes. The shove has to be cut short by at least this many metres before it counts as slamming into something.")]
    [SerializeField] private float wallHitMinShortfall = 0.5f;

    [Header("Animation")]
    [Tooltip("Can be left empty — it's found automatically. The shove works fine with no animation at all.")]
    [SerializeField] private Animator animator;
    [Tooltip("Optional. Animator trigger fired when the shove connects. Only used if a parameter with this name exists, so the ability can ship before the animation does.")]
    [SerializeField] private string shoveTrigger = "Shove";
    [SerializeField] private float animationDampTime = 0.1f;

    [Header("Debug")]
    [Tooltip("Tick to see in the Console who she shoves, how far they actually went, and what the wall stole.")]
    [SerializeField] private bool logShove = true;

    private NavMeshAgent agent;
    private CompanionCommand command;
    private Transform targetEnemy;
    private float cooldownRemaining;
    private float recoveryRemaining;
    // Wind-up phase: kick animation is playing, waiting for the animation event.
    private bool striking;
    private Transform strikingTarget;
    private float strikeTimeoutRemaining;
    private bool hasShoveTrigger;
    private int shoveTriggerHash;
    private bool warnedMissingWallMask;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // Busy while walking in, through the kick wind-up, AND through the recovery
    // window — so follow, wander and the combat brain all yield the agent to her
    // for the whole move rather than dragging her out of her own animation.
    public override bool IsBusy => targetEnemy != null || striking || recoveryRemaining > 0f;
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);

    // The player picks who gets shoved from the enemy-cycle layer — same picker
    // Zara's debuff uses. TryActivate below is handed the chosen enemy.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.EnemyPicker;

    // Deliberately does NOT override IsCasting / CastProgress: there is no cast, so
    // CompanionUI's cast bar correctly stays hidden for this ability.

    void Reset()
    {
        abilityName = "Shove";
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        command = GetComponent<CompanionCommand>();
        if (animator == null) animator = GetComponent<Animator>();

        shoveTriggerHash = Animator.StringToHash(shoveTrigger);
        hasShoveTrigger = animator != null && HasAnimatorParameter(animator, shoveTrigger);
    }

    // Runs when the player confirms an enemy on the wheel's target cycle. This only
    // ACCEPTS the job — the shove itself happens in Update once she's close enough.
    public override bool TryActivate(Transform enemy)
    {
        if (cooldownRemaining > 0f)
        {
            if (logShove) Debug.Log($"[LaylaShove] Still on cooldown — {cooldownRemaining:F1}s left.", this);
            return false;
        }

        if (enemy == null || !enemy.gameObject.activeInHierarchy)
        {
            if (logShove) Debug.Log("[LaylaShove] No live enemy to shove.", this);
            return false;
        }

        targetEnemy = enemy;
        recoveryRemaining = 0f;
        if (logShove) Debug.Log($"[LaylaShove] Moving in to shove {enemy.name}.", this);
        return true;
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

        // Wind-up: kick is playing, we're waiting for the animation event to fire
        // ResolveImpact. Runs first so nothing below (target-lost, player-attack)
        // can steal the agent out of her own kick mid-swing. FaceStrikeTarget keeps
        // her locked on if the target sidesteps during the wind-up.
        if (striking)
        {
            strikeTimeoutRemaining -= Time.deltaTime;
            FaceStrikeTarget();
            UpdateAnimation();
            // Safety net: the event never fired (missing on the clip, wrong clip
            // playing, interrupted state machine). Land the shove anyway so she
            // doesn't hang in wind-up forever, and say what likely needs wiring.
            if (strikeTimeoutRemaining <= 0f)
            {
                Debug.LogWarning("[LaylaShove] OnShoveImpact was never called by the animation — " +
                                 "check that the Kicking clip has an Animation Event pointing at OnShoveImpact. " +
                                 "Landing the shove via the safety timeout.", this);
                ResolveImpact();
            }
            return;
        }

        // The shove already landed — hold the agent through the animation, then let go.
        if (recoveryRemaining > 0f)
        {
            recoveryRemaining -= Time.deltaTime;
            UpdateAnimation();
            if (recoveryRemaining <= 0f) ReleaseAgent();
            return;
        }

        if (targetEnemy == null) return;

        // A PLAYER-issued attack beats the shove — but a brain-issued one must not,
        // or an aggressive brain would cancel the order the player just gave.
        if (command != null && command.HasPlayerCommand)
        {
            DropTarget();
            return;
        }

        // Target died, got pooled, or ran for the hills.
        if (!TargetStillValid())
        {
            DropTarget();
            return;
        }

        if (!agent.isOnNavMesh) return;

        Vector3 toEnemy = targetEnemy.position - transform.position;
        toEnemy.y = 0f;

        if (toEnemy.magnitude > approachDistance)
        {
            agent.speed = moveSpeed;
            agent.SetDestination(targetEnemy.position);
        }
        else
        {
            if (agent.hasPath) agent.ResetPath();
            StartStrike();
            return;
        }

        FaceTarget();
        UpdateAnimation();
    }

    // She's arrived. Fire the kick animation NOW and hand off to the wind-up
    // phase — the actual knockback waits for OnShoveImpact so it lines up with
    // her foot connecting instead of firing before she's visibly touched them.
    // With no shove trigger wired, there's nothing to wait for and impact resolves
    // immediately (ability still works on a companion with no kick animation).
    private void StartStrike()
    {
        // Hard-stop the agent BEFORE firing the trigger. NavMeshAgent smooths its
        // velocity down over a few frames — without this, agent.velocity still
        // reads as running when UpdateAnimation next writes Speed to the animator,
        // which blends locomotion into the kick and scales playback speed up.
        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }
        // Feed the animator that neutral state on the SAME frame, so the trigger
        // transition doesn't evaluate against a stale "still running" Speed.
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.speed = 1f;
        }

        if (hasShoveTrigger) animator.SetTrigger(shoveTriggerHash);

        // Cooldown starts on commit, not on impact — a long clip would otherwise
        // quietly shorten every cooldown by however late the event fires.
        cooldownRemaining = cooldown;

        strikingTarget = targetEnemy;
        // Hand IsBusy off to the striking flag. Leaving targetEnemy set would let
        // Update walk her forward again next frame.
        targetEnemy = null;

        if (!hasShoveTrigger)
        {
            // No animation, no event to wait for — land it immediately.
            ResolveImpact();
            return;
        }

        striking = true;
        strikeTimeoutRemaining = maxStrikeDuration;
        if (logShove) Debug.Log($"[LaylaShove] Kicking {strikingTarget.name} — waiting for impact event.", this);
    }

    // Called from the Kicking clip's Animation Event on the connect frame. Public
    // and void so the animator can find it. Same wiring pattern EnemyCombat uses
    // for DealDamage. Safe to no-op if it fires when we aren't striking — a stray
    // event from another clip won't create a phantom shove.
    public void OnShoveImpact()
    {
        if (!striking) return;
        ResolveImpact();
    }

    // The knockback + stagger + wall damage all land here. Reached either from the
    // animation event (normal case) or from the safety-net timeout in Update.
    private void ResolveImpact()
    {
        striking = false;
        strikeTimeoutRemaining = 0f;

        Transform enemy = strikingTarget;
        strikingTarget = null;

        // She can whiff — the target may have died / been pooled during the wind-up.
        // The kick animation still played and the cooldown still stands, which is
        // the honest outcome rather than silently rewinding the ability.
        if (enemy == null || !enemy.gameObject.activeInHierarchy)
        {
            if (logShove) Debug.Log("[LaylaShove] Target gone before the kick connected — whiffed.", this);
            recoveryRemaining = recoveryTime;
            if (recoveryRemaining <= 0f) ReleaseAgent();
            return;
        }

        // Straight out from her, flattened: a shove should never launch anyone into
        // the air, and an unflattened direction does exactly that on a slope.
        Vector3 direction = enemy.position - transform.position;
        direction.y = 0f;
        // Standing dead-centre on the target leaves no direction to push in — fall
        // back to her facing so the shove still goes somewhere sensible.
        direction = direction.sqrMagnitude < 0.0001f ? transform.forward : direction.normalized;

        float travelled = ResolveKnockbackDistance(enemy, direction);
        float shortfall = knockbackDistance - travelled;

        EnemyStagger stagger = enemy.GetComponent<EnemyStagger>();
        if (stagger == null) stagger = enemy.gameObject.AddComponent<EnemyStagger>();
        stagger.Apply(direction * travelled, knockbackDuration, staggerDuration, logShove);

        if (dealWallDamage && shortfall > wallHitMinShortfall)
            ApplyWallDamage(enemy, shortfall);

        recoveryRemaining = recoveryTime;

        if (logShove)
            Debug.Log($"[LaylaShove] Shoved {enemy.name} {travelled:F1}m of {knockbackDistance:F1}m " +
                      $"({shortfall:F1}m blocked), staggered for {staggerDuration:F1}s.", this);

        if (recoveryRemaining <= 0f) ReleaseAgent();
    }

    // Same as FaceTarget but for the wind-up phase, which reads strikingTarget
    // (targetEnemy is cleared the moment StartStrike commits).
    private void FaceStrikeTarget()
    {
        if (strikingTarget == null) return;
        Vector3 dir = strikingTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(dir.normalized), rotationSpeed * Time.deltaTime);
    }

    // How far the target can actually travel before something solid stops them.
    private float ResolveKnockbackDistance(Transform enemy, Vector3 direction)
    {
        if (knockbackDistance <= 0f) return 0f;

        if (wallMask == 0)
        {
            // Silent full-distance shoves look identical to "wall damage is broken".
            // Say it once, and only when the setting that depends on it is actually on.
            if (dealWallDamage && !warnedMissingWallMask)
            {
                warnedMissingWallMask = true;
                Debug.LogWarning("[LaylaShove] Deal Wall Damage is on but Wall Mask is empty — " +
                                 "nothing counts as a wall, so the shove always travels full distance " +
                                 "and never deals slam damage. Set Wall Mask to your wall/terrain layers.", this);
            }
            return knockbackDistance;
        }

        // Sweep from chest height so the cast tests walls instead of scraping floor.
        Vector3 origin = enemy.position + Vector3.up * sweepHeight;

        // SphereCastAll, not SphereCast: the cast starts inside the target's own
        // collider. A plain cast would report that overlap as the first hit at
        // distance 0 and clamp every shove to nothing.
        RaycastHit[] hits = Physics.SphereCastAll(origin, sweepRadius, direction,
            knockbackDistance, wallMask, QueryTriggerInteraction.Ignore);

        float nearest = knockbackDistance;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].transform;
            if (hit == null) continue;
            // The target being swept, and Layla doing the shoving, are not walls.
            if (hit == enemy || hit.IsChildOf(enemy)) continue;
            if (hit == transform || hit.IsChildOf(transform)) continue;
            if (hits[i].distance < nearest) nearest = hits[i].distance;
        }

        return Mathf.Clamp(nearest, 0f, knockbackDistance);
    }

    // Routed through IDamageable rather than EnemyHealth directly, matching every
    // other damage source — which means it lands on EnemyHealth.TakeDamage and picks
    // up Zara's damage debuff for free if one is running.
    private void ApplyWallDamage(Transform enemy, float shortfall)
    {
        IDamageable damageable = enemy.GetComponentInParent<IDamageable>();
        if (damageable == null) return;

        float damage = shortfall * damagePerBlockedMetre;
        damageable.TakeDamage(damage);
        if (logShove)
            Debug.Log($"[LaylaShove] {enemy.name} slammed into a wall — {shortfall:F1}m blocked = {damage:F0} damage.", this);
    }

    private bool TargetStillValid()
    {
        if (targetEnemy == null || !targetEnemy.gameObject.activeInHierarchy) return false;
        if (giveUpDistance <= 0f) return true;

        Vector3 toEnemy = targetEnemy.position - transform.position;
        toEnemy.y = 0f;
        if (toEnemy.sqrMagnitude <= giveUpDistance * giveUpDistance) return true;

        if (logShove) Debug.Log($"[LaylaShove] {targetEnemy.name} got too far away — giving up the shove.", this);
        return false;
    }

    private void DropTarget()
    {
        if (targetEnemy == null) return;
        targetEnemy = null;
        ReleaseAgent();
    }

    // Clear the path so whoever takes the agent next (follow, wander, the brain)
    // isn't inheriting a destination she set.
    private void ReleaseAgent()
    {
        if (agent != null && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    private void FaceTarget()
    {
        if (targetEnemy == null) return;
        Vector3 dir = targetEnemy.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(dir.normalized), rotationSpeed * Time.deltaTime);
    }

    private void UpdateAnimation()
    {
        // The follow/wander scripts stand down while she's busy, so this script has
        // to drive her animation itself — otherwise she'd slide around frozen.
        if (animator == null) return;
        float speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, animationDampTime, Time.deltaTime);
        animator.speed = speed > 0.1f ? speed / moveAnimSpeed : 1f;
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
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, approachDistance);
    }
}
