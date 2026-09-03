using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

// The PLAYER's own ability wheel — a sibling of CommandWheel, not a subclass.
//
// CommandWheel commands COMPANIONS: it is gated by aim/lock-on, driven by the
// d-pad, and always on screen. This one is the player's own kit: summoned by
// HOLDING the right bumper (Q on keyboard), driven by the FACE BUTTONS, and
// hidden the rest of the time. Keeping the two on separate controls means
// neither wheel ever has to ask which one a press belonged to.
//
// Slice order matches CommandWheel exactly (0 = TOP, 1 = RIGHT, 2 = BOTTOM,
// 3 = LEFT) because the face buttons form the same diamond the wheel does:
// Y = top, B = right, A = bottom, X = left.
//
// PLUMBING ONLY for now. There are no player abilities yet — pressing a slice
// lights its wedge and logs. FireSlot() is the single seam where the mission
// class loadout will dispatch a real ability later.
public class PlayerAbilityWheel : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The wheel graphics — background, icons, wedge. Toggled on/off as the bumper is held. This script must NOT live on this object: a deactivated GameObject stops running Update and could never turn itself back on. Put this component on a parent that stays active (the PlayerUi canvas root is fine).")]
    [SerializeField] private GameObject wheelRoot;
    [Tooltip("The companion command wheel. While its ally/enemy picker is open, gamepad A means 'commit target' — so this wheel stands down rather than also firing its BOTTOM slice. Auto-found if empty.")]
    [SerializeField] private CommandWheel commandWheel;

    [Header("Wheel Pieces")]
    [Tooltip("Icon Image on the TOP slice (Y / 1). Drag the child Image from the wheel here. Author its Source Image sprite in the Inspector — that's what shows on the slice.")]
    [SerializeField] private Image iconTop;
    [Tooltip("Icon Image on the RIGHT slice (B / 2). Drag the child Image from the wheel here.")]
    [SerializeField] private Image iconRight;
    [Tooltip("Icon Image on the BOTTOM slice (A / 3). Drag the child Image from the wheel here.")]
    [SerializeField] private Image iconBottom;
    [Tooltip("Icon Image on the LEFT slice (X / 4). Drag the child Image from the wheel here.")]
    [SerializeField] private Image iconLeft;
    [Tooltip("The rotating highlight wedge. Author its sprite pointing at the TOP slice — the wheel rotates it to the other three at confirm-flash time.")]
    [SerializeField] private Image highlightImage;
    [Tooltip("Reusable message line for this wheel — the CODE writes what shows here. Drag a TMP text; leave empty to skip.")]
    [SerializeField] private TMP_Text wheelMessageLabel;

    [Header("Highlight Tints")]
    [Tooltip("Highlight tint flashed on the slice that actually fired.")]
    [SerializeField] private Color confirmTint = new Color(0.4f, 1f, 0.5f);

    [Header("Timing")]
    [Tooltip("How long the confirm flash stays lit after a slice is pressed.")]
    [SerializeField, Min(0f)] private float confirmHoldTime = 0.18f;

    [Header("Debug")]
    [SerializeField] private bool logDispatch = true;

    private InputAction openAction;
    private InputAction slotTopAction;
    private InputAction slotRightAction;
    private InputAction slotBottomAction;
    private InputAction slotLeftAction;

    // The four icon Images in slice order, cached from the serialized fields for
    // index-based access (SetSlotIcon by slice number).
    private Image[] iconSlots;
    private bool wasOpen;
    private float confirmTimer;

    // Which control scheme drove the last press, so the message line can name the
    // right buttons. Same trick CommandWheel uses for its heal-player prompt.
    private bool lastInputWasGamepad;

    // Held, not toggled — and stands down while the companion wheel is mid-pick so
    // gamepad A never means "commit companion target" and "fire slot 2" at once.
    //
    // Reading the live device state (rather than latching a bool in an open/close
    // callback) is what lets Jump, Dodge and Interact simply ask "is the wheel
    // open?" with no ordering games. CommandWheel needs its sticky
    // suppressJumpThisPress latch because its confirm press MUTATES state that the
    // jump handler then reads; here the bumper is physically down for the whole
    // press, so IsPressed() is already true whichever callback runs first.
    public bool IsOpen =>
        openAction != null && openAction.IsPressed()
        && !(commandWheel != null && commandWheel.IsPickingTarget);

    void Awake()
    {
        // Bound in code, like CommandWheel and PlayerAimZoom, so adding a wheel
        // does not force a regen of the generated PlayerControls asset.
        openAction = new InputAction("PlayerWheelOpen", InputActionType.Button);
        openAction.AddBinding("<Gamepad>/rightShoulder");
        openAction.AddBinding("<Keyboard>/q");

        // Face buttons in wheel order. buttonNorth/East/South/West are Y/B/A/X on
        // Xbox and Triangle/Circle/Cross/Square on PlayStation — one layout, two
        // sets of names. The number row mirrors them for keyboard.
        slotTopAction = new InputAction("PlayerWheelTop", InputActionType.Button);
        slotTopAction.AddBinding("<Gamepad>/buttonNorth");
        slotTopAction.AddBinding("<Keyboard>/1");

        slotRightAction = new InputAction("PlayerWheelRight", InputActionType.Button);
        slotRightAction.AddBinding("<Gamepad>/buttonEast");
        slotRightAction.AddBinding("<Keyboard>/2");

        slotBottomAction = new InputAction("PlayerWheelBottom", InputActionType.Button);
        slotBottomAction.AddBinding("<Gamepad>/buttonSouth");
        slotBottomAction.AddBinding("<Keyboard>/3");

        slotLeftAction = new InputAction("PlayerWheelLeft", InputActionType.Button);
        slotLeftAction.AddBinding("<Gamepad>/buttonWest");
        slotLeftAction.AddBinding("<Keyboard>/4");

        if (commandWheel == null) commandWheel = FindObjectOfType<CommandWheel>();

        iconSlots = new[] { iconTop, iconRight, iconBottom, iconLeft };
        lastInputWasGamepad = Gamepad.current != null;
    }

    void OnEnable()
    {
        slotTopAction.performed += OnSlotTop;
        slotRightAction.performed += OnSlotRight;
        slotBottomAction.performed += OnSlotBottom;
        slotLeftAction.performed += OnSlotLeft;
        openAction.Enable();
        slotTopAction.Enable();
        slotRightAction.Enable();
        slotBottomAction.Enable();
        slotLeftAction.Enable();
    }

    void OnDisable()
    {
        slotTopAction.performed -= OnSlotTop;
        slotRightAction.performed -= OnSlotRight;
        slotBottomAction.performed -= OnSlotBottom;
        slotLeftAction.performed -= OnSlotLeft;
        openAction.Disable();
        slotTopAction.Disable();
        slotRightAction.Disable();
        slotBottomAction.Disable();
        slotLeftAction.Disable();

        // Disabled mid-hold (scene unload, leaving play mode) — do not leave the
        // wheel stranded on screen with no script left running to close it.
        CloseWheel();
    }

    void Start()
    {
        if (wheelRoot == null)
        {
            Debug.LogWarning($"[PlayerAbilityWheel] '{name}' has no Wheel Root assigned, so holding the bumper will show nothing. Drag the wheel graphics GameObject into that field — and make sure it is NOT this same object, or the wheel could never reopen itself.", this);
        }
        else if (wheelRoot == gameObject)
        {
            Debug.LogError($"[PlayerAbilityWheel] '{name}' has Wheel Root pointing at its own GameObject. Deactivating it would stop this script running, so the wheel would close once and never reopen. Move this component onto a parent that stays active.", this);
        }

        if (iconSlots != null)
        {
            for (int i = 0; i < iconSlots.Length; i++)
            {
                if (iconSlots[i] == null)
                    Debug.LogWarning($"[PlayerAbilityWheel] '{name}' has no Image dragged into the {SliceName(i)} slot — that slice will render nothing. Wire the four icon Images in the Inspector.", this);
            }
        }

        if (highlightImage == null)
            Debug.LogWarning($"[PlayerAbilityWheel] '{name}' has no Highlight Image assigned — the confirm-flash wedge will not render. Drag the wedge Image into the Highlight Image field.", this);

        CloseWheel();
        wasOpen = false;
    }

    void Update()
    {
        bool open = IsOpen;

        if (open != wasOpen)
        {
            if (open) OpenWheel();
            else CloseWheel();
            wasOpen = open;
        }

        // Put the confirm flash away once its hold time is up. Unscaled so the
        // flash still reads at a normal speed if the game is ever slowed.
        if (confirmTimer > 0f)
        {
            confirmTimer -= Time.unscaledDeltaTime;
            if (confirmTimer <= 0f) HideHighlight();
        }
    }

    private void OpenWheel()
    {
        HideHighlight();
        confirmTimer = 0f;
        if (wheelRoot != null) wheelRoot.SetActive(true);
        ShowWheelMessage(BuildWheelMessage());
    }

    private void CloseWheel()
    {
        confirmTimer = 0f;
        HideHighlight();
        HideWheelMessage();
        if (wheelRoot != null) wheelRoot.SetActive(false);
    }

    private void OnSlotTop(InputAction.CallbackContext ctx) { RecordInputDevice(ctx); FireSlot(0); }
    private void OnSlotRight(InputAction.CallbackContext ctx) { RecordInputDevice(ctx); FireSlot(1); }
    private void OnSlotBottom(InputAction.CallbackContext ctx) { RecordInputDevice(ctx); FireSlot(2); }
    private void OnSlotLeft(InputAction.CallbackContext ctx) { RecordInputDevice(ctx); FireSlot(3); }

    // The seam. Everything above this is the wheel; whatever the mission class
    // system adds later plugs in HERE, so the input, visibility and suppression
    // plumbing never has to change again.
    //
    // Note the IsOpen check is what makes sharing the face buttons safe: with the
    // wheel closed these callbacks still fire (the actions are always enabled) and
    // this early-return is what hands A/B/X back to jump/dodge/interact.
    private void FireSlot(int slice)
    {
        if (!IsOpen) return;

        ShowHighlight(slice, confirmTint);
        confirmTimer = confirmHoldTime;

        // TODO: dispatch the ability the mission class put in this slot.
        if (logDispatch)
            Debug.Log($"[PlayerAbilityWheel] Slot {slice} ({SliceName(slice)}) pressed — no ability bound yet.");
    }

    // Entry point for the mission class loadout: swap what a slice shows when the
    // player picks a class. Writes straight to the Image's sprite so the swap is
    // live whether the wheel is open or closed. Nothing calls this yet.
    public void SetSlotIcon(int slice, Sprite icon)
    {
        if (iconSlots == null || slice < 0 || slice >= iconSlots.Length) return;
        SetIcon(iconSlots[slice], icon);
    }

    private void RecordInputDevice(InputAction.CallbackContext ctx)
    {
        if (ctx.control != null)
            lastInputWasGamepad = ctx.control.device is Gamepad;
    }

    private static string SliceName(int slice)
    {
        switch (slice)
        {
            case 0: return "top";
            case 1: return "right";
            case 2: return "bottom";
            case 3: return "left";
            default: return "?";
        }
    }

    // Names the buttons for whichever device was last used, matching the slot
    // bindings above. Replaced by real ability names once a loadout exists.
    private string BuildWheelMessage()
    {
        return lastInputWasGamepad
            ? "Abilities:  Y / B / A / X"
            : "Abilities:  1 / 2 / 3 / 4";
    }

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

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    // slice: 0 = TOP, 1 = RIGHT, 2 = BOTTOM, 3 = LEFT. The wedge sprite is
    // authored on the TOP slice, so each step round the wheel is -90 degrees.
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
