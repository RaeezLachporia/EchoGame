using System.Collections.Generic;
using UnityEngine;

// One asset per companion. Everything that makes Layla fight like Layla lives in
// here as numbers — CompanionCombatBrain's decision loop is shared and never
// forks per character. When Piet or Naledi get a brain, they get their own
// profile asset and the same loop reads it.
//
// Start one from a CombatProfileTemplate (one per role) rather than from scratch:
// assign the template, hit "Sync From Template", then override only the handful of
// fields that make this character themselves. Sync is a one-shot copy, not live
// inheritance — see CombatProfileTemplate for why.
//
// Right-click in the Project window > Create > EchoGame > Combat Profile.
[CreateAssetMenu(fileName = "NewCombatProfile", menuName = "EchoGame/Combat Profile")]
public class CombatProfile : ScriptableObject
{
    // One entry per companion this character will go out of their way for. The id
    // matches CompanionDefinition.id ("layla", "naledi", ...). Nobody has to script
    // "Layla and Naledi are close" — a 1.3 here is that relationship, expressed as
    // a half-beat sooner and a couple of metres further.
    [System.Serializable]
    public struct PeelWeight
    {
        [Tooltip("CompanionDefinition.id of the ally, e.g. \"naledi\".")]
        public string companionId;
        [Tooltip("Multiplier on how urgently this ally gets protected. >1 = prioritised.")]
        public float weight;
    }

    [Header("Archetype")]
    [Tooltip("The role baseline this character started from. Purely an authoring aid — nothing reads it at runtime. Its inspector marks every field below that differs from the template, and offers a one-click resync.")]
    public CombatProfileTemplate template;
    [Tooltip("What this character is supposed to FEEL like, in two sentences. The numbers below drift over a production; this is the only record of what they were drifting toward.")]
    [TextArea(2, 5)] public string archetypeNote;

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
    [Tooltip("Where relative to the player she anchors. +1 = fully in front toward the fight (tanks — the default). 0 = beside the player. -1 = fully behind, using the player as cover (backline supports). Universal knob because every role positions SOMEWHERE relative to the party — the pacifism/self-defense triggers that would suit a specific character go on that character's brain extension, not here.")]
    [Range(-1f, 1f)] public float anchorForwardBias = 1f;
    [Tooltip("Move speed while repositioning under her own steam (anchoring, intercepting, backing off). Charging an enemy uses CompanionCommand's chase speed instead.")]
    public float moveSpeed = 4.5f;
    [Tooltip("Authored speed of the run animation, so footsteps stay synced while she repositions.")]
    public float moveAnimSpeed = 6f;
    public float rotationSpeed = 10f;

    [Header("Self-Preservation")]
    [Tooltip("Health fraction at which she pulls back. Low = she soaks more than she safely should, which for a tank is the point.")]
    [Range(0f, 1f)] public float selfPreserveEnter = 0.30f;
    [Tooltip("Health fraction she must climb back above before rejoining. Must exceed Enter. NOTE: Layla's own taunt grants +150 max AND current health, which jumps her fraction mid-fight — the gap between these two values is what makes that read as 'the taunt steadied her' instead of a flicker.")]
    [Range(0f, 1f)] public float selfPreserveExit = 0.45f;
    [Tooltip("How far from the player she retreats. She pulls to the edge of the fight, never out of it.")]
    public float selfPreserveRadius = 6f;

    [Header("Intercept")]
    [Tooltip("Off = she never body-blocks, she only anchors and attacks.")]
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
    [Tooltip("Weight for an enemy mid-swing — it's about to land a hit.")]
    public float weightSwinging = 1f;
    [Tooltip("Weight on how hurt the enemy is. Higher = more inclined to finish a wounded one.")]
    public float weightWounded = 1.2f;
    [Tooltip("Penalty for distance. Higher = strongly prefers what's already in reach.")]
    public float weightDistance = 1.5f;

    [Header("Peel Priority")]
    [Tooltip("Baseline urgency for allies not listed below.")]
    public float defaultPeelWeight = 1f;
    [Tooltip("Per-ally overrides. Layla covers everyone, so keep these at or above 1 — playing favourites in a crisis isn't who she is; some people just get reached first.")]
    public List<PeelWeight> peelWeights = new List<PeelWeight>();

    public float PeelWeightFor(string companionId)
    {
        if (!string.IsNullOrEmpty(companionId))
        {
            for (int i = 0; i < peelWeights.Count; i++)
                if (peelWeights[i].companionId == companionId) return peelWeights[i].weight;
        }
        return defaultPeelWeight;
    }

    // Catches the ordering mistakes that turn into stutter or a companion who
    // never re-engages, at author time instead of after twenty minutes of play.
    void OnValidate()
    {
        disengageRadius = Mathf.Max(disengageRadius, senseRadius + 0.5f);
        leashResumeRadius = Mathf.Min(leashResumeRadius, leashRadius - 0.5f);
        selfPreserveExit = Mathf.Max(selfPreserveExit, selfPreserveEnter + 0.05f);
        scanInterval = Mathf.Max(0.02f, scanInterval);
    }
}
