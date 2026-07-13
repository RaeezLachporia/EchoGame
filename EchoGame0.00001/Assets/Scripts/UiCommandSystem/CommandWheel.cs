using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CommandWheel : MonoBehaviour
{
    private enum WheelState
    {
        Idle,
        CompanionHighlighted,
        CompanionWheel,
        AttackHighlighted,
    }

    [Header("References")]
    [Tooltip("Aim script — d-pad selection works while this reports IsAiming. Auto-found if empty.")]
    [SerializeField] private PlayerAimZoom aim;
    [Tooltip("Lock-on script — d-pad also works while a lock is active. Auto-found if empty.")]
    [SerializeField] private PlayerLockOn lockOn;
    [Tooltip("Where ATTACK gets its target from. Auto-found if empty.")]
    [SerializeField] private PlayerCrosshair crosshair;
    [Tooltip("The wheel image we swap sprites on.")]
    [SerializeField] private Image wheelImage;

    [Header("Companions")]
    [Tooltip("Companion for the TOP slice (d-pad up).")]
    [SerializeField] private CompanionCommand companion1;
    [Tooltip("Companion for the RIGHT slice (d-pad right).")]
    [SerializeField] private CompanionCommand companion2;
    [Tooltip("Companion for the BOTTOM slice (d-pad down).")]
    [SerializeField] private CompanionCommand companion3;
    [Tooltip("Companion for the LEFT slice (d-pad left).")]
    [SerializeField] private CompanionCommand companion4;

    [Header("Sprites — Main Wheel")]
    [Tooltip("Default main wheel (4 numbered companion slices).")]
    [SerializeField] private Sprite idleSprite;
    [Tooltip("Main wheel with the TOP slice highlighted.")]
    [SerializeField] private Sprite companion1HighlightedSprite;
    [Tooltip("Main wheel with the RIGHT slice highlighted.")]
    [SerializeField] private Sprite companion2HighlightedSprite;
    [Tooltip("Main wheel with the BOTTOM slice highlighted.")]
    [SerializeField] private Sprite companion3HighlightedSprite;
    [Tooltip("Main wheel with the LEFT slice highlighted.")]
    [SerializeField] private Sprite companion4HighlightedSprite;

    [Header("Sprites — Companion Wheel")]
    [Tooltip("Companion 1's command wheel.")]
    [SerializeField] private Sprite companion1WheelSprite;
    [Tooltip("Companion 2's command wheel. Falls back to companion 1's if empty.")]
    [SerializeField] private Sprite companion2WheelSprite;
    [Tooltip("Companion 3's command wheel. Falls back to companion 1's if empty.")]
    [SerializeField] private Sprite companion3WheelSprite;
    [Tooltip("Companion 4's command wheel. Falls back to companion 1's if empty.")]
    [SerializeField] private Sprite companion4WheelSprite;
    [Tooltip("Companion wheel with ATTACK highlighted.")]
    [SerializeField] private Sprite attackHighlightedSprite;

    [Header("Timing")]
    [Tooltip("How long the highlight sprite shows before the companion wheel takes over.")]
    [SerializeField, Min(0f)] private float companionHighlightHoldTime = 0.18f;
    [Tooltip("How long the ATTACK highlight shows before returning to the main wheel.")]
    [SerializeField, Min(0f)] private float attackHighlightHoldTime = 0.18f;

    [Header("Debug")]
    [SerializeField] private bool logDispatch = true;

    private InputAction dpadUpAction;
    private InputAction dpadRightAction;
    private InputAction dpadDownAction;
    private InputAction dpadLeftAction;

    private WheelState state = WheelState.Idle;
    private int selectedSlot; // 1 = top slice, 2 = right slice, 0 = none
    private float stateTimer;

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

        if (aim == null) aim = FindObjectOfType<PlayerAimZoom>();
        if (lockOn == null) lockOn = FindObjectOfType<PlayerLockOn>();
        if (crosshair == null) crosshair = FindObjectOfType<PlayerCrosshair>();
    }

    void OnEnable()
    {
        dpadUpAction.performed += OnDpadUp;
        dpadRightAction.performed += OnDpadRight;
        dpadDownAction.performed += OnDpadDown;
        dpadLeftAction.performed += OnDpadLeft;
        dpadUpAction.Enable();
        dpadRightAction.Enable();
        dpadDownAction.Enable();
        dpadLeftAction.Enable();
    }

    void OnDisable()
    {
        dpadUpAction.performed -= OnDpadUp;
        dpadRightAction.performed -= OnDpadRight;
        dpadDownAction.performed -= OnDpadDown;
        dpadLeftAction.performed -= OnDpadLeft;
        dpadUpAction.Disable();
        dpadRightAction.Disable();
        dpadDownAction.Disable();
        dpadLeftAction.Disable();
    }

    void Start()
    {
        SetState(WheelState.Idle);
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
    }

    private void OnDpadRight(InputAction.CallbackContext ctx)
    {
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(2);
        }
        else if (state == WheelState.CompanionWheel)
        {
            DispatchSlice(1);
        }
    }

    private void OnDpadDown(InputAction.CallbackContext ctx)
    {
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(3);
        }
        else if (state == WheelState.CompanionWheel)
        {
            DispatchSlice(2);
        }
    }

    private void OnDpadLeft(InputAction.CallbackContext ctx)
    {
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(4);
        }
        else if (state == WheelState.CompanionWheel)
        {
            DispatchSlice(3);
        }
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

    private CompanionCommand GetSelectedCompanion()
    {
        switch (selectedSlot)
        {
            case 1: return companion1;
            case 2: return companion2;
            case 3: return companion3;
            case 4: return companion4;
            default: return null;
        }
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

        Transform target = crosshair != null ? crosshair.CurrentEnemy : null;
        if (abilities[sliceIndex].TryActivate(target))
        {
            if (logDispatch) Debug.Log($"[CommandWheel] {companion.name} → {abilities[sliceIndex].abilityName}");
            // Every ability shows the ATTACK confirm flash for now — the new
            // wheel UI will give each its own later.
            stateTimer = attackHighlightHoldTime;
            SetState(WheelState.AttackHighlighted);
        }
        else if (logDispatch)
        {
            Debug.Log($"[CommandWheel] {abilities[sliceIndex].abilityName} couldn't start (no valid target?) — ignoring.");
        }
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
                SetSprite(idleSprite);
                break;

            case WheelState.CompanionHighlighted:
                SetSprite(GetHighlightedSpriteFor(selectedSlot));
                break;

            case WheelState.CompanionWheel:
                Sprite wheel = GetCompanionWheelFor(selectedSlot);
                SetSprite(wheel != null ? wheel : companion1WheelSprite);
                break;

            case WheelState.AttackHighlighted:
                SetSprite(attackHighlightedSprite);
                break;
        }
    }

    private Sprite GetHighlightedSpriteFor(int slot)
    {
        switch (slot)
        {
            case 1: return companion1HighlightedSprite;
            case 2: return companion2HighlightedSprite;
            case 3: return companion3HighlightedSprite;
            case 4: return companion4HighlightedSprite;
            default: return null;
        }
    }

    private Sprite GetCompanionWheelFor(int slot)
    {
        switch (slot)
        {
            case 1: return companion1WheelSprite;
            case 2: return companion2WheelSprite;
            case 3: return companion3WheelSprite;
            case 4: return companion4WheelSprite;
            default: return null;
        }
    }

    private void SetSprite(Sprite sprite)
    {
        if (wheelImage != null && sprite != null) wheelImage.sprite = sprite;
    }
}
