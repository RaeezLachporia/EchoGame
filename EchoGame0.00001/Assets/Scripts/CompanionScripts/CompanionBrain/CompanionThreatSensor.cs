using System.Collections.Generic;
using UnityEngine;

// What a companion knows about the fight. Deliberately has no Update of its own —
// CompanionCombatBrain calls Scan() on its decision tick, so there is exactly one
// clock and the two can never drift out of phase with each other.
//
// Why polling and not EnemyFollowPlayer.StateChanged: enemies are pooled, and
// their OnEnable resets state by direct assignment specifically so the event does
// NOT fire on reuse. There's also no OnDisable to unsubscribe from, so subscribers
// leak and pin dead enemies alive. A snapshot every ~0.15s sidesteps both problems
// and costs nothing at these enemy counts. Don't "optimise" this into an event.
[DisallowMultipleComponent]
public class CompanionThreatSensor : MonoBehaviour
{
    // One enemy, as this companion sees it.
    public struct Threat
    {
        public EnemyFollowPlayer enemy;
        public Transform transform;
        public Transform victim;          // who it's hunting: the player, or one of us
        public float distanceToSelf;
        public float distanceToVictim;
        public bool isEngaged;            // Combat state — the only state that can actually swing
        public bool targetsMe;
        public bool targetsAlly;          // someone worth stepping in front of
        public bool isSwinging;
        public float healthFraction;
        public float score;
    }

    [Header("Detection")]
    [Tooltip("Layers enemies live on. Set to EnemyLayer. Leaving this empty makes the whole brain a silent no-op.")]
    [SerializeField] private LayerMask enemyMask;

    [Header("Debug")]
    [Tooltip("Draw a line to every threat while this companion is selected. Red = fighting me, orange = fighting an ally, yellow = engaged elsewhere, grey = not yet in the fight.")]
    [SerializeField] private bool drawGizmos = true;
    [Tooltip("Log the chosen target every time it changes.")]
    [SerializeField] private bool logTargeting = false;

    // Sized for a generous brawl. OverlapSphereNonAlloc silently truncates rather
    // than growing, which is the right failure: 64 enemies in one radius is a
    // design problem, not a buffer problem.
    private readonly Collider[] overlapBuffer = new Collider[64];
    private readonly List<Threat> threats = new List<Threat>();

    private Comapnion self;
    private CombatProfile profile;
    private Transform player;
    private Transform lastLoggedTarget;

    public IReadOnlyList<Threat> Threats => threats;
    public int Count => threats.Count;

    // Highest-scoring enemy — who she should be punching.
    public Threat Primary { get; private set; }
    public bool HasPrimary { get; private set; }

    // The engaged enemy closest to landing a hit on someone she's protecting.
    // This drives body-blocking, which is why it ignores enemies already on her.
    public Threat MostImminent { get; private set; }
    public bool HasImminent { get; private set; }

    // True while anything nearby is actually in Combat. The brain's master switch:
    // she never starts a fight, she only joins one that already exists.
    public bool AnyEngaged { get; private set; }

    // Mean position of everything currently engaged — the direction of "the fight".
    public Vector3 EngagedCentroid { get; private set; }

    // True if any threat's chosen victim is HER — reads the flag Describe already
    // computes off enemy.CurrentTarget, so this is essentially free. Brain
    // extensions use it to detect the Focus system: whenever an enemy sets its
    // focus onto this companion, CurrentTarget flips and targetsMe goes true.
    public bool HasThreatTargetingMe
    {
        get
        {
            for (int i = 0; i < threats.Count; i++)
                if (threats[i].targetsMe) return true;
            return false;
        }
    }

    // Pick the closest threat within a radius of HER, not the player. Used by
    // pacifist extensions like Naledi's — when she does fight, she doesn't
    // strategise, she swings at whatever is on her. Score-based Primary would
    // stay pinned on a wounded enemy further off while a healthy one bites her.
    public bool TryGetNearestSelfWithin(float radius, out Threat nearest)
    {
        nearest = default;
        if (radius <= 0f) return false;

        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < threats.Count; i++)
        {
            Threat t = threats[i];
            if (t.transform == null) continue;
            if (t.distanceToSelf > radius) continue;
            if (t.distanceToSelf >= best) continue;
            best = t.distanceToSelf;
            nearest = t;
            found = true;
        }
        return found;
    }

    public void Initialize(Comapnion owner, CombatProfile combatProfile, Transform playerTransform)
    {
        self = owner;
        profile = combatProfile;
        player = playerTransform;

        // An unset mask finds nothing, forever, and looks exactly like a broken
        // brain. Same warning PietDoubletap gives for the same reason.
        if (enemyMask == 0)
            Debug.LogWarning($"[CompanionThreatSensor] Enemy Mask on '{name}' is empty — no enemy will ever be detected and this companion will never fight on her own. Set it to EnemyLayer.", this);
    }

    public void Scan(float senseRadius)
    {
        threats.Clear();
        HasPrimary = false;
        HasImminent = false;
        AnyEngaged = false;
        EngagedCentroid = Vector3.zero;

        if (player == null || profile == null) return;

        // Centred on the PLAYER, not on her. Her job is defined relative to them,
        // so a player-anchored sphere returns the right fight even when she's
        // drifted to the edge of it — and the leash shares the same origin.
        int hits = Physics.OverlapSphereNonAlloc(player.position, senseRadius, overlapBuffer, enemyMask, QueryTriggerInteraction.Ignore);

        float bestScore = float.MinValue;
        float nearestImminent = float.MaxValue;
        int engagedCount = 0;
        Vector3 engagedSum = Vector3.zero;

        for (int i = 0; i < hits; i++)
        {
            Collider hit = overlapBuffer[i];
            if (hit == null) continue;

            // GetComponentInParent so a future child hitbox still resolves to the brain.
            EnemyFollowPlayer enemy = hit.GetComponentInParent<EnemyFollowPlayer>();
            if (enemy == null) continue;
            if (!enemy.gameObject.activeInHierarchy) continue;

            Threat threat = Describe(enemy);
            threats.Add(threat);

            if (threat.isEngaged)
            {
                engagedCount++;
                engagedSum += threat.transform.position;
            }

            if (threat.score > bestScore)
            {
                bestScore = threat.score;
                Primary = threat;
                HasPrimary = true;
            }

            // Body-blocking only makes sense against something that can actually
            // swing, at someone other than her, and that hasn't already committed
            // the swing she'd be trying to prevent.
            if (threat.isEngaged && threat.targetsAlly && !threat.isSwinging
                && threat.distanceToVictim < nearestImminent)
            {
                nearestImminent = threat.distanceToVictim;
                MostImminent = threat;
                HasImminent = true;
            }
        }

        AnyEngaged = engagedCount > 0;
        EngagedCentroid = engagedCount > 0 ? engagedSum / engagedCount : player.position;

        if (logTargeting && HasPrimary && Primary.transform != lastLoggedTarget)
        {
            lastLoggedTarget = Primary.transform;
            Debug.Log($"[CompanionThreatSensor] {name} priority target -> {Primary.transform.name} (score {Primary.score:F2}, {threats.Count} threats).", this);
        }
    }

    private Threat Describe(EnemyFollowPlayer enemy)
    {
        Threat threat = new Threat
        {
            enemy = enemy,
            transform = enemy.transform,
            // Only a Combat enemy can swing — EnemyCombat refuses to attack in any
            // other state. Alert stalkers are visible to her as targets but never
            // count as something worth throwing herself in front of.
            isEngaged = enemy.State == EnemyFollowPlayer.EnemyState.Combat
        };

        threat.victim = threat.isEngaged ? enemy.CurrentTarget : null;
        threat.distanceToSelf = Vector3.Distance(transform.position, threat.transform.position);
        threat.distanceToVictim = threat.victim != null
            ? Vector3.Distance(threat.transform.position, threat.victim.position)
            : float.MaxValue;

        threat.targetsMe = threat.victim == transform;
        threat.targetsAlly = threat.victim != null && !threat.targetsMe && IsWorthProtecting(threat.victim);

        EnemyCombat combat = enemy.GetComponent<EnemyCombat>();
        threat.isSwinging = combat != null && combat.isAttacking;

        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        threat.healthFraction = health != null ? health.HealthFraction : 1f;

        threat.score = Score(threat);
        return threat;
    }

    // Tanks can look after themselves — this is the one place CompanionRole is
    // read at runtime, and deliberately the only one. Behaviour branching on role
    // is a much bigger commitment than a tuning asset; this is just triage.
    private bool IsWorthProtecting(Transform victim)
    {
        if (player != null && victim == player) return true;

        Comapnion ally = victim.GetComponentInParent<Comapnion>();
        if (ally == null || ally == self) return false;
        if (ally.Definition != null && ally.Definition.role == CompanionRole.Tank) return false;
        return true;
    }

    private float Score(in Threat threat)
    {
        float score = 0f;
        if (threat.targetsMe) score += profile.weightTargetsMe;
        if (threat.targetsAlly) score += profile.weightTargetsAlly * PeelWeight(threat.victim);
        if (threat.isSwinging) score += profile.weightSwinging;

        // Finish the hurt one.
        score += profile.weightWounded * (1f - threat.healthFraction);

        // Prefer what's already in reach — running past one enemy to reach another
        // is how a wall stops being a wall.
        float senseRadius = Mathf.Max(0.01f, profile.senseRadius);
        score -= profile.weightDistance * (threat.distanceToSelf / senseRadius);

        // Something that hasn't joined the fight is a target of last resort. It
        // still scores, so she'll punch an Alert straggler when nothing else is
        // happening, but never over something actively hitting someone.
        if (!threat.isEngaged) score -= 2f;

        return score;
    }

    private float PeelWeight(Transform victim)
    {
        if (victim == null) return profile.defaultPeelWeight;
        Comapnion ally = victim.GetComponentInParent<Comapnion>();
        if (ally == null || ally.Definition == null) return profile.defaultPeelWeight;
        return profile.PeelWeightFor(ally.Definition.id);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || threats.Count == 0) return;

        Vector3 from = transform.position + Vector3.up;
        for (int i = 0; i < threats.Count; i++)
        {
            Threat threat = threats[i];
            if (threat.transform == null) continue;

            Gizmos.color = !threat.isEngaged ? new Color(0.5f, 0.5f, 0.5f, 0.5f)
                         : threat.targetsMe ? Color.red
                         : threat.targetsAlly ? new Color(1f, 0.55f, 0.1f)
                         : Color.yellow;
            Gizmos.DrawLine(from, threat.transform.position + Vector3.up);
        }

        if (HasPrimary && Primary.transform != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(Primary.transform.position + Vector3.up * 2f, 0.4f);
        }
    }
}
