using UnityEngine;
using UnityEngine.UI;

// Status-effect icon on an enemy's floating panel.
//
// One shared Image renderer, two sprite slots — the code picks which sprite to
// show based on what's active. Matches how CompanionUI drives the ally-side
// status icon, so the two sides read the same way in the inspector.
//
// This lives on the enemy PREFAB so the wiring can be done in the inspector.
// EnemyDebuff and EnemyStagger can't hold these references themselves: they're
// added at runtime by the abilities that apply them, so any serialized field on
// them would always be empty. The runtime effects call SetDebuffVisible /
// SetStaggerVisible instead.
//
// Both effects can run on the same enemy at once (Zara's debuff + Layla's shove).
// Only one icon shows at a time and STAGGER wins the tie — "helpless" is the more
// urgent thing for the player to see than "damage amplified". The debuff icon
// pops back the moment the stagger expires.
public class EnemyStatusIcons : MonoBehaviour
{
    [Header("Renderer")]
    [Tooltip("The single Image on the enemy panel that shows the active status. Drag the GAMEOBJECT (the Image) from the enemy panel in the Hierarchy. Its Source Image is overwritten by the sprites below whenever an effect turns on.")]
    [SerializeField] private Image statusIcon;

    [Header("Sprites")]
    [Tooltip("Shown while a damage-amp debuff (Zara) is running. Drag the sprite asset from the Project window.")]
    [SerializeField] private Sprite debuffSprite;
    [Tooltip("Shown while a shove stagger (Layla) is running. Outranks the debuff sprite when both are active, since helpless is the more urgent thing to signal.")]
    [SerializeField] private Sprite staggerSprite;

    private bool debuffActive;
    private bool staggerActive;

    void Awake()
    {
        // Start hidden no matter how the prefab was left in the editor, so an enemy
        // never shows a status it doesn't actually have.
        debuffActive = false;
        staggerActive = false;
        Refresh();
    }

    // Enemies are pooled — a reused one must not come back wearing the icon from
    // its previous life.
    void OnEnable()
    {
        debuffActive = false;
        staggerActive = false;
        Refresh();
    }

    // Driven by EnemyDebuff while a damage-amp is running.
    public void SetDebuffVisible(bool visible)
    {
        debuffActive = visible;
        Refresh();
    }

    // Driven by EnemyStagger while a shove's knockback and helpless window run.
    public void SetStaggerVisible(bool visible)
    {
        staggerActive = visible;
        Refresh();
    }

    // Pick the highest-priority active sprite and show it. Toggles the Image's
    // renderer rather than the GameObject so the panel's layout doesn't reflow
    // when the icon comes and goes.
    private void Refresh()
    {
        if (statusIcon == null) return;

        Sprite next = null;
        if (staggerActive && staggerSprite != null) next = staggerSprite;
        else if (debuffActive && debuffSprite != null) next = debuffSprite;

        if (next != null)
        {
            statusIcon.sprite = next;
            statusIcon.enabled = true;
        }
        else
        {
            statusIcon.enabled = false;
        }
    }
}
