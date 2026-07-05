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
    [Tooltip("Aim script driving the aim-hold. D-pad selection is accepted while this reports IsAiming. Auto-found if left empty.")]
    [SerializeField] private PlayerAimZoom aim;
    [Tooltip("Lock-on script. While a target is locked, the wheel accepts d-pad input without needing aim held. Auto-found if left empty.")]
    [SerializeField] private PlayerLockOn lockOn;
    [Tooltip("Source of the currently-targeted enemy. Its CurrentEnemy is who ATTACK will send the selected companion at. Auto-found if left empty.")]
    [SerializeField] private PlayerCrosshair crosshair;
    [Tooltip("UI Image whose sprite is swapped between wheel states. Usually the root wheel image on the HUD canvas.")]
    [SerializeField] private Image wheelImage;

    [Header("Companions")]
    [Tooltip("Companion linked to the TOP slice of the main wheel (d-pad up). Leave empty if you don't have this companion in the scene yet.")]
    [SerializeField] private CompanionCommand companion1;
    [Tooltip("Companion linked to the RIGHT slice of the main wheel (d-pad right).")]
    [SerializeField] private CompanionCommand companion2;

    [Header("Sprites — Main Wheel")]
    [Tooltip("Default main wheel — the 4 numbered COMPANION slices.")]
    [SerializeField] private Sprite idleSprite;
    [Tooltip("Main wheel with the top (companion 1) slice highlighted.")]
    [SerializeField] private Sprite companion1HighlightedSprite;
    [Tooltip("Main wheel with the right (companion 2) slice highlighted.")]
    [SerializeField] private Sprite companion2HighlightedSprite;

    [Header("Sprites — Companion Wheel")]
    [Tooltip("Command wheel shown after companion 1 is picked (ATTACK + EMPTY slots).")]
    [SerializeField] private Sprite companion1WheelSprite;
    [Tooltip("Command wheel shown after companion 2 is picked. Falls back to companion 1's wheel sprite if empty.")]
    [SerializeField] private Sprite companion2WheelSprite;
    [Tooltip("Companion wheel with the ATTACK slice highlighted, shown briefly on dispatch.")]
    [SerializeField] private Sprite attackHighlightedSprite;

    [Header("Timing")]
    [Tooltip("How long the highlighted-companion sprite is shown before switching to that companion's command wheel.")]
    [SerializeField, Min(0f)] private float companionHighlightHoldTime = 0.18f;
    [Tooltip("How long the highlighted-attack sprite is shown after dispatch before the wheel returns to the main wheel.")]
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
        // Bound in code (not in PlayerControls.inputactions) so the wheel is
        // self-contained and doesn't force a regen of the input asset.
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
        // Wheel is "engaged" while the player is aiming OR locked on to a target.
        // When they let go of both, revert to idle so the base wheel (and its
        // cooldown animations) is always what the player sees.
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
            // Top slice on the companion wheel is ATTACK.
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
        // Right / down / left slices on the companion wheel are EMPTY for now.
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
