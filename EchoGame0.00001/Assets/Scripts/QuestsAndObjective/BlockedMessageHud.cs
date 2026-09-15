using UnityEngine;
using TMPro;

// The "you can't go through yet" banner. One of these on the player's HUD, shared by
// every ObjectiveBarrier in the level — a barrier calls Show when the player walks up
// to it while it's still shut, and Hide when they walk away or it opens.
//
// Shared rather than one per door for the same reason PlayerInteractor drives a single
// InteractionPrompt: the message is a property of the player's screen, not of each
// wall, so there's one label and one barrier can't leave another's text stuck on.
//
// Put it on a full-screen (or centred) panel inside PlayerUi with a CanvasGroup and a
// TMP text. It fades rather than snapping so a door you brush past doesn't flash.
public class BlockedMessageHud : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Fades the banner in and out. Auto-found on this object if left empty — add a CanvasGroup component if there isn't one.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("The TMP text the message is written into. Drag the text object from inside your banner panel. Nothing shows without it.")]
    [SerializeField] private TMP_Text messageText;

    [Header("Fade")]
    [Tooltip("How fast the banner fades in and out. Higher is snappier. This is an alpha-per-second rate, so 8 fades in roughly an eighth of a second.")]
    [SerializeField, Min(0.1f)] private float fadeSpeed = 8f;

    // Which barrier's message is currently up. A barrier only gets to hide the banner
    // if the message showing is its own — otherwise, with two doors close together,
    // one walking out of range would wipe the other's message while the player is
    // still standing at it.
    private Component currentOwner;
    private float targetAlpha;

    void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        // Start hidden and stay click-through — this is a read-only banner, never
        // something the player interacts with.
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    void Start()
    {
        if (canvasGroup == null)
            Debug.LogWarning($"[BlockedMessageHud] '{name}' has no CanvasGroup, so the banner can't fade and won't show. Add a CanvasGroup component to this object, or drag one into the Canvas Group field.", this);
        if (messageText == null)
            Debug.LogWarning($"[BlockedMessageHud] '{name}' has no Message Text, so the banner will fade an empty box. Drag the TMP text from inside your banner panel into the Message Text field.", this);
    }

    // Show a message, owned by the barrier asking for it. Calling again with a
    // different owner just swaps the text — the nearest door the player is standing
    // at wins by being the last to call each frame.
    public void Show(Component owner, string message)
    {
        currentOwner = owner;
        if (messageText != null) messageText.text = message;
        targetAlpha = 1f;
    }

    // Hide, but only if the message up is the one this owner put there. A hide from a
    // barrier that isn't the current owner is ignored.
    public void Hide(Component owner)
    {
        if (owner != currentOwner) return;
        currentOwner = null;
        targetAlpha = 0f;
    }

    void Update()
    {
        if (canvasGroup == null) return;
        if (Mathf.Approximately(canvasGroup.alpha, targetAlpha)) return;

        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
    }
}
