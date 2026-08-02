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
}
