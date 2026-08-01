using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

// The wheel is composed live instead of swapping full-wheel sprites:
// a static background Image (lives in the scene, no reference needed here),
// four icon Images sitting over the slices, and one highlight wedge that the
// code rotates to the selected slice and tints for the confirm flash.
public class CommandWheel : MonoBehaviour
{
    private enum WheelState
    {
        Idle,
        CompanionHighlighted,
        CompanionWheel,
        AllyWheel,
        AttackHighlighted,
    }

    [Header("References")]
    [Tooltip("Aim script — d-pad selection works while this reports IsAiming. Auto-found if empty.")]
    [SerializeField] private PlayerAimZoom aim;
    [Tooltip("Lock-on script — d-pad also works while a lock is active. Auto-found if empty.")]
    [SerializeField] private PlayerLockOn lockOn;
    [Tooltip("Where ATTACK gets its target from. Auto-found if empty.")]
    [SerializeField] private PlayerCrosshair crosshair;

    [Header("Companions")]
    [Tooltip("Companion for the TOP slice (d-pad up).")]
    [SerializeField] private CompanionCommand companion1;
    [Tooltip("Companion for the RIGHT slice (d-pad right).")]
    [SerializeField] private CompanionCommand companion2;
    [Tooltip("Companion for the BOTTOM slice (d-pad down).")]
    [SerializeField] private CompanionCommand companion3;
    [Tooltip("Companion for the LEFT slice (d-pad left).")]
    [SerializeField] private CompanionCommand companion4;

    [Header("Wheel Pieces")]
    [Tooltip("Icon Image sitting over the TOP slice.")]
    [SerializeField] private Image iconTop;
    [Tooltip("Icon Image sitting over the RIGHT slice.")]
    [SerializeField] private Image iconRight;
    [Tooltip("Icon Image sitting over the BOTTOM slice.")]
    [SerializeField] private Image iconBottom;
    [Tooltip("Icon Image sitting over the LEFT slice.")]
    [SerializeField] private Image iconLeft;
    [Tooltip("The single highlight wedge. Author the sprite pointing at the TOP slice — the wheel rotates it to the other three.")]
    [SerializeField] private Image highlightImage;
    [Tooltip("Stand-in for a slice whose companion or ability has no icon assigned yet. Wire this to wheel_slot_default so a half-built companion still reads as an occupied slot instead of a blank one.")]
    [SerializeField] private Sprite placeholderIcon;

    [Header("Ally Targeting")]
    [Tooltip("The player character. NOT a slice on the ally wheel — the four slices are companion1-4. The player is healed by the dedicated heal-player button instead. Auto-found by the 'Player' tag if left empty.")]
    [SerializeField] private Transform playerTarget;
    [Tooltip("Reusable message line for the wheel — the CODE writes what shows here (e.g. \"Press Y to heal player\" on the ally wheel; buff and other prompts reuse it later). Drag a TMP text; leave empty to skip. Stays hidden unless a message is set.")]
    [SerializeField] private TMP_Text wheelMessageLabel;

    [Header("Highlight Tints")]
    [Tooltip("Highlight tint while a slice is selected. White = the sprite's own colours.")]
    [SerializeField] private Color selectTint = Color.white;
    [Tooltip("Highlight tint flashed on the slice whose command actually fired.")]
    [SerializeField] private Color confirmTint = new Color(0.4f, 1f, 0.5f);

    [Header("Timing")]
    [Tooltip("How long the highlight shows before the companion wheel takes over.")]
    [SerializeField, Min(0f)] private float companionHighlightHoldTime = 0.18f;
    [Tooltip("How long the confirm flash shows before returning to the main wheel.")]
    [SerializeField, Min(0f)] private float attackHighlightHoldTime = 0.18f;

    [Header("Debug")]
    [SerializeField] private bool logDispatch = true;

    private InputAction dpadUpAction;
    private InputAction dpadRightAction;
    private InputAction dpadDownAction;
    private InputAction dpadLeftAction;
    private InputAction healPlayerAction;

    // Which control scheme the player last used, so the heal-player prompt can show
    // the right button (Y on gamepad, H on keyboard).
    private bool lastInputWasGamepad;

    private WheelState state = WheelState.Idle;
    private int selectedSlot; // 1 = top slice, 2 = right slice, 0 = none
    private int confirmedSlice; // slice the confirm flash points at (0 = top)
    private float stateTimer;
    private Image[] icons; // top, right, bottom, left — same order as slices

    // Ally wheel (Layer 3a) working state. pendingAbility is the heal/buff waiting on
    // a target; allyTargets/allyIcons map each slice to a party member and its portrait.
    private CompanionAbility pendingAbility;
    private readonly Transform[] allyTargets = new Transform[4];
    private readonly Sprite[] allyIcons = new Sprite[4];

    void Awake()
    {
        // Binding here in code so tweaking the wheel doesn't force a regen of the input asset.
        dpadUpAction = new InputAction("WheelUp", InputActionType.Button);
        dpadUpAction.AddBinding("<Gamepad>/dpad/up");
        dpadUpAction.AddBinding("<Keyboard>/upArrow");

        dpadRightAction = new InputAction("WheelRight", InputActionType.Button);
        dpadRightAction.AddBinding("<Gamepad>/dpad/right");
        dpadRightAction.AddBinding("<Keyboard>/rightArrow");

        dpadDownAction = new InputAction("WheelDown", InputActionType.Button);
        dpadDownAction.AddBinding("<Gamepad>/dpad/down");
        dpadDownAction.AddBinding("<Keyboard>/downArrow");

        dpadLeftAction = new InputAction("WheelLeft", InputActionType.Button);
        dpadLeftAction.AddBinding("<Gamepad>/dpad/left");
        dpadLeftAction.AddBinding("<Keyboard>/leftArrow");

        // Dedicated "heal the player" button for the ally wheel — the player isn't a
        // wheel slice. buttonNorth (Y) and 'H' are free: Jump = A, Dodge = B,
        // Attack = RB / left-mouse, so this won't double-fire while aiming.
        healPlayerAction = new InputAction("HealPlayer", InputActionType.Button);
        healPlayerAction.AddBinding("<Gamepad>/buttonNorth");
        healPlayerAction.AddBinding("<Keyboard>/h");

        if (aim == null) aim = FindObjectOfType<PlayerAimZoom>();
        if (lockOn == null) lockOn = FindObjectOfType<PlayerLockOn>();
        if (crosshair == null) crosshair = FindObjectOfType<PlayerCrosshair>();

        icons = new[] { iconTop, iconRight, iconBottom, iconLeft };
        lastInputWasGamepad = Gamepad.current != null;
    }

    void OnEnable()
    {
        dpadUpAction.performed += OnDpadUp;
        dpadRightAction.performed += OnDpadRight;
        dpadDownAction.performed += OnDpadDown;
        dpadLeftAction.performed += OnDpadLeft;
        healPlayerAction.performed += OnHealPlayer;
        dpadUpAction.Enable();
        dpadRightAction.Enable();
        dpadDownAction.Enable();
        dpadLeftAction.Enable();
        healPlayerAction.Enable();
    }

    void OnDisable()
    {
        dpadUpAction.performed -= OnDpadUp;
        dpadRightAction.performed -= OnDpadRight;
        dpadDownAction.performed -= OnDpadDown;
        dpadLeftAction.performed -= OnDpadLeft;
        healPlayerAction.performed -= OnHealPlayer;
        dpadUpAction.Disable();
        dpadRightAction.Disable();
        dpadDownAction.Disable();
        dpadLeftAction.Disable();
        healPlayerAction.Disable();

        // If the wheel gets disabled mid-ally-wheel, don't leave the message on screen.
        HideWheelMessage();
    }

    void Start()
    {
        ResolvePlayerTarget();
        SetState(WheelState.Idle);
        AuditCompanionIcons();
    }

    void Update()
    {
        // Not aiming and not locked → drop back to idle so the player always sees
        // the base wheel (and its cooldowns) when disengaged.
        if (!IsWheelEngaged() && state != WheelState.Idle)
        {
            SetState(WheelState.Idle);
            return;
        }

        if (state == WheelState.CompanionHighlighted)
        {
            stateTimer -= Time.unscaledDeltaTime;
            if (stateTimer <= 0f)
                SetState(WheelState.CompanionWheel);
        }
        else if (state == WheelState.AttackHighlighted)
        {
            stateTimer -= Time.unscaledDeltaTime;
            if (stateTimer <= 0f)
                SetState(WheelState.Idle);
        }
    }

    private void OnDpadUp(InputAction.CallbackContext ctx)
    {
        RecordInputDevice(ctx);
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(1);
        }
        else if (state == WheelState.CompanionWheel)
        {
            // Top slice = the companion's first ability (ATTACK by convention).
            DispatchSlice(0);
        }
        else if (state == WheelState.AllyWheel)
        {
            DispatchAllyTarget(0);
        }
    }

    private void OnDpadRight(InputAction.CallbackContext ctx)
    {
        RecordInputDevice(ctx);
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(2);
        }
        else if (state == WheelState.CompanionWheel)
        {
            DispatchSlice(1);
        }
        else if (state == WheelState.AllyWheel)
        {
            DispatchAllyTarget(1);
        }
    }

    private void OnDpadDown(InputAction.CallbackContext ctx)
    {
        RecordInputDevice(ctx);
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(3);
        }
        else if (state == WheelState.CompanionWheel)
        {
            DispatchSlice(2);
        }
        else if (state == WheelState.AllyWheel)
        {
            DispatchAllyTarget(2);
        }
    }

    private void OnDpadLeft(InputAction.CallbackContext ctx)
    {
        RecordInputDevice(ctx);
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(4);
        }
        else if (state == WheelState.CompanionWheel)
        {
            DispatchSlice(3);
        }
        else if (state == WheelState.AllyWheel)
        {
            DispatchAllyTarget(3);
        }
    }

    private void OnHealPlayer(InputAction.CallbackContext ctx)
    {
        RecordInputDevice(ctx);
        if (!IsWheelEngaged()) return;
        if (state == WheelState.AllyWheel) DispatchPlayerHeal();
    }

    // Track which control scheme drove the last wheel input, so the heal-player
    // prompt (built when the ally wheel opens) names the right button.
    private void RecordInputDevice(InputAction.CallbackContext ctx)
    {
        if (ctx.control != null)
            lastInputWasGamepad = ctx.control.device is Gamepad;
    }

    // The reusable wheel message line. Code writes the text; the box hides when
    // there's nothing to say. Reuse ShowWheelMessage for buff prompts and any other
    // wheel messages later — including from other scripts if this is made public.
    private void ShowWheelMessage(string message)
    {
        if (wheelMessageLabel == null) return;
        wheelMessageLabel.text = message;
        wheelMessageLabel.gameObject.SetActive(true);
    }

    private void HideWheelMessage()
    {
        if (wheelMessageLabel == null) return;
        wheelMessageLabel.gameObject.SetActive(false);
    }

    // The dedicated player button, glyph chosen by the last input used (Y on gamepad,
    // H on keyboard) — matches the healPlayerAction bindings.
    private string PlayerButtonLabel => lastInputWasGamepad ? "Y" : "H";

    // "Press Y to heal player" / "...to buff player" — the verb comes from the active
    // ability's name, so the same line serves heal and, later, buff with no rewiring.
    private string BuildPlayerActionMessage()
    {
        string verb = pendingAbility != null ? pendingAbility.abilityName.ToLower() : "heal";
        return $"Press {PlayerButtonLabel} to {verb} player";
    }

    private bool IsWheelEngaged()
    {
        bool aiming = aim != null && aim.IsAiming;
        bool locked = lockOn != null && lockOn.IsLocked;
        return aiming || locked;
    }

    private void SelectCompanion(int slot)
    {
        selectedSlot = slot;
        stateTimer = companionHighlightHoldTime;
        SetState(WheelState.CompanionHighlighted);
    }

    private CompanionCommand GetCompanionInSlot(int slot)
    {
        switch (slot)
        {
            case 1: return companion1;
            case 2: return companion2;
            case 3: return companion3;
            case 4: return companion4;
            default: return null;
        }
    }

    private CompanionCommand GetSelectedCompanion()
    {
        return GetCompanionInSlot(selectedSlot);
    }

    // Fires whichever ability sits in that slice of the selected companion's wheel.
    // Slice numbers: 0 = TOP, 1 = RIGHT, 2 = BOTTOM, 3 = LEFT — matching the order
    // of the ability components on the companion. Companions with no abilities
    // still work the old way: TOP slice = attack.
    private void DispatchSlice(int sliceIndex)
    {
        CompanionCommand companion = GetSelectedCompanion();
        if (companion == null)
        {
            if (logDispatch) Debug.LogWarning($"[CommandWheel] Slot {selectedSlot} has no companion assigned — nothing to command.");
            return;
        }

        CompanionAbility[] abilities = companion.GetComponents<CompanionAbility>();
        if (abilities.Length == 0)
        {
            if (sliceIndex == 0) TryDispatchAttack();
            return;
        }

        if (sliceIndex >= abilities.Length) return; // empty slice

        CompanionAbility ability = abilities[sliceIndex];
        switch (ability.TargetKind)
        {
            // Heal/buff: don't fire yet — open the ally wheel so the player picks who.
            case AbilityTargetKind.AllyPicker:
                OpenAllyWheel(ability);
                break;

            // Debuff: the enemy-cycle reticle (Layer 3b) is a later step, and nothing
            // declares EnemyPicker yet — note it and stay on the ability wheel for now.
            case AbilityTargetKind.EnemyPicker:
                if (logDispatch) Debug.Log($"[CommandWheel] {ability.abilityName} wants an enemy picker — not built yet, ignoring.");
                break;

            // Attack and anything else: fire immediately on the reticle/lock target.
            default:
                FireAbility(ability, sliceIndex, crosshair != null ? crosshair.CurrentEnemy : null);
                break;
        }
    }

    // Runs an ability against a resolved target and plays the confirm flash on the
    // slice that fired. Shared by the reticle path and the ally wheel. Returns true
    // if the ability actually started.
    private bool FireAbility(CompanionAbility ability, int confirmSlice, Transform target)
    {
        if (ability.TryActivate(target))
        {
            if (logDispatch)
            {
                CompanionCommand who = GetSelectedCompanion();
                Debug.Log($"[CommandWheel] {(who != null ? who.name : "?")} → {ability.abilityName}");
            }
            confirmedSlice = confirmSlice;
            stateTimer = attackHighlightHoldTime;
            SetState(WheelState.AttackHighlighted);
            return true;
        }

        if (logDispatch)
            Debug.Log($"[CommandWheel] {ability.abilityName} couldn't start (no valid target?) — ignoring.");
        return false;
    }

    // Layer 3a. Stash the ability, build the ally slices (companion1-4, same layout
    // as the main wheel — the caster included, so a support can heal itself), and
    // switch to the ally wheel. With no one to target, stay on the ability wheel.
    private void OpenAllyWheel(CompanionAbility ability)
    {
        pendingAbility = ability;
        BuildAllyWheel();
        if (!AnyAllyTarget())
        {
            if (logDispatch) Debug.Log($"[CommandWheel] {ability.abilityName}: no ally targets to show.");
            pendingAbility = null;
            return;
        }
        SetState(WheelState.AllyWheel);
    }

    // Fires the pending ally-picker ability on whoever sits in that slice. An empty
    // slice, or a target the ability rejects (e.g. already full health), is a no-op.
    private void DispatchAllyTarget(int sliceIndex)
    {
        if (pendingAbility == null) { SetState(WheelState.Idle); return; }

        Transform target = sliceIndex >= 0 && sliceIndex < allyTargets.Length ? allyTargets[sliceIndex] : null;
        if (target == null)
        {
            if (logDispatch) Debug.Log("[CommandWheel] Empty ally slice — nothing to heal/buff there.");
            return;
        }

        // Keep pendingAbility set if the target is rejected (e.g. already full health)
        // so the player can pick a different ally; clear it once the heal/buff fires.
        if (FireAbility(pendingAbility, sliceIndex, target))
            pendingAbility = null;
    }

    // The player isn't a wheel slice — this dedicated button (Y / H) heals them from
    // the ally wheel. Single press today; a "Heal player?" confirm popup will wrap it
    // once the UI exists. Rejected (already full) leaves the ally wheel open.
    private void DispatchPlayerHeal()
    {
        if (pendingAbility == null) return;

        Transform p = ResolvePlayerTarget();
        if (p == null)
        {
            if (logDispatch) Debug.Log("[CommandWheel] Heal-player pressed but no Player was found.");
            return;
        }

        if (pendingAbility.TryActivate(p))
        {
            if (logDispatch)
            {
                CompanionCommand who = GetSelectedCompanion();
                Debug.Log($"[CommandWheel] {(who != null ? who.name : "?")} → heal player");
            }
            pendingAbility = null;
            SetState(WheelState.Idle);
        }
        else if (logDispatch)
        {
            Debug.Log("[CommandWheel] Player already at full health — not healing.");
        }
    }

    // Ally wheel mirrors the MAIN wheel's layout exactly: companion1 = TOP,
    // companion2 = RIGHT, companion3 = BOTTOM, companion4 = LEFT. The caster is
    // included, so a support heals itself by picking its own slice. The player is
    // NOT on the wheel — they get the dedicated heal-player button.
    private void BuildAllyWheel()
    {
        for (int i = 0; i < allyTargets.Length; i++)
        {
            CompanionCommand c = GetCompanionInSlot(i + 1);
            allyTargets[i] = c != null ? ResolveHealTargetTransform(c) : null;
            allyIcons[i] = c != null ? Resolve(GetPortrait(c)) : null;
        }
    }

    private bool AnyAllyTarget()
    {
        for (int i = 0; i < allyTargets.Length; i++)
            if (allyTargets[i] != null) return true;
        return false;
    }

    // Cache the player's transform. Serialized field wins; otherwise fall back to
    // the 'Player' tag so the wheel works with no manual wiring.
    private Transform ResolvePlayerTarget()
    {
        if (playerTarget != null) return playerTarget;
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) playerTarget = p.transform;
        return playerTarget;
    }

    // Aim the heal/buff at the Comapnion body (which carries IHealable), falling back
    // to the command's own transform if the body sits on the same object anyway.
    private static Transform ResolveHealTargetTransform(CompanionCommand companion)
    {
        Comapnion body = GetBody(companion);
        if (body != null) return body.transform;
        return companion != null ? companion.transform : null;
    }

    // The old attack path — only used for companions that don't have any
    // ability components on their prefab yet.
    private void TryDispatchAttack()
    {
        CompanionCommand target = GetSelectedCompanion();
        if (target == null)
        {
            if (logDispatch) Debug.LogWarning($"[CommandWheel] Slot {selectedSlot} has no CompanionCommand assigned — nothing to command.");
            return;
        }

        Transform enemy = crosshair != null ? crosshair.CurrentEnemy : null;
        if (enemy == null)
        {
            if (logDispatch) Debug.Log("[CommandWheel] ATTACK selected but no enemy under reticle / lock — ignoring.");
            return;
        }

        target.CommandAttack(enemy);
        if (logDispatch) Debug.Log($"[CommandWheel] {target.name} → attack {enemy.name}");

        confirmedSlice = 0;
        stateTimer = attackHighlightHoldTime;
        SetState(WheelState.AttackHighlighted);
    }

    private void SetState(WheelState next)
    {
        state = next;

        switch (next)
        {
            case WheelState.Idle:
                selectedSlot = 0;
                stateTimer = 0f;
                pendingAbility = null;
                ShowCompanionIcons();
                HideHighlight();
                break;

            case WheelState.CompanionHighlighted:
                ShowCompanionIcons();
                ShowHighlight(selectedSlot - 1, selectTint);
                break;

            case WheelState.CompanionWheel:
                ShowAbilityIcons();
                HideHighlight();
                break;

            case WheelState.AllyWheel:
                ShowAllyIcons();
                HideHighlight();
                break;

            case WheelState.AttackHighlighted:
                // Icons stay as they are — only the confirm flash changes.
                ShowHighlight(confirmedSlice, confirmTint);
                break;
        }

        // The player-button prompt belongs to the ally wheel only — every transition
        // runs through here, so it can never be left stranded on. The code writes the
        // message (device-aware button + the ability's verb), so the same line can be
        // reused for buff and other prompts later.
        if (next == WheelState.AllyWheel) ShowWheelMessage(BuildPlayerActionMessage());
        else HideWheelMessage();
    }

    // Main wheel: each slice shows the portrait of the companion in that slot.
    // A slot holding a companion whose portrait isn't authored yet falls back to
    // the placeholder, so the slice still reads as occupied. Only a genuinely
    // empty slot hides its icon and lets the background's own slice art through.
    private void ShowCompanionIcons()
    {
        for (int i = 0; i < icons.Length; i++)
        {
            CompanionCommand companion = GetCompanionInSlot(i + 1);
            SetIcon(icons[i], companion != null ? Resolve(GetPortrait(companion)) : null);
        }
    }

    // Companion wheel: slices show the selected companion's ability icons in
    // component order — the same order DispatchSlice fires them, so the picture
    // and the dispatch can never disagree. An ability with no icon yet still
    // shows the placeholder, because the slice is live and will fire.
    private void ShowAbilityIcons()
    {
        CompanionCommand companion = GetSelectedCompanion();
        CompanionAbility[] abilities = companion != null
            ? companion.GetComponents<CompanionAbility>()
            : System.Array.Empty<CompanionAbility>();

        for (int i = 0; i < icons.Length; i++)
            SetIcon(icons[i], i < abilities.Length ? Resolve(abilities[i].icon) : null);
    }

    // Ally wheel: each slice shows a party member's portrait, matching the target
    // DispatchAllyTarget will fire on. Empty slices hide (no ally there to pick).
    private void ShowAllyIcons()
    {
        for (int i = 0; i < icons.Length; i++)
            SetIcon(icons[i], i < allyIcons.Length ? allyIcons[i] : null);
    }

    // Null sprite on a live slice → placeholder. Written out rather than using ??
    // because Unity's overloaded == doesn't play well with null-coalescing.
    private Sprite Resolve(Sprite sprite)
    {
        return sprite != null ? sprite : placeholderIcon;
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    private static Sprite GetPortrait(CompanionCommand companion)
    {
        Comapnion body = GetBody(companion);
        return body != null && body.Definition != null ? body.Definition.portrait : null;
    }

    // The Comapnion body normally sits on the same GameObject as the command
    // script, but fall back to a child search so a prefab that nests its rig
    // differently still resolves to a definition.
    private static Comapnion GetBody(CompanionCommand companion)
    {
        if (companion == null) return null;
        Comapnion body = companion.GetComponent<Comapnion>();
        return body != null ? body : companion.GetComponentInChildren<Comapnion>();
    }

    // A missing portrait used to make the slice vanish, which looks exactly like
    // "the wheel is broken". Name the slot and the missing link once at startup
    // so the empty slice is traceable to the asset that needs filling in.
    private void AuditCompanionIcons()
    {
        for (int slot = 1; slot <= 4; slot++)
        {
            CompanionCommand companion = GetCompanionInSlot(slot);
            if (companion == null)
            {
                Debug.LogWarning($"[CommandWheel] Slot {slot} has no companion assigned — that slice stays empty.", this);
                continue;
            }

            Comapnion body = GetBody(companion);
            if (body == null)
            {
                Debug.LogWarning($"[CommandWheel] Slot {slot} ('{companion.name}') has no Comapnion component, so the wheel can't reach a definition or portrait.", companion);
                continue;
            }

            if (body.Definition == null)
            {
                Debug.LogWarning($"[CommandWheel] Slot {slot} ('{companion.name}') has no Companion Definition assigned on its Comapnion component — no portrait to show.", companion);
                continue;
            }

            if (body.Definition.portrait == null)
                Debug.LogWarning($"[CommandWheel] Slot {slot} ('{companion.name}') uses definition '{body.Definition.name}', which has an empty Portrait field. Drag a sprite into that asset's Portrait to give the slice an icon.", body.Definition);
        }
    }

    // slice: 0 = TOP, 1 = RIGHT, 2 = BOTTOM, 3 = LEFT. The wedge sprite is
    // authored on the TOP slice, so each step round the wheel is -90° (clockwise).
    private void ShowHighlight(int slice, Color tint)
    {
        if (highlightImage == null) return;
        highlightImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, -90f * slice);
        highlightImage.color = tint;
        highlightImage.enabled = true;
    }

    private void HideHighlight()
    {
        if (highlightImage == null) return;
        highlightImage.enabled = false;
    }
}
