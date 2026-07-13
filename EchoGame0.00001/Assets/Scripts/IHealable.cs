// Put this on anything that can be healed — companions and the player have it.
// It's the healing version of IDamageable.
public interface IHealable
{
    void Heal(float amount);
    float CurrentHealth { get; }
    float MaxHealth { get; }
}
