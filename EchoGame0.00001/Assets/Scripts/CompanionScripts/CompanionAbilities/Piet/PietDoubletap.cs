using UnityEngine;
using UnityEngine.AI;

// Piet's Double Tap: two rifle shots in quick succession at one enemy.
//
// The project's FIRST ranged attack — everything else (CompanionAttackBox,
// EnemyCombat, PlayerBasicCombat) resolves damage with a melee overlap. Shots here
// are hitscan: a raycast from the muzzle to the target, damage applied instantly.
//
// Targeting comes from the wheel's enemy-cycle layer (TargetKind = EnemyPicker):
// the player d-pads through nearby enemies and confirms, and the wheel hands the
// chosen enemy to TryActivate.
//
// Unlike the melee companions he does NOT charge in. If he can't see the target he
// walks to a spot that has a clear line, then fires from there.
public class PietDoubletap : CompanionAbility
{
    private enum Phase { Idle, Repositioning, Firing }

    [Header("Shots")]
    [Tooltip("Damage each individual shot deals. Two shots by default, so a full Double Tap is twice this.")]
    [SerializeField] private float damagePerShot = 25f;
    [Tooltip("How many shots the burst fires. 2 = the classic double tap; raise it for a longer burst.")]
    [SerializeField, Min(1)] private int shotsPerBurst = 2;
    [Tooltip("Seconds between shots in the burst. Small — the two shots should read as one action.")]
    [SerializeField, Min(0f)] private float shotDelay = 0.18f;
    [Tooltip("Seconds before Double Tap can be used again, counted from the end of the burst.")]
    [SerializeField, Min(0f)] private float cooldown = 8f;

    [Header("Range & Positioning")]
    [Tooltip("Furthest he will shoot from. Past this he repositions before firing.")]
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
    [Tooltip("Used when no Muzzle transform is set: how far above Piet's feet shots originate.")]
    [SerializeField] private float muzzleHeight = 1.4f;
    [Tooltip("How far up the enemy he aims — roughly chest height, so shots don't clip the floor.")]
    [SerializeField] private float targetHeight = 1f;
    [Tooltip("Layers that count as shootable enemies. MUST be set, or shots hit nothing.")]
    [SerializeField] private LayerMask enemyMask;
    [Tooltip("Layers that stop a bullet and block line of sight — walls, terrain, cover.")]
    [SerializeField] private LayerMask shotBlockers;

    [Header("Visuals")]
    [Tooltip("How long each tracer streak stays on screen. Short — it should read as a snap, not a laser beam.")]
    [SerializeField] private float tracerDuration = 0.06f;
    [SerializeField] private Color tracerColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private float tracerWidth = 0.04f;
    [Tooltip("Optional effect spawned where a shot lands (particle system, decal...). Leave empty — the tracer and the target's hit flash already show hits.")]
    [SerializeField] private GameObject impactPrefab;
    [Tooltip("Seconds before a spawned impact effect is cleaned up.")]
    [SerializeField] private float impactLifetime = 2f;

    [Header("Animation")]
    [Tooltip("Optional. Animator trigger fired per shot. Only used if a parameter with this name exists — safe with no animation.")]
    [SerializeField] private string shootTrigger = "Shoot";
    [SerializeField] private Animator animator;

    [Header("Debug")]
    [Tooltip("Log each shot, what it hit, and repositioning decisions.")]
    [SerializeField] private bool logShots = true;

    private NavMeshAgent agent;
    private CompanionCommand command;
    private Transform target;
    private Phase phase = Phase.Idle;
    private int shotsFired;
    private float shotTimer;
    private float repositionElapsed;
    private float cooldownRemaining;
    private bool hasShootTrigger;
    private int shootTriggerHash;

    private LineRenderer tracer;
    private Material tracerMaterial;
    private float tracerRemaining;

    // Opens the wheel's enemy-cycle layer instead of firing on the reticle.
    public override AbilityTargetKind TargetKind => AbilityTargetKind.EnemyPicker;
    public override float CooldownRemaining => Mathf.Max(0f, cooldownRemaining);
    // Busy for the whole approach-and-fire, so follow/wander yield the agent.
    public override bool IsBusy => phase != Phase.Idle;

    void Reset()
    {
        abilityName = "Double Tap";
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        command = GetComponent<CompanionCommand>();
        if (animator == null) animator = GetComponent<Animator>();
        shootTriggerHash = Animator.StringToHash(shootTrigger);
        hasShootTrigger = animator != null && HasAnimatorParameter(animator, shootTrigger);

        BuildTracer();

        // An unset mask silently hits nothing, which looks exactly like the ability
        // being broken. Say so once at startup instead.
        if (enemyMask.value == 0)
            Debug.LogWarning($"[PietDoubletap] '{name}' has an empty Enemy Mask — shots will hit nothing. Set it to your enemy layer.", this);
    }

    // Built in code so the ability works dropped onto a prefab with no art or setup.
    // Same runtime-material approach as EnemyVisionCone.
    private void BuildTracer()
    {
        GameObject go = new GameObject("DoubleTapTracer");
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

        // A normal ATTACK order from the player beats a queued Double Tap.
        if (command != null && command.HasActiveCommand)
        {
            Cancel("an attack order came in");
            return;
        }

        if (target == null || !target.gameObject.activeInHierarchy)
        {
            Cancel("the target is gone");
            return;
        }

        switch (phase)
        {
            case Phase.Repositioning: TickReposition(); break;
            case Phase.Firing: TickFiring(); break;
        }
    }

    public override bool TryActivate(Transform enemy)
    {
        if (enemy == null)
        {
            if (logShots) Debug.Log("[PietDoubletap] No enemy selected — nothing to shoot.", this);
            return false;
        }

        if (cooldownRemaining > 0f)
        {
            if (logShots) Debug.Log($"[PietDoubletap] Still on cooldown — {cooldownRemaining:F1}s left.", this);
            return false;
        }

        if (enemy.GetComponentInParent<IDamageable>() == null)
        {
            if (logShots) Debug.Log($"[PietDoubletap] '{enemy.name}' can't take damage — ignoring.", this);
            return false;
        }

        target = enemy;
        shotsFired = 0;
        shotTimer = 0f;
        repositionElapsed = 0f;

        // Already got the shot? Skip the walk entirely.
        if (HasClearShot()) BeginFiring();
        else
        {
            phase = Phase.Repositioning;
            MoveToFiringPosition();
        }
        return true;
    }

    // Walks toward a spot with a clear line, re-checking every frame — if the enemy
    // steps into the open he fires early rather than finishing a pointless walk.
    private void TickReposition()
    {
        repositionElapsed += Time.deltaTime;

        if (HasClearShot())
        {
            BeginFiring();
            return;
        }

        if (repositionElapsed >= repositionTimeout)
        {
            Cancel("couldn't find a clear shot in time");
            return;
        }

        FaceTarget();
    }

    private void TickFiring()
    {
        // Hold still and stay on target between shots.
        if (agent != null && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
        FaceTarget();

        shotTimer -= Time.deltaTime;
        if (shotTimer > 0f) return;

        FireOneShot();
        shotsFired++;

        if (shotsFired >= shotsPerBurst)
        {
            cooldownRemaining = cooldown;
            if (logShots) Debug.Log($"[PietDoubletap] Burst finished — {shotsFired} shots fired.", this);
            EndCommand();
            return;
        }
        shotTimer = shotDelay;
    }

    private void BeginFiring()
    {
        phase = Phase.Firing;
        shotTimer = 0f; // first shot lands immediately
        if (agent != null && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
    }

    private void FireOneShot()
    {
        Vector3 origin = MuzzlePosition();
        Vector3 aimPoint = target.position + Vector3.up * targetHeight;
        Vector3 dir = aimPoint - origin;
        float distance = dir.magnitude;
        if (distance < 0.001f) return;
        dir /= distance;

        if (hasShootTrigger) animator.SetTrigger(shootTriggerHash);

        // One cast against enemies AND cover, so whatever is physically first wins —
        // stepping behind a wall mid-burst stops the second shot, as it should.
        if (Physics.Raycast(origin, dir, out RaycastHit hit, distance + 0.5f,
                            enemyMask | shotBlockers, QueryTriggerInteraction.Ignore))
        {
            ShowTracer(origin, hit.point);

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                // Routes through EnemyHealth.TakeDamage, so Zara's debuff amplifies
                // these shots with no extra work here.
                damageable.TakeDamage(damagePerShot);

                EnemyHighlight highlight = hit.collider.GetComponentInParent<EnemyHighlight>();
                if (highlight != null) highlight.Flash();

                SpawnImpact(hit.point, hit.normal);
                if (logShots) Debug.Log($"[PietDoubletap] Shot {shotsFired + 1}/{shotsPerBurst} HIT {hit.collider.name} for {damagePerShot}.", this);
            }
            else
            {
                SpawnImpact(hit.point, hit.normal);
                if (logShots) Debug.Log($"[PietDoubletap] Shot {shotsFired + 1}/{shotsPerBurst} blocked by {hit.collider.name}.", this);
            }
            return;
        }

        // Nothing in the way and nothing hit — draw the streak out to the aim point.
        ShowTracer(origin, aimPoint);
        if (logShots) Debug.Log($"[PietDoubletap] Shot {shotsFired + 1}/{shotsPerBurst} missed — nothing on the Enemy Mask along that line.", this);
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

            Vector3 eye = navHit.position + Vector3.up * muzzleHeight;
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
            Debug.Log($"[PietDoubletap] {(found ? "Moving to a firing position" : "No firing position found — closing in")} on {target.name}.", this);
    }

    private bool HasClearShot()
    {
        if (target == null) return false;

        Vector3 origin = MuzzlePosition();
        Vector3 aimPoint = target.position + Vector3.up * targetHeight;
        if ((aimPoint - origin).magnitude > range) return false;

        return !Physics.Linecast(origin, aimPoint, shotBlockers, QueryTriggerInteraction.Ignore);
    }

    private Vector3 MuzzlePosition()
    {
        return muzzle != null ? muzzle.position : transform.position + Vector3.up * muzzleHeight;
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

    private void Cancel(string reason)
    {
        if (logShots && phase != Phase.Idle)
            Debug.Log($"[PietDoubletap] Double Tap cancelled — {reason}.", this);
        EndCommand();
    }

    private void EndCommand()
    {
        phase = Phase.Idle;
        target = null;
        shotsFired = 0;
        if (agent != null && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
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
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, range);
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, preferredRange);
    }
}
