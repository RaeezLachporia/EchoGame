using UnityEngine;
using UnityEngine.AI;

// A shove landing on an enemy: a short physical knockback, then a helpless window.
//
// Added at RUNTIME by the ability that lands it (LaylaShove), so enemy prefabs need
// zero wiring — same deal as EnemyDebuff, and it destroys itself once the stagger
// runs out.
//
// Two phases, and they are NOT the same thing:
//
//   PUSH    — the enemy slides backwards over pushDuration. The NavMeshAgent is
//             switched OFF for this. An enabled agent rewrites transform.position
//             from its own path every frame, so the slide would be erased as fast
//             as it was written and the shove would look like nothing happened.
//
//   STAGGER — the agent is back on but held stopped, so they stand there helpless.
//             EnemyFollowPlayer and EnemyCombat both check IsStaggered and skip
//             their ticks, which is what stops them chasing AND swinging. Stopping
//             only the agent would leave them rooted but still swinging at anyone
//             who walked into range.
//
// This is a STAGGER, not a debuff: it does not touch EnemyDebuff and does not
// change incoming damage. The two are separate components with separate icons and
// can run on the same enemy at the same time.
public class EnemyStagger : MonoBehaviour, IStatusEffect
{
    private NavMeshAgent agent;
    private EnemyStatusIcons statusIcons;

    // Push phase
    private bool pushing;
    private Vector3 pushStart;
    private Vector3 pushEnd;
    private float pushDuration;
    private float pushElapsed;

    // Stagger phase
    private float staggerExpiresAt;
    private float totalDuration;
    private bool logStagger;

    // How far off the NavMesh the search for a landing spot reaches once the slide
    // ends. Generous enough to recover from being shoved over a ledge lip, tight
    // enough that it can't teleport them across a gap.
    private const float navSnapRadius = 2f;

    void Awake()
    {
        // Resolved here rather than on demand: Awake runs the moment AddComponent
        // creates us, so both are ready before the first Apply call lands.
        // Include-inactive, since the status panel may start hidden.
        agent = GetComponent<NavMeshAgent>();
        statusIcons = GetComponentInChildren<EnemyStatusIcons>(true);
    }

    // Helpless: can't chase, can't swing. True for the whole slide as well as the
    // stagger window after it — being mid-air on a shove obviously counts, even if
    // someone tunes pushDuration longer than staggerDuration.
    public bool IsStaggered => pushing || Time.time < staggerExpiresAt;

    public float SecondsRemaining => Mathf.Max(0f, staggerExpiresAt - Time.time);

    // IStatusEffect — lets status-aware UI ask "is anything running on this
    // character?" without knowing what a stagger is.
    public bool IsActive => IsStaggered;
    public StatusEffectKind Kind => StatusEffectKind.Stagger;
    public float RemainingNormalized =>
        totalDuration <= 0f ? 0f : Mathf.Clamp01(SecondsRemaining / totalDuration);

    // Re-applying keeps the LONGER stagger and restarts the slide from wherever
    // they are now, so a second shove can never cut the first one short.
    //
    // pushOffset is the FINAL, already wall-clipped displacement — the ability owns
    // the sweep, because it's the thing that knows how far the shove was meant to
    // travel and therefore how much of it a wall stole.
    public void Apply(Vector3 pushOffset, float pushSeconds, float staggerSeconds, bool log)
    {
        logStagger = log;

        staggerExpiresAt = Mathf.Max(staggerExpiresAt, Time.time + staggerSeconds);
        totalDuration = Mathf.Max(totalDuration, staggerSeconds);

        if (statusIcons != null) statusIcons.SetStaggerVisible(true);

        if (pushOffset.sqrMagnitude > 0.0001f)
        {
            pushStart = transform.position;
            pushEnd = pushStart + pushOffset;
            pushDuration = Mathf.Max(0f, pushSeconds);
            pushElapsed = 0f;
            pushing = true;

            // Hand the transform over for the slide. Guarded because a re-apply
            // mid-slide would otherwise disable an already-disabled agent.
            if (agent != null && agent.enabled) agent.enabled = false;
        }

        if (logStagger)
            Debug.Log($"[EnemyStagger] {name} STAGGERED — shoved {pushOffset.magnitude:F1}m, " +
                      $"helpless for {SecondsRemaining:F1}s.", this);
    }

    void Update()
    {
        if (pushing)
        {
            AdvancePush();
            return;
        }

        if (Time.time < staggerExpiresAt)
        {
            // Re-assert every frame rather than setting it once: this component is
            // the only thing that should be holding them still, so it shouldn't
            // depend on nobody else ever touching isStopped.
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            return;
        }

        if (logStagger)
            Debug.Log($"[EnemyStagger] {name} stagger EXPIRED — back in the fight.", this);
        Destroy(this);
    }

    private void AdvancePush()
    {
        pushElapsed += Time.deltaTime;

        // A zero-length push is legal (someone tuned Knockback Duration to nothing)
        // and must not divide by zero — it just snaps straight to the end.
        float t = pushDuration <= 0f ? 1f : Mathf.Clamp01(pushElapsed / pushDuration);
        transform.position = Vector3.Lerp(pushStart, pushEnd, t);

        if (t < 1f) return;

        pushing = false;
        EndPush();
    }

    // Hand the transform back to the agent. The slide moved them by raw transform
    // writes, which the NavMesh knows nothing about, so they can easily finish a
    // little off-mesh — snap to the nearest valid point before re-enabling, or the
    // agent comes back disabled-in-all-but-name and the enemy never moves again.
    private void EndPush()
    {
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navSnapRadius, NavMesh.AllAreas))
            transform.position = hit.position;

        if (agent == null) return;

        agent.enabled = true;
        if (!agent.isOnNavMesh) return;

        // Drop the stale path from before the shove — it was computed from the old
        // position and would walk them straight back where they were standing.
        agent.ResetPath();
        agent.isStopped = true;
    }

    // Pooled enemies keep their components across lives — without this, a reused
    // enemy would wake up still staggered, or worse, with its agent left disabled
    // from a slide that never got to finish.
    void OnDisable()
    {
        pushing = false;
        pushElapsed = 0f;
        pushDuration = 0f;
        staggerExpiresAt = 0f;
        totalDuration = 0f;
        RestoreAgent();
        if (statusIcons != null) statusIcons.SetStaggerVisible(false);
    }

    // Covers every way this component goes away — expiry above, or the enemy being
    // destroyed outright — so the icon and the agent can't outlive the stagger.
    void OnDestroy()
    {
        RestoreAgent();
        if (statusIcons != null) statusIcons.SetStaggerVisible(false);
    }

    // Idempotent on purpose: OnDisable and OnDestroy both run when the component is
    // destroyed, and a re-enabled agent must not be re-enabled twice.
    private void RestoreAgent()
    {
        if (agent == null) return;
        if (!agent.enabled) agent.enabled = true;
        if (agent.isOnNavMesh) agent.isStopped = false;
    }
}
