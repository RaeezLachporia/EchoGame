using UnityEngine;

// The baseline numbers for a ROLE, not a character. One asset per CompanionRole:
// TankTemplate, DamageTemplate, SupportTemplate, ControllerTemplate.
//
// A CombatProfile points at one of these and can copy its values in ("Sync From
// Template" on the profile's inspector). After that the profile owns its own
// numbers — this is a starting point and a reference to diff against, NOT a live
// parent. That's deliberate: a designer who spent an afternoon tuning one
// companion should never have that silently overwritten because someone rebalanced
// the role. The profile's inspector marks every field that differs from here, so
// the deviations stay visible instead of becoming invisible drift.
//
// Field names MUST stay identical to CombatProfile's. The sync and the diff both
// match by serialized property name, so a rename on one side silently drops that
// field out of the system.
//
// Right-click in the Project window > Create > EchoGame > Combat Profile Template.
[CreateAssetMenu(fileName = "NewCombatProfileTemplate", menuName = "EchoGame/Combat Profile Template")]
public class CombatProfileTemplate : ScriptableObject
{
    [Header("Archetype")]
    [Tooltip("Which CompanionRole this template is the baseline for. Documentation for whoever opens it — nothing branches on this at runtime.")]
    public CompanionRole role = CompanionRole.Damage;
    [Tooltip("Shown as the info banner on every CombatProfile using this template. Say what the archetype is TRYING to feel like, in plain English — six months from now this is the only record of intent.")]
    [TextArea(3, 8)] public string summary;

    // ---- Everything below mirrors CombatProfile field-for-field, minus the peel
    // priority block. Peel weights encode "who does this character go out of their
    // way for", which is a relationship between two specific companions and can
    // never come from a role.

    [Header("Engagement")]
    [Tooltip("Radius around the PLAYER that gets scanned for enemies. Enemies inside it exist as far as this companion is concerned.")]
    public float senseRadius = 14f;
    [Tooltip("Must be larger than Sense Radius. A threat has to leave THIS radius before it stops counting, so one parked at the edge doesn't flicker in and out every tick.")]
    public float disengageRadius = 18f;
    [Tooltip("How far from the player an enemy can be and still be worth attacking. This is the aggression dial: small = she anchors, large = she chases.")]
    public float engageRadius = 8f;

    [Header("Leash")]
    [Tooltip("Maximum distance from the player before she abandons her attack and walks back. An off-tank who runs across the map stops being a wall.")]
    public float leashRadius = 10f;
    [Tooltip("Must be smaller than Leash Radius. She only re-engages once she's back inside this, so she doesn't yo-yo across the boundary.")]
    public float leashResumeRadius = 7f;
    [Tooltip("Extra slack on Engage Radius before a target she's already fighting is dropped for drifting away.")]
    public float targetLeashSlack = 3f;

    [Header("Timing")]
    [Tooltip("Seconds between decisions. Not per frame — cheaper, and the slight lag is what stops her looking like a machine.")]
    public float scanInterval = 0.15f;
    [Tooltip("Seconds a new threat must persist before she commits to reacting. Low = a crisis responder who snaps; high = someone who watches first. This one value is most of a companion's personality.")]
    public float reactionDelay = 0.15f;

    [Header("Positioning")]
    [Tooltip("How far in front of the player she holds the line.")]
    public float anchorDistance = 2.75f;
    [Tooltip("Degrees to swing her anchor off the straight line to the threat. Keeps multiple brained companions from stacking on one point.")]
    public float anchorFanAngle = 0f;
    [Tooltip("-1, 0 or +1. Which way anchorFanAngle leans for this companion.")]
    [Range(-1, 1)] public int anchorFanSign = 0;
    [Tooltip("Move speed while repositioning under her own steam (anchoring, intercepting, backing off). Charging an enemy uses CompanionCommand's chase speed instead.")]
    public float moveSpeed = 4.5f;
    [Tooltip("Authored speed of the run animation, so footsteps stay synced while she repositions.")]
    public float moveAnimSpeed = 6f;
    public float rotationSpeed = 10f;

    [Header("Self-Preservation")]
    [Tooltip("Health fraction at which she pulls back. Low = she soaks more than she safely should, which for a tank is the point.")]
    [Range(0f, 1f)] public float selfPreserveEnter = 0.30f;
    [Tooltip("Health fraction she must climb back above before rejoining. Must exceed Enter — the gap is what stops chip damage flickering her in and out.")]
    [Range(0f, 1f)] public float selfPreserveExit = 0.45f;
    [Tooltip("How far from the player she retreats. She pulls to the edge of the fight, never out of it.")]
    public float selfPreserveRadius = 6f;

    [Header("Intercept")]
    [Tooltip("Off = she never body-blocks, she only anchors and attacks. Usually only Tanks want this on.")]
    public bool interceptEnabled = true;
    [Tooltip("How close an enemy must be to its victim before she throws herself in the way.")]
    public float interceptTriggerRange = 6f;
    [Tooltip("Extra standoff beyond the enemy's own reach when placing the block, so she's inside the swing rather than clipping its edge.")]
    public float blockPadding = 0.3f;
    [Tooltip("Never stand closer than this to whoever she's protecting — she's a shield, not a lapdog stood in their capsule.")]
    public float minPlayerClearance = 1f;
    [Tooltip("Assumed enemy reach when the enemy has no EnemyCombat to ask.")]
    public float fallbackEnemyReach = 2.3f;

    [Header("Handoff")]
    [Tooltip("Seconds with no threats before she hands the agent back to follow/wander. Covers the gap while the next enemy is still escalating.")]
    public float disengageLinger = 1.2f;
    [Tooltip("Seconds after handing off before she's allowed to take the agent again. This is what makes stutter structurally impossible rather than just unlikely.")]
    public float reEngageCooldown = 0.5f;

    [Header("Target Scoring")]
    [Tooltip("Weight for an enemy already attacking ME. Finish what's on you.")]
    public float weightTargetsMe = 1.5f;
    [Tooltip("Weight for an enemy attacking someone I'm protecting.")]
    public float weightTargetsAlly = 2f;
    [Tooltip("Weight for an enemy mid-swing — it's about to land a hit. Controllers push this up: a debuff landing mid-swing amplifies the hit that's already committed.")]
    public float weightSwinging = 1f;
    [Tooltip("Weight on how hurt the enemy is. Higher = more inclined to finish a wounded one. Damage roles push this up; Controllers push it DOWN — a dying enemy doesn't need a debuff spent on it.")]
    public float weightWounded = 1.2f;
    [Tooltip("Penalty for distance. Higher = strongly prefers what's already in reach.")]
    public float weightDistance = 1.5f;

    // Same invariants CombatProfile enforces. Catching them here means a broken
    // template can't seed four broken profiles before anyone notices.
    void OnValidate()
    {
        disengageRadius = Mathf.Max(disengageRadius, senseRadius + 0.5f);
        leashResumeRadius = Mathf.Min(leashResumeRadius, leashRadius - 0.5f);
        selfPreserveExit = Mathf.Max(selfPreserveExit, selfPreserveEnter + 0.05f);
        scanInterval = Mathf.Max(0.02f, scanInterval);
    }
}
