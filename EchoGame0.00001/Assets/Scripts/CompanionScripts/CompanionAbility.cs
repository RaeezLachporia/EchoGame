using UnityEngine;

// How the command wheel sources the target for an ability. Kept top-level (like
// CompanionRole) so both abilities and the wheel can name it directly.
// APPEND-ONLY: add new kinds at the END so existing serialized data doesn't shift.
public enum AbilityTargetKind
{
    EnemyUnderReticle, // fire now on whatever's under the crosshair / lock (Attack)
    AllyPicker,        // open the ally wheel — player picks a companion or the protagonist (heal/buff)
    EnemyPicker,       // open the enemy-cycle reticle — player picks a nearby enemy (debuff)
}

// Base for every companion ability (Attack, Heal, and whatever comes later).
// Abilities are components on the companion's prefab.
// The command wheel uses their order on the prefab:
// 1st ability = TOP slice, 2nd = RIGHT, 3rd = BOTTOM, 4th = LEFT.
public abstract class CompanionAbility : MonoBehaviour
{
    [Header("Ability")]
    [Tooltip("The ability's name, e.g. \"Attack\" or \"Heal\".")]
    public string abilityName = "Ability";
    [Tooltip("Icon for this ability's wheel slice (not shown yet — for the future wheel UI).")]
    public Sprite icon;

    // The command wheel calls this when the player picks this slice.
    // target = the target the wheel resolved for this ability — the reticle enemy,
    // or a chosen ally/enemy from the picker layer. Can be null.
    // Return true if the ability actually started.
    public abstract bool TryActivate(Transform target);

    // Tells the wheel how to source this ability's target. Defaults to the reticle
    // enemy so existing abilities (Attack) keep firing immediately with no picker.
    public virtual AbilityTargetKind TargetKind => AbilityTargetKind.EnemyUnderReticle;

    // True while this ability is in charge of the companion.
    // Follow and wander check this and wait their turn.
    public virtual bool IsBusy => false;

    // For showing cooldowns on the wheel later.
    public virtual float CooldownRemaining => 0f;
}
