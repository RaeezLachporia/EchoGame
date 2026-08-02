using UnityEngine;

// Floating panel above a companion. Goes ON the UI object itself (the panel), and
// shows/hides it based on what the player is doing.
//
// Visible when EITHER:
//   - the player is looking at this companion (it sits near the centre of the
//     view, within Show Distance, and isn't behind a wall if the LOS check is on),
//   - or the companion currently has a buff/debuff running (any IStatusEffect).
// Hidden otherwise, so the world isn't cluttered with permanent nameplates.
//
// Visibility is driven through a CanvasGroup rather than SetActive: this script
// lives on the panel, so deactivating that object would stop Update and the panel
// could never turn itself back on. Fading the group keeps the script alive (and
// gives a soft fade for free).
[RequireComponent(typeof(CanvasGroup))]
public class CompanionUI : MonoBehaviour
{
    [Header("Owner")]
    [Tooltip("The companion this panel belongs to. Auto-found from the parents if left empty — only set it by hand if the panel isn't a child of the companion.")]
    [SerializeField] private Transform owner;

    [Header("Name & Health")]
    [Tooltip("The HealthBarUi on this panel — the SAME component the on-screen HUD bars use, so the panel shows the identical name, numbers and formatting. Auto-found in this panel's children if left empty; leave the whole thing empty to skip name/health entirely.")]
    [SerializeField] private HealthBarUi healthBar;

    [Header("Show When Looked At")]
    [Tooltip("Untick to make the panel ignore where the player is looking (buffs/debuffs would then be the only thing that shows it).")]
    [SerializeField] private bool showWhenLookedAt = true;
    [Tooltip("How near the centre of the screen the companion must be, in degrees off the camera's forward. Bigger = more forgiving. Below ~25 the panel gets twitchy: a companion walking beside the player sits 20-40 degrees off centre, so it flickers as you turn. 35 reads as 'looking their way'.")]
    [SerializeField, Range(1f, 90f)] private float lookAngle = 35f;
    [Tooltip("Companions further away than this never show a panel from looking, however centred they are.")]
    [SerializeField] private float showDistance = 20f;
    [Tooltip("Tick to hide the panel when something solid is between the camera and the companion. Needs Sight Obstacles set to your wall/environment layers.")]
    [SerializeField] private bool requireLineOfSight = false;
    [Tooltip("Layers that block sight. Only used when Require Line Of Sight is ticked.")]
    [SerializeField] private LayerMask sightObstacles;
    [Tooltip("Raised off the companion's feet so the sight check aims at their body rather than the floor.")]
    [SerializeField] private float sightHeight = 1.2f;

    [Header("Show When Buffed / Debuffed")]
    [Tooltip("Tick so the panel appears on its own whenever a buff or debuff is running on this companion, even if the player is looking elsewhere.")]
    [SerializeField] private bool showWhenStatusActive = true;
    [Tooltip("Seconds between checks for buffs/debuffs. Effects get added at runtime, so this polls rather than caching. 0.25 is plenty responsive.")]
    [SerializeField, Min(0.05f)] private float statusPollInterval = 0.25f;

    [Header("Fade")]
    [Tooltip("How fast the panel fades in/out. Higher = snappier. Set very high for an instant pop.")]
    [SerializeField] private float fadeSpeed = 10f;

    [Header("Billboard")]
    [Tooltip("Tick to keep the panel turned to face the camera. Untick if something else already billboards this object.")]
    [SerializeField] private bool faceCamera = true;

    [Header("Debug")]
    [Tooltip("Log when the panel shows/hides and why (looked at vs status).")]
    [SerializeField] private bool logVisibility = false;

    private CanvasGroup group;
    private Camera cam;
    private float pollTimer;
    private bool statusActive;
    private bool lastVisible;
    private Comapnion body;
    private float shownHealth = -1f;
    private float shownMaxHealth = -1f;
    private string shownName;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        body = GetComponentInParent<Comapnion>();
        if (owner == null)
        {
            // The Comapnion body is the thing being looked at; fall back to our own
            // parent (or self) so this still works on a panel wired up differently.
            if (body != null) owner = body.transform;
            else owner = transform.parent != null ? transform.parent : transform;
        }

        if (healthBar == null) healthBar = GetComponentInChildren<HealthBarUi>(true);

        // Start hidden so a companion never flashes a panel on the first frame
        // before the first visibility check runs.
        group.alpha = 0f;
        SetInteractable(false);
    }

    void Start()
    {
        // A missing link here shows up as "the panel just doesn't fill in" with no
        // other symptom, so name the exact field that needs wiring instead.
        if (body == null)
        {
            Debug.LogWarning($"[CompanionUI] '{name}' found no Comapnion component on itself or any parent, so it has no name/health to show. Make this panel a CHILD of the companion, or assign Owner by hand.", this);
            return;
        }

        if (healthBar == null)
        {
            Debug.LogWarning($"[CompanionUI] '{name}' found no HealthBarUi on this panel or its children — name and health stay blank. Add a HealthBarUi component to the panel and wire its Slider / Name Label / Label, or drag one into the Health Bar field.", this);
            return;
        }

        // Seed the bar after Comapnion.Start has applied its definition, so the
        // panel opens on the right name and full health rather than placeholders.
        shownName = body.DisplayName;
        shownMaxHealth = body.MaxHealth;
        shownHealth = body.CurrentHealth;
        healthBar.SetName(shownName);
        healthBar.Initialize(shownMaxHealth, shownHealth);

        // Prints exactly what got resolved, so a blank panel can be traced to the
        // real cause — wrong companion, empty name, or the wrong HealthBarUi.
        if (logVisibility)
            Debug.Log($"[CompanionUI] '{name}' wired to companion '{body.name}' " +
                      $"DisplayName=\"{shownName}\" (len {(shownName == null ? -1 : shownName.Length)}), " +
                      $"health {shownHealth}/{shownMaxHealth}, using HealthBarUi on '{healthBar.name}'.", this);
    }

    void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        if (faceCamera) transform.forward = cam.transform.forward;

        // Health is two float compares — cheap enough to check every frame so the
        // bar never lags a hit. Name and status effects ride the slower poll.
        RefreshHealth();

        pollTimer -= Time.deltaTime;
        if (pollTimer <= 0f)
        {
            pollTimer = statusPollInterval;
            RefreshName();
            statusActive = showWhenStatusActive && HasActiveStatus();
        }

        // Declared up front, not inline: with Show When Looked At off, && skips the
        // call entirely and the out values would never be assigned.
        float lookAngleNow = 999f;
        float distanceNow = 999f;
        bool lookedAt = showWhenLookedAt && PlayerIsLookingAtOwner(out lookAngleNow, out distanceNow);
        bool visible = statusActive || lookedAt;

        if (logVisibility && visible != lastVisible)
        {
            // Report the measured angle/distance next to the thresholds, so a panel
            // that won't appear points straight at which limit is stopping it.
            Debug.Log($"[CompanionUI] {owner.name} panel {(visible ? "SHOWN" : "hidden")} — lookedAt={lookedAt} " +
                      $"(angle {lookAngleNow:F1}° vs Look Angle {lookAngle}°, distance {distanceNow:F1}m vs Show Distance {showDistance}m), " +
                      $"status={statusActive}.", this);
            lastVisible = visible;
        }

        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
        SetInteractable(group.alpha > 0.5f);
    }

    // Both this panel and the on-screen HUD bar read Comapnion's own health, so the
    // two can't drift apart — whatever damages or heals the companion updates the
    // single value both are showing.
    private void RefreshHealth()
    {
        if (healthBar == null || body == null) return;

        if (!Mathf.Approximately(body.MaxHealth, shownMaxHealth))
        {
            shownMaxHealth = body.MaxHealth;
            // refill: false — keep the current value, we push it below.
            healthBar.SetMaxHealth(shownMaxHealth, false);
            shownHealth = -1f; // force the health push so the bar rescales correctly
        }

        if (!Mathf.Approximately(body.CurrentHealth, shownHealth))
        {
            shownHealth = body.CurrentHealth;
            healthBar.SetHealth(shownHealth);
        }
    }

    // Picks up a name arriving late — e.g. the spawner calling Initialize with a
    // definition after this panel already seeded itself.
    private void RefreshName()
    {
        if (healthBar == null || body == null) return;
        if (body.DisplayName == shownName) return;
        shownName = body.DisplayName;
        healthBar.SetName(shownName);
    }

    // "Looking at" = the companion sits within lookAngle of where the camera points,
    // is close enough to matter, and (optionally) isn't behind cover. Angle rather
    // than a screen-space box so it behaves the same at any resolution or FOV.
    private bool PlayerIsLookingAtOwner(out float angle, out float distance)
    {
        angle = 999f;
        distance = 999f;
        if (owner == null) return false;

        Vector3 toOwner = owner.position - cam.transform.position;
        distance = toOwner.magnitude;
        if (distance < 0.001f) { angle = 0f; return true; }

        angle = Vector3.Angle(cam.transform.forward, toOwner);
        if (distance > showDistance) return false;
        if (angle > lookAngle) return false;

        if (requireLineOfSight)
        {
            Vector3 target = owner.position + Vector3.up * sightHeight;
            Vector3 dir = target - cam.transform.position;
            if (Physics.Raycast(cam.transform.position, dir.normalized, dir.magnitude, sightObstacles, QueryTriggerInteraction.Ignore))
                return false;
        }

        return true;
    }

    // Buffs and debuffs are added at runtime, so this polls instead of caching a
    // reference that would go stale the moment an effect is applied or expires.
    private bool HasActiveStatus()
    {
        if (owner == null) return false;
        IStatusEffect[] effects = owner.GetComponentsInChildren<IStatusEffect>(true);
        for (int i = 0; i < effects.Length; i++)
            if (effects[i] != null && effects[i].IsActive) return true;
        return false;
    }

    // Keep a faded-out panel from swallowing clicks / raycasts.
    private void SetInteractable(bool on)
    {
        group.interactable = on;
        group.blocksRaycasts = on;
    }
}
