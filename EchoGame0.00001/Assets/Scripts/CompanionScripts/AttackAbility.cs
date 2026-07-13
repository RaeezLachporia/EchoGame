using UnityEngine;

// Makes ATTACK show up as an ability on the command wheel.
// Add it as the FIRST ability on the prefab so attack stays on the TOP slice.
// Companions without any abilities can still attack (the wheel falls back to
// the old way), so only add this once a companion gets a second ability.
[RequireComponent(typeof(CompanionCommand))]
public class AttackAbility : CompanionAbility
{
    private CompanionCommand command;

    void Reset()
    {
        abilityName = "Attack";
    }

    void Awake()
    {
        command = GetComponent<CompanionCommand>();
    }

    public override bool TryActivate(Transform target)
    {
        if (target == null) return false; // no enemy aimed at — nothing to attack
        command.CommandAttack(target);
        return true;
    }

    public override bool IsBusy => command != null && command.HasActiveCommand;
}
