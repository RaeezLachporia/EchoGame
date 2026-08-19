// Whether an effect helps or harms the character carrying it. Status panels use
// this to pick which icon to show.
// APPEND-ONLY: add new kinds at the END so existing serialized data doesn't shift.
public enum StatusEffectKind
{
    Buff,
    Debuff,
    // Knocked back and helpless. Its own kind rather than a Debuff because it
    // doesn't change incoming damage — and because an enemy can carry a stagger
    // and a damage debuff at once, each with its own icon.
    Stagger,
}

// A temporary buff or debuff running on a character.
//
// UI asks for these rather than knowing about individual effects, so a panel can
// show "this character has something on them" without caring what it is. Any new
// buff/debuff component implements this and every status-aware UI picks it up for
// free — same idea as IDamageable / IHealable.
public interface IStatusEffect
{
    // False once the effect has run out but before its component is cleaned up.
    bool IsActive { get; }

    // Drives which icon the panel shows.
    StatusEffectKind Kind { get; }

    // 1 when freshly applied, falling to 0 as it expires. Drives the radial
    // "spin down" on a status icon (Image Type = Filled, Radial 360).
    float RemainingNormalized { get; }
}
