using UnityEngine;

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
    // target = whatever is under the crosshair (can be null).
    // Return true if the ability actually started.
    public abstract bool TryActivate(Transform target);

    // True while this ability is in charge of the companion.
    // Follow and wander check this and wait their turn.
    public virtual bool IsBusy => false;

    // For showing cooldowns on the wheel later.
    public virtual float CooldownRemaining => 0f;
}
