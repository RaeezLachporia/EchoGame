using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

// Goes on the player. Owns the interact button, works out which nearby object the
// player is looking at, and drives the floating prompt.
//
// The player owns the input rather than each object, so ten interactables in a room
// still means one action and one winner per press.
public class PlayerInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The floating prompt this drives. Auto-found in the scene if left empty.")]
    [SerializeField] private InteractionPrompt prompt;
    [Tooltip("Camera used to work out what the player is facing. Uses the main camera if left empty.")]
    [SerializeField] private Camera viewCamera;

    [Header("Blocking")]
    [Tooltip("Command wheel. While its ally or enemy picker is open the prompt hides and presses are ignored. Auto-found if empty.")]
    [SerializeField] private CommandWheel commandWheel;
    [Tooltip("Untick to let the player still interact while the command wheel is open.")]
    [SerializeField] private bool blockedByWheel = true;
    [Tooltip("The player's ability wheel. Interact shares gamepad X with its LEFT slice, so presses are ignored while the right bumper holds the wheel open. Auto-found if empty.")]
    [SerializeField] private PlayerAbilityWheel abilityWheel;
    [Tooltip("Aim script. The prompt hides while aiming. Auto-found if empty.")]
    [SerializeField] private PlayerAimZoom aim;
    [Tooltip("Untick to let the player still interact while aiming.")]
    [SerializeField] private bool blockedByAiming = true;

    [Header("Targeting")]
    [Tooltip("A rival object has to be this many times nearer to steal the prompt from the one already showing. 1 makes two objects side by side swap every frame.")]
    [SerializeField, Min(1f)] private float switchAdvantage = 1.2f;

    [Header("Button Labels")]
    [Tooltip("Shown while the player is on keyboard.")]
    [SerializeField] private string keyboardLabel = "E";
    [Tooltip("Shown on an Xbox or generic controller.")]
    [SerializeField] private string xboxLabel = "X";
    [Tooltip("Shown on a PlayStation controller. Same physical button as the Xbox one, different name printed on the pad.")]
    [SerializeField] private string playstationLabel = "Square";

    [Header("Debug")]
    [Tooltip("Log what the prompt lands on and what happens when the button is pressed.")]
    [SerializeField] private bool logInteractions = true;

    private InputAction interactAction;
    private Interactable current;
    private bool suppressThisPress;

    // What the prompt is currently offering, or null.
    public Interactable Current => current;

    // True for the whole press that fired an interaction. Nothing reads it yet, since
    // nothing else is bound to E or buttonWest. It's here so that when something is,
    // it can skip the press instead of both firing, the way InputManager checks
    // CommandWheel.SuppressJumpThisPress before jumping.
    public bool SuppressThisPress => suppressThisPress;

    void Awake()
    {
        interactAction = new InputAction("Interact", InputActionType.Button);
        interactAction.AddBinding("<Keyboard>/e");
        // buttonWest is X on Xbox and Square on PlayStation. One button, two names.
        interactAction.AddBinding("<Gamepad>/buttonWest");
        interactAction.performed += OnInteractPressed;

        if (prompt == null) prompt = FindObjectOfType<InteractionPrompt>();
        if (commandWheel == null) commandWheel = FindObjectOfType<CommandWheel>();
        if (abilityWheel == null) abilityWheel = FindObjectOfType<PlayerAbilityWheel>();
        if (aim == null) aim = GetComponent<PlayerAimZoom>();
    }

    void OnEnable() { interactAction.Enable(); }
    void OnDisable() { interactAction.Disable(); }

    void Start()
    {
        if (prompt == null)
            Debug.LogWarning($"[PlayerInteractor] '{name}' found no InteractionPrompt in the scene, so nothing will ever show over an interactable. Put one on a world-space canvas, or drag it into the Prompt field.", this);
    }

    void Update()
    {
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
            if (viewCamera == null) return;
        }

        Interactable next = Blocked ? null : FindBest();

        if (logInteractions && next != current)
            Debug.Log($"[PlayerInteractor] Prompt {(next != null ? "on '" + next.name + "'" : "cleared")}.", next);

        current = next;

        if (prompt == null) return;
        if (current != null) prompt.Show(current.PromptText, ButtonLabel, current.PromptPosition);
        else prompt.Hide();
    }

    // Cleared the frame after a press, so the latch covers the whole button event
    // whatever order the callbacks happen to run in.
    void LateUpdate()
    {
        suppressThisPress = false;
    }

    private bool Blocked =>
        (blockedByWheel && commandWheel != null && commandWheel.IsPickingTarget)
        || (blockedByWheel && abilityWheel != null && abilityWheel.IsOpen)
        || (blockedByAiming && aim != null && aim.IsAiming);

    private void OnInteractPressed(InputAction.CallbackContext ctx)
    {
        // Blocked only clears the target in Update, but input callbacks run BEFORE
        // Update — so on the frame the wheel opens, 'current' is still last frame's
        // target and X would interact on its way to becoming a wheel slice. The
        // prompt-hiding above is cosmetic; this is what actually swallows the press.
        if (abilityWheel != null && abilityWheel.IsOpen) return;

        if (current == null) return;

        Interactable target = current;
        bool done = target.Interact(gameObject);
        if (done) suppressThisPress = true;

        if (logInteractions)
            Debug.Log($"[PlayerInteractor] {(done ? "Interacted with" : "Could not interact with")} '{target.name}'.", target);
    }

    // Nearest object that's both in range and roughly in front of the player.
    //
    // Facing is a gate, not a ranking. Sorting by angle instead would let a distant
    // object dead ahead steal the prompt from the one you're standing on top of.
    private Interactable FindBest()
    {
        IReadOnlyList<Interactable> candidates = Interactable.Active;

        Interactable best = null;
        float bestDistance = float.MaxValue;
        // Stays at MaxValue unless the object already showing still passes both gates,
        // so turning away from it drops the prompt instead of sticking to it.
        float currentDistance = float.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            Interactable candidate = candidates[i];
            if (candidate == null || !candidate.CanInteract) continue;

            float distance = Vector3.Distance(transform.position, candidate.transform.position);
            if (distance > candidate.InteractRange) continue;

            Vector3 fromCamera = candidate.PromptPosition - viewCamera.transform.position;
            if (fromCamera.sqrMagnitude > 0.0001f
                && Vector3.Angle(viewCamera.transform.forward, fromCamera) > candidate.LookAngle) continue;

            if (candidate == current) currentDistance = distance;

            if (distance >= bestDistance) continue;
            best = candidate;
            bestDistance = distance;
        }

        // Keep what's already showing unless the rival is clearly nearer. Without this
        // two objects side by side swap the prompt back and forth every frame.
        if (current != null && best != current
            && currentDistance < float.MaxValue
            && bestDistance * switchAdvantage >= currentDistance)
            return current;

        return best;
    }

    private string ButtonLabel
    {
        get
        {
            // lastUpdateTime is per device, so this follows what the player actually
            // has in their hands rather than what happens to be plugged in.
            bool gamepad = Gamepad.current != null
                && (Keyboard.current == null || Gamepad.current.lastUpdateTime >= Keyboard.current.lastUpdateTime);

            if (!gamepad) return keyboardLabel;
            return Gamepad.current is DualShockGamepad ? playstationLabel : xboxLabel;
        }
    }
}
