using UnityEngine;

// Naledi's character brain layer, sitting on top of the shared CombatProfile.
// She is defined by NOT wanting to fight — the vanilla brain would treat her as
// a support-shaped damage role and swing at anything in engage radius. This
// extension replaces that decision with three specific triggers:
//
//   1. Focused    — any enemy has targeted HER (through the Focus API, the taunt
//                   pipeline, or the passive combat-peel). Swing at that enemy;
//                   ignore radius/leash — being the designated victim isn't
//                   optional.
//   2. Emergency  — enough party members are down that pacifism is a liability.
//                   She drops the last-resort gate and engages like a normal
//                   damage role: whichever priority target the sensor scored.
//   3. Self-def   — an enemy is inside her lastResortAttackRadius (small, ~2 m).
//                   Nearest one, not sensor.Primary — a scared healer swings at
//                   whatever's on her, not at whatever's tactically "best".
//
// Falls through to the vanilla brain for everything else, so Anchor (positioning
// behind the player), SelfPreserve (health bail-out), Idle (nothing engaged) all
// keep working via CompanionCombatBrain.Evaluate. SelfPreserve intentionally
// still wins over these triggers — a downed healer helps no one.
[CreateAssetMenu(fileName = "Naledi", menuName = "EchoGame/Brain Extension/Naledi")]
public class NalediBrainExtension : CompanionBrainExtension
{
    [Header("Proximity Self-Defense")]
    [Tooltip("Enemies within this radius of HER trigger a swing. Smaller than the profile's engageRadius (which measures from the player). Naledi's should be tiny — she flinches at what's already on her, she doesn't chase.")]
    [SerializeField] private float lastResortAttackRadius = 2f;

    [Header("Focus Response")]
    [Tooltip("On = she must fight any enemy that specifically targets her (via Focus API, taunt, or proximity peel). Off = even being hunted doesn't wake her combat mode. Leave on for Naledi; a bard-type future healer might want it off.")]
    [SerializeField] private bool fightWhenFocused = true;

    [Header("Party Emergency")]
    [Tooltip("How many party members must be DOWN before she abandons pacifism and fights like a normal damage role. Player counts. She doesn't count herself. 0 = never trigger emergency.")]
    [SerializeField, Min(0)] private int partyDownEmergencyThreshold = 2;

    [Header("Debug")]
    [Tooltip("Log the specific trigger that pulled her into a fight, so 'why did she start swinging?' has an answer without stepping through frames.")]
    [SerializeField] private bool logTriggers = false;

    public override bool TryEvaluate(in BrainContext ctx,
                                     out CompanionCombatBrain.BrainState state,
                                     out Transform target)
    {
        state = CompanionCombatBrain.BrainState.Idle;
        target = null;

        if (ctx.sensor == null) return false;

        // Trigger 1: Focused. Any threat whose CurrentTarget is her — that
        // includes explicit SetFocusTarget calls, Layla-style taunt (though
        // taunt onto Naledi would be weird), and the passive combat-peel.
        // Pick the closest such threat so she defends against the one that
        // actually reached her first.
        if (fightWhenFocused && ctx.sensor.HasThreatTargetingMe)
        {
            if (TryPickClosestTargetingMe(ctx.sensor, out Transform focuser))
            {
                target = focuser;
                state = CompanionCombatBrain.BrainState.Attack;
                if (logTriggers) Debug.Log($"[Naledi] Fighting because focused on: {focuser.name}", ctx.self);
                return true;
            }
        }

        // Trigger 2: Party emergency. Iterate live companions + player and count
        // IsDown flags. Comapnion.IsDown / PlayerHealth.IsDown are stubs today
        // (return false) — this trigger stays dormant until the downed system
        // lands, at which point it activates without any change here.
        if (partyDownEmergencyThreshold > 0)
        {
            int downCount = CountDownedAllies(ctx.self);
            if (downCount >= partyDownEmergencyThreshold
                && ctx.sensor.HasPrimary
                && ctx.sensor.Primary.transform != null)
            {
                target = ctx.sensor.Primary.transform;
                state = CompanionCombatBrain.BrainState.Attack;
                if (logTriggers) Debug.Log($"[Naledi] Fighting because party emergency: {downCount} down.", ctx.self);
                return true;
            }
        }

        // Trigger 3: Proximity self-defense. Something is literally on her —
        // swing at the nearest one regardless of score. Sensor helper walks the
        // threat list; if nothing is inside the radius we decline and vanilla
        // Anchor / Idle handles the frame.
        if (lastResortAttackRadius > 0f
            && ctx.sensor.TryGetNearestSelfWithin(lastResortAttackRadius, out CompanionThreatSensor.Threat nearest))
        {
            target = nearest.transform;
            state = CompanionCombatBrain.BrainState.Attack;
            if (logTriggers) Debug.Log($"[Naledi] Fighting because in-face: {nearest.transform.name} at {nearest.distanceToSelf:F1}m", ctx.self);
            return true;
        }

        // No opinion — vanilla Anchor / Idle covers the frame.
        return false;
    }

    private static bool TryPickClosestTargetingMe(CompanionThreatSensor sensor, out Transform picked)
    {
        picked = null;
        float best = float.MaxValue;
        System.Collections.Generic.IReadOnlyList<CompanionThreatSensor.Threat> threats = sensor.Threats;
        for (int i = 0; i < threats.Count; i++)
        {
            CompanionThreatSensor.Threat t = threats[i];
            if (!t.targetsMe) continue;
            if (t.transform == null) continue;
            if (t.distanceToSelf >= best) continue;
            best = t.distanceToSelf;
            picked = t.transform;
        }
        return picked != null;
    }

    private static int CountDownedAllies(Transform self)
    {
        int count = 0;

        // Companions. Skip self so a downed Naledi doesn't count her own state
        // as an emergency (moot today, will matter once the downed state lands
        // and her brain is still running because she IS the one down).
        System.Collections.Generic.IReadOnlyList<Comapnion> allies = Comapnion.Active;
        for (int i = 0; i < allies.Count; i++)
        {
            Comapnion ally = allies[i];
            if (ally == null) continue;
            if (ally.transform == self) continue;
            if (ally.IsDown) count++;
        }

        // Player. Look them up on the same tag CompanionCombatBrain uses — she
        // is party-aware via the tag, not a serialized reference on the SO.
        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null)
        {
            PlayerHealth playerHealth = playerObject.GetComponent<PlayerHealth>();
            if (playerHealth != null && playerHealth.IsDown) count++;
        }

        return count;
    }
}
