using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    [Tooltip("SIMPLEST OPTION: drag your name text here (e.g. CompanionName) and the companion's name from its Companion Definition asset is written into it. Leave empty if you'd rather drive everything through a HealthBarUi below.")]
    [SerializeField] private TMP_Text nameLabel;
    [Tooltip("Drag your health Slider here (e.g. CompanionHealth). Optional.")]
    [SerializeField] private Slider healthSlider;
    [Tooltip("Optional text for the numbers, shown as \"340 / 400\".")]
    [SerializeField] private TMP_Text healthLabel;
    [Tooltip("OPTIONAL alternative: a HealthBarUi — the same component the on-screen HUD bars use. Auto-found on this panel if present. Anything wired above is filled in as well, so you can use either or both.")]
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

    [Header("Cast Bar")]
    [Tooltip("The cast bar Image (Image Type = Filled). Its Fill Amount sweeps 0 to 1 over the cast, and the object is hidden while nothing is casting.")]
    [SerializeField] private Image castFill;

    [Header("Status Icon")]
    [Tooltip("Drag the StatusEffect IMAGE from the panel here. It stays hidden until a buff or debuff is running, then swaps to the matching sprite below. Without this wired the image just sits there permanently visible.")]
    [SerializeField] private Image statusIcon;
    [Tooltip("Sprite shown while a BUFF is on this companion.")]
    [SerializeField] private Sprite buffIcon;
    [Tooltip("Sprite shown while a DEBUFF is on this companion. A debuff outranks a buff if somehow both are running.")]
    [SerializeField] private Sprite debuffIcon;

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
    // Abilities live on the companion root; cached because they never change at
    // runtime, unlike status effects which get added and removed constantly.
    private CompanionAbility[] abilities;
    // The effect currently driving the icon. Found on the slow poll, then read
    // every frame so the radial countdown drains smoothly.
    private IStatusEffect activeEffect;
    private float nextCastLogAt;

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
        abilities = body != null
            ? body.GetComponentsInChildren<CompanionAbility>(true)
            : System.Array.Empty<CompanionAbility>();

        // Start hidden so a companion never flashes a panel on the first frame
        // before the first visibility check runs.
        group.alpha = 0f;
        SetInteractable(false);

        // Hide the status icon immediately. Left to the first poll it would show
        // for a fraction of a second, and if it's never wired it would otherwise
        // sit visible forever regardless of whether anything is applied.
        if (statusIcon != null) statusIcon.enabled = false;
        SetCastBar(false, 0f);

        if (statusIcon != null && statusIcon.type != Image.Type.Filled)
            Debug.LogWarning($"[CompanionUI] Status icon '{statusIcon.name}' has Image Type = {statusIcon.type}. Set it to FILLED (Fill Method: Radial 360) if you want the buff/debuff icon to wind down; ignore this if you want a plain icon.", statusIcon);
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

        if (!HasAnyDisplay())
        {
            Debug.LogWarning($"[CompanionUI] '{name}' has nothing to write into — drag your name text into the Name Label field (and optionally the Slider), or put a HealthBarUi on this panel.", this);
            return;
        }

        // Same text object in both slots = the health numbers overwrite the name
        // every frame, so the panel shows "100 / 100" where the name should be.
        // Drop the health text so the name survives, and say why.
        if (nameLabel != null && nameLabel == healthLabel)
        {
            Debug.LogWarning($"[CompanionUI] '{name}' has the SAME text object ('{nameLabel.name}') in both Name Label and Health Label, so the numbers were overwriting the name. Ignoring Health Label — give the numbers their own text object, or leave Health Label empty.", this);
            healthLabel = null;
        }

        // Seed after Comapnion.Awake has applied its definition, so the panel opens
        // on the name from the Companion Definition asset rather than placeholders.
        shownName = body.DisplayName;
        shownMaxHealth = body.MaxHealth;
        shownHealth = body.CurrentHealth;
        PushName(shownName);
        PushHealth(shownHealth, shownMaxHealth);

        // Prints exactly what got resolved, so a blank panel traces to the real
        // cause — wrong companion, empty name, or nothing wired to write into.
        if (logVisibility)
            Debug.Log($"[CompanionUI] '{name}' wired to companion '{body.name}' " +
                      $"DisplayName=\"{shownName}\" (len {(shownName == null ? -1 : shownName.Length)}), " +
                      $"health {shownHealth}/{shownMaxHealth}. Writing to: " +
                      $"nameLabel={(nameLabel != null ? nameLabel.name : "none")}, " +
                      $"healthLabel={(healthLabel != null ? healthLabel.name : "none")}, " +
                      $"slider={(healthSlider != null ? healthSlider.name : "none")}, " +
                      $"mode={(UsingDirectFields ? "direct fields (HealthBarUi ignored)" : "HealthBarUi " + (healthBar != null ? healthBar.name : "none"))}.", this);
    }

    private bool HasAnyDisplay()
    {
        return nameLabel != null || healthSlider != null || healthLabel != null || healthBar != null;
    }

    // Direct references WIN over a HealthBarUi, and the two are never used together.
    // Driving both meant HealthBarUi's own Label field could point at the same text
    // object as our Name Label, and its health numbers would overwrite the name.
    private bool UsingDirectFields => nameLabel != null || healthSlider != null || healthLabel != null;

    private void PushName(string displayName)
    {
        if (UsingDirectFields)
        {
            if (nameLabel != null) nameLabel.text = displayName;
            return;
        }
        if (healthBar != null) healthBar.SetName(displayName);
    }

    private void PushHealth(float current, float max)
    {
        if (UsingDirectFields)
        {
            if (healthSlider != null)
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = max;
                healthSlider.value = current;
            }
            if (healthLabel != null)
                healthLabel.text = Mathf.CeilToInt(current) + " / " + Mathf.CeilToInt(max);
            return;
        }
        // Initialize rather than SetHealth: it rescales the bar's own slider too, so
        // a max-health change lands correctly without a separate call.
        if (healthBar != null) healthBar.Initialize(max, current);
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
            // The icon follows what's actually applied; showWhenStatusActive only
            // decides whether that also forces the whole panel open.
            activeEffect = FindActiveStatus();
            statusActive = showWhenStatusActive && activeEffect != null;
            UpdateStatusIcon(activeEffect);
        }

        // Countdown and cast fill run every frame, not on the poll, so they drain
        // smoothly instead of stepping four times a second.
        if (statusIcon != null && statusIcon.enabled && activeEffect != null)
            statusIcon.fillAmount = activeEffect.RemainingNormalized;

        bool casting = UpdateCastBar();

        // Declared up front, not inline: with Show When Looked At off, && skips the
        // call entirely and the out values would never be assigned.
        float lookAngleNow = 999f;
        float distanceNow = 999f;
        bool lookedAt = showWhenLookedAt && PlayerIsLookingAtOwner(out lookAngleNow, out distanceNow);
        // Casting forces the panel open — a cast bar nobody can see is pointless.
        bool visible = statusActive || lookedAt || casting;

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
        if (body == null || !HasAnyDisplay()) return;
        if (Mathf.Approximately(body.CurrentHealth, shownHealth)
            && Mathf.Approximately(body.MaxHealth, shownMaxHealth)) return;

        shownHealth = body.CurrentHealth;
        shownMaxHealth = body.MaxHealth;
        PushHealth(shownHealth, shownMaxHealth);

        if (logVisibility)
            Debug.Log($"[CompanionUI] {body.name} health changed → {shownHealth}/{shownMaxHealth}; " +
                      $"slider '{(healthSlider != null ? healthSlider.name : "none")}' now " +
                      $"{(healthSlider != null ? healthSlider.value + "/" + healthSlider.maxValue : "n/a")}.", this);
    }

    // Picks up a name arriving late — e.g. the spawner calling Initialize with a
    // definition after this panel already seeded itself.
    private void RefreshName()
    {
        if (body == null || !HasAnyDisplay()) return;
        if (body.DisplayName == shownName) return;
        shownName = body.DisplayName;
        PushName(shownName);
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
    private IStatusEffect FindActiveStatus()
    {
        if (owner == null) return null;

        IStatusEffect[] effects = owner.GetComponentsInChildren<IStatusEffect>(true);
        IStatusEffect best = null;
        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i] == null || !effects[i].IsActive) continue;
            // A debuff wins outright — trouble is the more urgent thing to show.
            if (effects[i].Kind == StatusEffectKind.Debuff) return effects[i];
            if (best == null) best = effects[i];
        }
        return best;
    }

    // One image, two sprites: swaps to the buff or debuff art while something is
    // running, and switches off entirely when nothing is.
    private void UpdateStatusIcon(IStatusEffect effect)
    {
        if (statusIcon == null) return;

        if (effect == null)
        {
            statusIcon.enabled = false;
            return;
        }

        Sprite sprite = effect.Kind == StatusEffectKind.Debuff ? debuffIcon : buffIcon;
        // No art assigned for that kind → stay hidden rather than show whatever
        // sprite happened to be left on the Image in the editor.
        if (sprite != null) statusIcon.sprite = sprite;
        statusIcon.enabled = sprite != null;
    }

    // Fills the bar while any of this companion's abilities is casting. Reads the
    // generic CompanionAbility contract, so a new ability with a cast shows a bar
    // here without this script knowing anything about it. Returns true while casting.
    private bool UpdateCastBar()
    {
        if (abilities == null) return false;

        for (int i = 0; i < abilities.Length; i++)
        {
            if (abilities[i] == null || !abilities[i].IsCasting) continue;
            SetCastBar(true, abilities[i].CastProgress);

            // Sampled, not every frame: shows whether the value is actually ramping
            // or slamming straight to 1 (which would mean the cast has no duration).
            if (logVisibility && Time.time >= nextCastLogAt)
            {
                nextCastLogAt = Time.time + 0.5f;
                Debug.Log($"[CompanionUI] t={Time.time:F1}s {abilities[i].abilityName} casting — progress {abilities[i].CastProgress:F2}, " +
                          $"fill '{(castFill != null ? castFill.name : "NOT WIRED")}' = {(castFill != null ? castFill.fillAmount.ToString("F2") : "n/a")}, " +
                          $"type {(castFill != null ? castFill.type.ToString() : "n/a")}, " +
                          $"abilityEnabled={abilities[i].isActiveAndEnabled}.", this);
            }
            return true;
        }
        nextCastLogAt = 0f;

        // Nothing casting: empty it AND switch it off. Zeroing as well as hiding
        // means it can never flash a leftover full bar as the next cast starts.
        SetCastBar(false, 0f);
        return false;
    }

    // Fills the bar whatever the Image is set to.
    //
    // fillAmount ONLY does anything on Image Type = Filled — on Simple/Sliced/Tiled
    // Unity draws the whole sprite and ignores it, which looks like the bar popping
    // in at full width. Rather than force one setup, stretch the rect for those
    // types: moving the right anchor keeps 9-slice borders crisp (scaling wouldn't).
    private void SetCastBar(bool active, float progress)
    {
        if (castFill == null) return;

        SetBarFill(castFill, progress);
        if (castFill.gameObject.activeSelf != active) castFill.gameObject.SetActive(active);
    }

    // Drives a bar fill from 0 to 1 whatever the Image Type is.
    //
    // Filled images render a partial fill natively but CANNOT be 9-sliced, so
    // rounded caps get cut square. Simple/Sliced/Tiled ignore fillAmount entirely,
    // so for those we stretch the rect by moving its right anchor — which keeps a
    // 9-sliced sprite's rounded ends crisp (scaling would squash them instead).
    // That means a bar can keep its curved edges just by staying Sliced.
    private static void SetBarFill(Image img, float t)
    {
        if (img == null) return;

        if (img.type == Image.Type.Filled)
        {
            img.fillAmount = t;
            return;
        }

        RectTransform rt = img.rectTransform;
        // Pin the left edge once so the bar grows rightwards from a fixed start.
        if (!Mathf.Approximately(rt.anchorMin.x, 0f))
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
        rt.anchorMax = new Vector2(t, rt.anchorMax.y);
        // Leftover left/right insets fight the anchors and leave a stub of bar
        // visible at 0, so clear them while keeping the vertical ones.
        rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
    }

    // Keep a faded-out panel from swallowing clicks / raycasts.
    private void SetInteractable(bool on)
    {
        group.interactable = on;
        group.blocksRaycasts = on;
    }
}
