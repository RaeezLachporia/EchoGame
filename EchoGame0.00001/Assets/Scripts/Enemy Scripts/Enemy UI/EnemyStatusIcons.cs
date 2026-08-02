using UnityEngine;
using UnityEngine.UI;

// Status-effect icons on an enemy's floating panel.
//
// This lives on the enemy PREFAB so the Image references can be wired in the
// inspector. EnemyDebuff can't hold them itself: it's added at runtime by the
// ability that applies it, so any serialized field on it would always be empty.
// The runtime effect asks this component to show/hide instead.
//
// Add more slots here as further buffs/debuffs arrive (stun, burn, shield...).
public class EnemyStatusIcons : MonoBehaviour
{
    [Header("Icons")]
    [Tooltip("Drag the StatusEffect GAMEOBJECT from the enemy panel in the Hierarchy — NOT the sprite asset from the Project window. The sprite stays in that object's own Source Image; this slot just needs the Image so the code can switch it on and off. Hidden until a damage debuff is applied.")]
    [SerializeField] private Image debuffIcon;

    void Awake()
    {
        // Start hidden no matter how the prefab was left in the editor, so an enemy
        // never shows a status it doesn't actually have.
        SetDebuffVisible(false);
    }

    // Enemies are pooled — a reused one must not come back wearing the icon from
    // its previous life.
    void OnEnable()
    {
        SetDebuffVisible(false);
    }

    // Toggles the Image's renderer rather than the GameObject, so the panel's
    // layout doesn't reflow when the icon comes and goes.
    public void SetDebuffVisible(bool visible)
    {
        if (debuffIcon == null) return;
        debuffIcon.enabled = visible;
    }
}
