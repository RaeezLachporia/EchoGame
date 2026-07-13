using UnityEngine;

public class CompanionHealthHud : MonoBehaviour
{
    public static CompanionHealthHud Instance { get; private set; }

    [SerializeField] private HealthBarUi[] slots = new HealthBarUi[4];

    [Tooltip("Drag the CompanionDatabase asset here so companions keep the same bar order as the list — first in the list gets the top bar.")]
    [SerializeField] private CompanionDatabase database;

    private readonly Object[] occupants = new Object[4];

    void Awake()
    {
        // Stray copies of this component (e.g. accidentally added to a companion
        // GameObject) have empty slots but can still win the singleton race —
        // the loser branch used to Destroy(gameObject), which nuked the real
        // HUD's UI and any other companion. Only claim Instance with wired
        // slots, and destroy just the duplicate component, not its host.
        bool hasSlots = false;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) { hasSlots = true; break; }

        if (!hasSlots || (Instance != null && Instance != this))
        {
            Destroy(this);
            return;
        }
        Instance = this;

        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) slots[i].Hide();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public int Claim(Object owner, string displayName, float maxHealth, float currentHealth, CompanionDefinition definition = null)
    {
        if (owner == null) return -1;

        // This companion already has a bar? Just update it.
        for (int i = 0; i < slots.Length; i++)
        {
            if (occupants[i] == owner)
                return Assign(i, owner, displayName, maxHealth, currentHealth);
        }

        // Give them the bar matching their spot in the database list, so the
        // HUD order matches the list instead of whoever happened to load first.
        if (database != null && definition != null)
        {
            int preferred = database.allCompanions.IndexOf(definition);
            if (preferred >= 0 && preferred < slots.Length && slots[preferred] != null && occupants[preferred] == null)
                return Assign(preferred, owner, displayName, maxHealth, currentHealth);
        }

        // Otherwise: first free bar.
        for (int i = 0; i < slots.Length; i++)
        {
            if (occupants[i] == null && slots[i] != null)
                return Assign(i, owner, displayName, maxHealth, currentHealth);
        }

        Debug.LogWarning("CompanionHealthHud: no free slots.");
        return -1;
    }

    private int Assign(int slot, Object owner, string displayName, float maxHealth, float currentHealth)
    {
        occupants[slot] = owner;
        slots[slot].SetName(displayName);
        slots[slot].Initialize(maxHealth, currentHealth);
        slots[slot].Show();
        return slot;
    }

    public void SetHealth(int slot, float value)
    {
        if (!IsValid(slot)) return;
        slots[slot].SetHealth(value);
    }

    public void SetMaxHealth(int slot, float value, bool refill = true)
    {
        if (!IsValid(slot)) return;
        slots[slot].SetMaxHealth(value, refill);
    }

    public void Release(int slot)
    {
        if (!IsValid(slot)) return;
        occupants[slot] = null;
        slots[slot].Hide();
    }

    bool IsValid(int slot)
    {
        return slot >= 0 && slot < slots.Length && slots[slot] != null;
    }
}
