using UnityEngine;

// A per-character brain override. Sits in the CompanionCombatBrain's Extension
// slot on a specific companion's prefab; called BEFORE the vanilla Evaluate runs.
//
// The design contract: extensions ALWAYS win when they have an opinion.
// CombatProfile is the foundation of how a role behaves; the extension is what
// makes a given character themselves. If a berserker tank's extension says
// "chase this wounded enemy regardless of leash" and the vanilla brain would
// have said "no, leash break", the extension wins. The whole point of layering
// character logic on top of the profile is that the character logic gets the
// final say — otherwise the extension would be advisory noise.
//
// Extensions decline (return false) for any state they don't have an opinion
// about. The vanilla brain covers the case then. So a Naledi extension owning
// the Attack decision can still let the vanilla logic handle SelfPreserve.
//
// One extension per character. If two characters want the same logic, they can
// share one asset — or one class with per-character asset instances.
public abstract class CompanionBrainExtension : ScriptableObject
{
    // Return true to override the vanilla decision; the returned state/target
    // are used verbatim. Return false to let the vanilla Evaluate run. Target
    // is only read when state == Attack; leave null for the other states.
    public abstract bool TryEvaluate(in BrainContext ctx,
                                     out CompanionCombatBrain.BrainState state,
                                     out Transform target);
}

// Read-only view of everything an extension might need. Passed by ref (in) to
// avoid a struct copy — no allocations. Kept as a struct rather than exposing
// the brain itself so extensions can't reach into private state; if you find
// yourself wanting a field that isn't here, add it explicitly rather than
// widening the surface.
public readonly struct BrainContext
{
    public readonly CompanionCombatBrain brain;
    public readonly Transform self;
    public readonly Transform player;
    public readonly CombatProfile profile;
    public readonly CompanionThreatSensor sensor;
    public readonly float healthFraction;
    public readonly bool combatLatched;
    public readonly CompanionCombatBrain.BrainState currentState;

    public BrainContext(CompanionCombatBrain brain, Transform self, Transform player,
                        CombatProfile profile, CompanionThreatSensor sensor,
                        float healthFraction, bool combatLatched,
                        CompanionCombatBrain.BrainState currentState)
    {
        this.brain = brain;
        this.self = self;
        this.player = player;
        this.profile = profile;
        this.sensor = sensor;
        this.healthFraction = healthFraction;
        this.combatLatched = combatLatched;
        this.currentState = currentState;
    }
}
