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
    }

    [Header("References")]
    [Tooltip("Aim script driving the aim-hold. D-pad selection is accepted while this reports IsAiming. Auto-found if left empty.")]
    [SerializeField] private PlayerAimZoom aim;
    [Tooltip("Lock-on script. While a target is locked, the wheel accepts d-pad input without needing aim held. Auto-found if left empty.")]
    [SerializeField] private PlayerLockOn lockOn;
    [Tooltip("UI Image whose sprite is swapped between wheel states. Usually the root wheel image on the HUD canvas.")]
    [SerializeField] private Image wheelImage;

    [Header("Sprites")]
    [Tooltip("Default wheel shown when aim is first held — the 4 numbered COMPANION slices.")]
    [SerializeField] private Sprite idleSprite;
    [Tooltip("Idle wheel with the top (companion 1) slice highlighted.")]
    [SerializeField] private Sprite companion1HighlightedSprite;
    [Tooltip("Idle wheel with the right (companion 2) slice highlighted.")]
    [SerializeField] private Sprite companion2HighlightedSprite;
    [Tooltip("Command wheel shown after companion 1 is picked (ATTACK + EMPTY slots).")]
    [SerializeField] private Sprite companion1WheelSprite;
    [Tooltip("Command wheel shown after companion 2 is picked. Falls back to companion 1's wheel sprite if empty.")]
    [SerializeField] private Sprite companion2WheelSprite;

    [Header("Timing")]
    [Tooltip("How long the highlighted-companion sprite is shown before switching to that companion's command wheel.")]
    [SerializeField, Min(0f)] private float highlightHoldTime = 0.18f;

    private InputAction dpadUpAction;
    private InputAction dpadRightAction;

    private WheelState state = WheelState.Idle;
    private int selectedCompanion; // 1 or 2 while a companion is picked, 0 otherwise
    private float highlightTimer;

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
            highlightTimer -= Time.unscaledDeltaTime;
            if (highlightTimer <= 0f)
                SetState(WheelState.CompanionWheel);
        }
    }

    private void OnDpadUp(InputAction.CallbackContext ctx)
    {
        if (!IsWheelEngaged()) return;
        // Only accept selection from the idle wheel — d-pad in a companion's own
        // wheel will map to that wheel's commands later.
        if (state != WheelState.Idle) return;
        SelectCompanion(1);
    }

    private void OnDpadRight(InputAction.CallbackContext ctx)
    {
        if (!IsWheelEngaged()) return;
        if (state != WheelState.Idle) return;
        SelectCompanion(2);
    }

    private bool IsWheelEngaged()
    {
        bool aiming = aim != null && aim.IsAiming;
        bool locked = lockOn != null && lockOn.IsLocked;
        return aiming || locked;
    }

    private void SelectCompanion(int companion)
    {
        selectedCompanion = companion;
        highlightTimer = highlightHoldTime;
        SetState(WheelState.CompanionHighlighted);
    }

    private void SetState(WheelState next)
    {
        state = next;

        switch (next)
        {
            case WheelState.Idle:
                selectedCompanion = 0;
                highlightTimer = 0f;
                SetSprite(idleSprite);
                break;

            case WheelState.CompanionHighlighted:
                SetSprite(selectedCompanion == 1 ? companion1HighlightedSprite : companion2HighlightedSprite);
                break;

            case WheelState.CompanionWheel:
                Sprite wheel = selectedCompanion == 2
                    ? (companion2WheelSprite != null ? companion2WheelSprite : companion1WheelSprite)
                    : companion1WheelSprite;
                SetSprite(wheel);
                break;
        }
    }

    private void SetSprite(Sprite sprite)
    {
        if (wheelImage != null && sprite != null) wheelImage.sprite = sprite;
    }
}
