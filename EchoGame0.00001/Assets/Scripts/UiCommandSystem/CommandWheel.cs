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

    [Header("Sprites — Main Wheel")]
    [Tooltip("Default main wheel (4 numbered companion slices).")]
    [SerializeField] private Sprite idleSprite;
    [Tooltip("Main wheel with the TOP slice highlighted.")]
    [SerializeField] private Sprite companion1HighlightedSprite;
    [Tooltip("Main wheel with the RIGHT slice highlighted.")]
    [SerializeField] private Sprite companion2HighlightedSprite;

    [Header("Sprites — Companion Wheel")]
    [Tooltip("Companion 1's command wheel.")]
    [SerializeField] private Sprite companion1WheelSprite;
    [Tooltip("Companion 2's command wheel. Falls back to companion 1's if empty.")]
    [SerializeField] private Sprite companion2WheelSprite;
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

        if (aim == null) aim = FindObjectOfType<PlayerAimZoom>();
        if (lockOn == null) lockOn = FindObjectOfType<PlayerLockOn>();
        if (crosshair == null) crosshair = FindObjectOfType<PlayerCrosshair>();
    }

    void OnEnable()
    {
        dpadUpAction.performed += OnDpadUp;
        dpadRightAction.performed += OnDpadRight;
        dpadUpAction.Enable();
        dpadRightAction.Enable();
    }

    void OnDisable()
    {
        dpadUpAction.performed -= OnDpadUp;
        dpadRightAction.performed -= OnDpadRight;
        dpadUpAction.Disable();
        dpadRightAction.Disable();
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
            // Top slice is ATTACK.
            TryDispatchAttack();
        }
    }

    private void OnDpadRight(InputAction.CallbackContext ctx)
    {
        if (!IsWheelEngaged()) return;

        if (state == WheelState.Idle)
        {
            SelectCompanion(2);
        }
        // Right/down/left slices are EMPTY on the companion wheel for now.
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
            default: return null;
        }
    }

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
                SetSprite(selectedSlot == 1 ? companion1HighlightedSprite : companion2HighlightedSprite);
                break;

            case WheelState.CompanionWheel:
                Sprite wheel = selectedSlot == 2
                    ? (companion2WheelSprite != null ? companion2WheelSprite : companion1WheelSprite)
                    : companion1WheelSprite;
                SetSprite(wheel);
                break;

            case WheelState.AttackHighlighted:
                SetSprite(attackHighlightedSprite);
                break;
        }
    }

    private void SetSprite(Sprite sprite)
    {
        if (wheelImage != null && sprite != null) wheelImage.sprite = sprite;
    }
}
