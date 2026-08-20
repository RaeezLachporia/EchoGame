using UnityEngine;
using UnityEngine.AI;

// Piet's Prone Shot: he drops into prone, steadies himself, and lands one heavy
// hitscan shot before getting back up.
//
// The heavy counterpart to PietDoubletap. Same hitscan/tracer/reposition bones as
// Double Tap (duplicated here rather than shared, keeping each ability
// self-contained and Double Tap untouched), but built around a committed prone
// animation with a steady delay before the shot.
//
// Targeting comes from the wheel's enemy-cycle layer (TargetKind = EnemyPicker):
// the player d-pads through nearby enemies and confirms.
//
// Phase machine: Idle -> Repositioning -> GoingProne -> Steadying -> GettingUp.
//   Repositioning — reuse Double Tap's walk-to-a-clear-line. ONLY phase a player
//                   attack order can cancel; going prone is a commitment.
//   GoingProne    — fire the GoProne trigger, wait for the OnProneReady animation
//                   event (safety timeout if it never comes — same guard Layla's
//                   shove uses for OnShoveImpact).
//   Steadying     — the steady delay. Cast bar fills over steadyTime; the shot
//                   fires when it expires. He's in a looping prone-aim hold, so
//                   the exact frame doesn't matter — the delay IS the mechanic.
//   GettingUp     — fire StandUp, stay busy through riseRecovery so the get-up
//                   plays out before follow/wander reclaim him.
public class PietProneShot : CompanionAbility
{
    private enum Phase { Idle, Repositioning, GoingProne, Steadying, GettingUp }

    [Header("Shot")]
    [Tooltip("Damage the single heavy shot deals. This is his big-hit ability, so it's tuned well above a Double Tap round.")]
    [SerializeField, Range(50f, 300f)] private float damage = 150f;
    [Tooltip("Seconds before Prone Shot can be used again, counted from the moment the shot fires.")]
    [SerializeField, Min(0f)] private float cooldown = 12f;

    [Header("Steady")]
    [Tooltip("Seconds he holds prone steadying before the shot fires. The cast bar fills over this time — this delay is the whole point of the ability.")]
    [SerializeField, Range(0.5f, 5f)] private float steadyTime = 2f;
    [Tooltip("Safety net: if the OnProneReady animation event never fires within this long after GoProne, steadying starts anyway so a mis-wired clip can't freeze him prone forever. Set a bit longer than the stand->crouch->prone chain.")]
    [SerializeField, Range(1f, 6f)] private float proneReadyTimeout = 4f;
    [Tooltip("How long he stays committed after the shot while the get-up animation (prone->crouch->stand) plays, before follow/wander take the agent back.")]
    [SerializeField, Range(0f, 3f)] private float riseRecovery = 1f;

    [Header("Range & Positioning")]
    [Tooltip("Furthest he will shoot from. Past this he repositions before going prone.")]
    [SerializeField] private float range = 25f;
    [Tooltip("The distance he tries to stand at when he has to move for a clear shot. Keep well under Range so he isn't shooting from the very edge.")]
    [SerializeField] private float preferredRange = 12f;
    [Tooltip("How many positions around the target to test when hunting for a firing spot. More = better spots, slightly more work on the one frame it runs.")]
    [SerializeField, Min(4)] private int firingPositionSamples = 12;
    [Tooltip("Give up repositioning after this long and abandon the shot, so an unreachable target can't freeze him.")]
    [SerializeField] private float repositionTimeout = 6f;
    [Tooltip("Move speed while walking to a firing position.")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Aim")]
    [Tooltip("Where shots leave from. Drag the rifle's muzzle here once the model exists — until then it fires from Muzzle Height above his origin.")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Used when no Muzzle transform is set: how far above Piet's feet shots originate. He's PRONE for this shot, so keep it low — near the ground.")]
    [SerializeField] private float muzzleHeight = 0.4f;
    [Tooltip("Eye height for the LINE-OF-SIGHT decision only — he's standing upright while choosing a firing spot and walking to it, and drops prone only after committing. Firing still leaves from the low Muzzle Height. Set to roughly his standing chest/eye height; too low and he clips walls near cover and never commits.")]
    [SerializeField] private float sightHeight = 1.4f;
    [Tooltip("How far up the enemy he aims — roughly chest height, so shots don't clip the floor.")]
    [SerializeField] private float targetHeight = 1f;
    [Tooltip("Layers that count as shootable enemies. MUST be set, or shots hit nothing.")]
    [SerializeField] private LayerMask enemyMask;
    [Tooltip("Layers that stop a bullet and block line of sight — walls, terrain, cover.")]
    [SerializeField] private LayerMask shotBlockers;

    [Header("Visuals")]
    [Tooltip("How long the tracer streak stays on screen. A touch longer/heavier than Double Tap's, since this is one big shot.")]
    [SerializeField] private float tracerDuration = 0.1f;
    [SerializeField] private Color tracerColor = new Color(1f, 0.6f, 0.2f, 1f);
    [SerializeField] private float tracerWidth = 0.08f;
    [Tooltip("Optional effect spawned where the shot lands (particle system, decal...). Leave empty — the tracer and the target's hit flash already show hits.")]
    [SerializeField] private GameObject impactPrefab;
    [Tooltip("Seconds before a spawned impact effect is cleaned up.")]
    [SerializeField] private float impactLifetime = 2f;

    [Header("Animation")]
    [Tooltip("Trigger that starts the stand->crouch->prone chain. Wire the animator so Locomotion transitions to the prone chain on this, with Has Exit Time OFF so it drops immediately.")]
    [SerializeField] private string proneTrigger = "GoProne";
    [Tooltip("Trigger that starts the prone->crouch->stand chain, fired right after the shot. The looping ProneAim state should leave only on this.")]
    [SerializeField] private string standTrigger = "StandUp";
    [SerializeField] private Animator animator;

    [Header("Debug")]
    [Tooltip("Log the shot, what it hit, repositioning, and phase changes.")]
    [SerializeField] private bool logShots = true;

    private NavMeshAgent agent;
    private CompanionCommand command;
    private Transform target;
    private Phase phase = Phase.Idle;
    private float proneElapsed;
    private float steadyRemaining;
    private float riseRemaining;
    private float repositionElapsed;
    private float cooldownRemaining;
    private bool hasProneTrigger;
    private bool hasStandTrigger;
    private int proneTriggerHash;
    private int standTriggerHash;

    private LineRenderer tracer;
    private Material tracerMaterial;
    private float tracerRemaining;

    // Opens the wheel's enemy-cycle layer instead of firing on the reticle.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.EnemyPicker;
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);
    // Busy for the whole approach-prone-fire-rise sequence, so follow/wander yield.
    public override bool IsBusy => phase != Phase.Idle;
    // Cast bar fills while he steadies — reuses CompanionUI's cast bar, same shape
    // as ZaraBuffAlly.
    public override bool IsCasting => phase == Phase.Steadying;
    public override float CastProgress =>
        phase != Phase.Steadying ? 0f : (steadyTime <= 0f ? 1f : Mathf.Clamp01(1f - steadyRemaining / steadyTime));

    void Reset()
    {
        abilityName = "Prone Shot";
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        command = GetComponent<CompanionCommand>();
        if (animator == null) animator = GetComponent<Animator>();

        proneTriggerHash = Animator.StringToHash(proneTrigger);
        standTriggerHash = Animator.StringToHash(standTrigger);
        hasProneTrigger = animator != null && HasAnimatorParameter(animator, proneTrigger);
        hasStandTrigger = animator != null && HasAnimatorParameter(animator, standTrigger);

        BuildTracer();

        // An unset mask silently hits nothing, which looks exactly like the ability
        // being broken. Say so once at startup instead.
        if (enemyMask.value == 0)
            Debug.LogWarning($"[PietProneShot] '{name}' has an empty Enemy Mask — shots will hit nothing. Set it to your enemy layer.", this);
    }

    // Built in code so the ability works dropped onto a prefab with no art or setup.
    // Same runtime-material approach as PietDoubletap / EnemyVisionCone.
    private void BuildTracer()
    {
        GameObject go = new GameObject("ProneShotTracer");
        go.transform.SetParent(transform, false);

        tracer = go.AddComponent<LineRenderer>();
        tracer.positionCount = 2;
        tracer.useWorldSpace = true;
        tracer.startWidth = tracerWidth;
        tracer.endWidth = tracerWidth;
        tracer.numCapVertices = 2;
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tracer.receiveShadows = false;
        tracer.enabled = false;

        tracerMaterial = new Material(Shader.Find("Sprites/Default"));
        tracerMaterial.color = tracerColor;
        tracer.sharedMaterial = tracerMaterial;
    }

    void OnDestroy()
    {
        // Runtime material is ours; Unity leaks it if we don't clean up.
        if (tracerMaterial != null) Destroy(tracerMaterial);
    }

    void Update()
    {
        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;
        FadeTracer();

        if (phase == Phase.Idle) return;

        // A PLAYER-issued attack cancels — but ONLY before he commits to prone.
        // Once he's going down he completes the shot, because the prone animation
        // has to unwind cleanly. A brain-issued command never cancels, or the
        // ability would abort itself the instant the autonomous brain has a target.
        if (phase == Phase.Repositioning && command != null && command.HasPlayerCommand)
        {
            Cancel("a player attack order came in");
            return;
        }

        // Target lost. Before commit, abandon. After commit, keep going — he plays
        // out the prone shot and whiffs, rather than half-finishing the animation.
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            if (phase == Phase.Repositioning)
            {
                Cancel("the target is gone");
                return;
            }
            target = null; // FireShot handles a null target as a whiff.
        }

        switch (phase)
        {
            case Phase.Repositioning: TickReposition(); break;
            case Phase.GoingProne: TickGoingProne(); break;
            case Phase.Steadying: TickSteadying(); break;
            case Phase.GettingUp: TickGettingUp(); break;
        }
    }

    public override bool TryActivate(Transform enemy)
    {
        if (enemy == null)
        {
            if (logShots) Debug.Log("[PietProneShot] No enemy selected — nothing to shoot.", this);
            return false;
        }

        if (cooldownRemaining > 0f)
        {
            if (logShots) Debug.Log($"[PietProneShot] Still on cooldown — {cooldownRemaining:F1}s left.", this);
            return false;
        }

        if (enemy.GetComponentInParent<IDamageable>() == null)
        {
            if (logShots) Debug.Log($"[PietProneShot] '{enemy.name}' can't take damage — ignoring.", this);
            return false;
        }

        target = enemy;
        repositionElapsed = 0f;

        // Already got the line? Skip the walk and drop straight into prone.
        if (HasClearShot()) StartGoingProne();
        else
        {
            phase = Phase.Repositioning;
            MoveToFiringPosition();
        }
        return true;
    }

    // Walks toward a spot with a clear line, re-checking every frame — if the enemy
    // steps into the open he goes prone early rather than finishing a pointless walk.
    private void TickReposition()
    {
        repositionElapsed += Time.deltaTime;

        if (HasClearShot())
        {
            StartGoingProne();
            return;
        }

        if (repositionElapsed >= repositionTimeout)
        {
            Cancel("couldn't find a clear shot in time");
            return;
        }

        FaceTarget();
    }

    // Commit. From here a player command no longer cancels — he's going down.
    private void StartGoingProne()
    {
        phase = Phase.GoingProne;
        proneElapsed = 0f;
        HoldStill();
        if (hasProneTrigger) animator.SetTrigger(proneTriggerHash);
        if (logShots) Debug.Log($"[PietProneShot] Going prone on {target.name} — waiting for OnProneReady.", this);
    }

    // Waiting for the animation to reach the prone hold. The clip tells us via
    // OnProneReady; the timeout is the safety net if the event isn't wired.
    private void TickGoingProne()
    {
        HoldStill();
        FaceTarget();

        proneElapsed += Time.deltaTime;
        if (proneElapsed >= proneReadyTimeout)
        {
            Debug.LogWarning("[PietProneShot] OnProneReady was never called by the animation — " +
                             "check that the Crouch->Prone clip has an Animation Event pointing at OnProneReady. " +
                             "Starting the steady timer via the safety timeout.", this);
            BeginSteadying();
        }
    }

    // Called from the Crouch->Prone clip's Animation Event, at the frame he settles
    // into the prone hold. Public and void so the animator can find it. Guarded so a
    // stray event from another clip can't jump him into steadying.
    public void OnProneReady()
    {
        if (phase != Phase.GoingProne) return;
        BeginSteadying();
    }

    private void BeginSteadying()
    {
        phase = Phase.Steadying;
        steadyRemaining = steadyTime;
        if (logShots) Debug.Log($"[PietProneShot] Steadying for {steadyTime:F1}s.", this);
        if (steadyRemaining <= 0f) FireAndRise();
    }

    private void TickSteadying()
    {
        HoldStill();
        FaceTarget();

        steadyRemaining -= Time.deltaTime;
        if (steadyRemaining <= 0f) FireAndRise();
    }

    private void FireAndRise()
    {
        FireShot();
        cooldownRemaining = cooldown;

        phase = Phase.GettingUp;
        riseRemaining = riseRecovery;
        if (hasStandTrigger) animator.SetTrigger(standTriggerHash);

        // Zero recovery means release the instant the shot lands.
        if (riseRemaining <= 0f) EndCommand();
    }

    private void TickGettingUp()
    {
        HoldStill();
        riseRemaining -= Time.deltaTime;
        if (riseRemaining <= 0f) EndCommand();
    }

    // One heavy hitscan shot. A null target (died mid-prone) is an honest whiff —
    // he still took the shot, it just hit nothing.
    private void FireShot()
    {
        if (target == null)
        {
            if (logShots) Debug.Log("[PietProneShot] Fired but the target was gone — whiffed.", this);
            return;
        }

        Vector3 origin = MuzzlePosition();
        Vector3 aimPoint = target.position + Vector3.up * targetHeight;
        Vector3 dir = aimPoint - origin;
        float distance = dir.magnitude;
        if (distance < 0.001f) return;
        dir /= distance;

        // One cast against enemies AND cover, so whatever is physically first wins —
        // a wall that slid into the line during the steady blocks the shot.
        if (Physics.Raycast(origin, dir, out RaycastHit hit, distance + 0.5f,
                            enemyMask | shotBlockers, QueryTriggerInteraction.Ignore))
        {
            ShowTracer(origin, hit.point);

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                // Routes through EnemyHealth.TakeDamage, so Zara's debuff amplifies
                // this shot with no extra work here.
                damageable.TakeDamage(damage);

                EnemyHighlight highlight = hit.collider.GetComponentInParent<EnemyHighlight>();
                if (highlight != null) highlight.Flash();

                SpawnImpact(hit.point, hit.normal);
                if (logShots) Debug.Log($"[PietProneShot] HIT {hit.collider.name} for {damage}.", this);
            }
            else
            {
                SpawnImpact(hit.point, hit.normal);
                if (logShots) Debug.Log($"[PietProneShot] Blocked by {hit.collider.name}.", this);
            }
            return;
        }

        // Nothing in the way and nothing hit — draw the streak out to the aim point.
        ShowTracer(origin, aimPoint);
        if (logShots) Debug.Log("[PietProneShot] Missed — nothing on the Enemy Mask along that line.", this);
    }

    // Ring-samples positions around the target, keeps the ones that can actually see
    // it, and heads for whichever is nearest — so he moves as little as possible.
    private void MoveToFiringPosition()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        agent.speed = moveSpeed;

        Vector3 aimPoint = target.position + Vector3.up * targetHeight;
        Vector3 best = Vector3.zero;
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < firingPositionSamples; i++)
        {
            float angle = (360f / firingPositionSamples) * i;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * preferredRange;
            Vector3 candidate = target.position + offset;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 3f, NavMesh.AllAreas)) continue;

            // Test line of sight from a standing eye — he walks there upright, then
            // drops prone. muzzleHeight is prone-low, so it isn't the right height
            // for the pre-move sight check.
            Vector3 eye = navHit.position + Vector3.up * sightHeight;
            if (Physics.Linecast(eye, aimPoint, shotBlockers, QueryTriggerInteraction.Ignore)) continue;

            float d = (navHit.position - transform.position).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = navHit.position;
            found = true;
        }

        // No sampled spot works (fully enclosed target, odd geometry) — just close in
        // and let the per-frame clear-shot test catch the moment a line opens up.
        agent.SetDestination(found ? best : target.position);
        if (logShots)
            Debug.Log($"[PietProneShot] {(found ? "Moving to a firing position" : "No firing position found — closing in")} on {target.name}.", this);
    }

    // The commit decision: is there a clear line to the target from where he stands
    // now? Uses the STANDING sight height, not the prone muzzle — he's upright while
    // deciding, and a prone-low ray clips walls near cover so he'd never commit.
    private bool HasClearShot()
    {
        if (target == null) return false;

        Vector3 origin = SightOrigin();
        Vector3 aimPoint = target.position + Vector3.up * targetHeight;
        if ((aimPoint - origin).magnitude > range) return false;

        return !Physics.Linecast(origin, aimPoint, shotBlockers, QueryTriggerInteraction.Ignore);
    }

    // Where the shot actually leaves from — prone-low, or the muzzle transform.
    private Vector3 MuzzlePosition()
    {
        return muzzle != null ? muzzle.position : transform.position + Vector3.up * muzzleHeight;
    }

    // Standing eye used only for the line-of-sight commit decision.
    private Vector3 SightOrigin()
    {
        return transform.position + Vector3.up * sightHeight;
    }

    // Hold position and drop any path so he doesn't slide while prone or steadying.
    private void HoldStill()
    {
        if (agent != null && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    private void ShowTracer(Vector3 from, Vector3 to)
    {
        if (tracer == null) return;
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;
        tracerRemaining = tracerDuration;
        if (tracerMaterial != null) tracerMaterial.color = tracerColor;
    }

    private void FadeTracer()
    {
        if (tracer == null || !tracer.enabled) return;
        tracerRemaining -= Time.deltaTime;
        if (tracerRemaining <= 0f)
        {
            tracer.enabled = false;
            return;
        }
        if (tracerMaterial != null)
        {
            Color c = tracerColor;
            c.a = tracerColor.a * Mathf.Clamp01(tracerRemaining / Mathf.Max(0.0001f, tracerDuration));
            tracerMaterial.color = c;
        }
    }

    private void SpawnImpact(Vector3 point, Vector3 normal)
    {
        if (impactPrefab == null) return;
        GameObject fx = Instantiate(impactPrefab, point, Quaternion.LookRotation(normal));
        Destroy(fx, impactLifetime);
    }

    // Only reached before commit (Repositioning) — once prone he always plays out.
    // Clears any queued prone trigger so it can't sneak out after he's walked off.
    private void Cancel(string reason)
    {
        if (logShots && phase != Phase.Idle)
            Debug.Log($"[PietProneShot] Prone Shot cancelled — {reason}.", this);
        if (hasProneTrigger) animator.ResetTrigger(proneTriggerHash);
        EndCommand();
    }

    private void EndCommand()
    {
        phase = Phase.Idle;
        target = null;
        HoldStill();
    }

    private void FaceTarget()
    {
        if (target == null) return;
        Vector3 dir = target.position - transform.position;
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, range);
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, preferredRange);
    }
}
