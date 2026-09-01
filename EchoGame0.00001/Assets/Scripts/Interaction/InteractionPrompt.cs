using UnityEngine;
using TMPro;

// The floating prompt that appears over whatever the player can interact with.
// One of these in the scene, on a world-space canvas. PlayerInteractor moves it onto
// the object and fills in the text, so nothing needs setting up per object.
[RequireComponent(typeof(CanvasGroup))]
public class InteractionPrompt : MonoBehaviour
{
    [Header("Text")]
    [Tooltip("The TMP text this writes into. Drag the text object from inside the prompt.")]
    [SerializeField] private TMP_Text label;
    [Tooltip("How the line is put together. {0} = the button letter, {1} = the object's Prompt Text.")]
    [SerializeField] private string format = "[{0}]  {1}";

    [Header("Look")]
    [Tooltip("How fast the prompt fades in and out. Higher is snappier. Set it very high for an instant pop.")]
    [SerializeField] private float fadeSpeed = 12f;
    [Tooltip("Untick if something else already turns this to face the camera.")]
    [SerializeField] private bool faceCamera = true;

    private CanvasGroup group;
    private Camera cam;
    private bool visible;
    // What the label currently reads, so the string only gets rebuilt when it
    // actually changes. Show runs every frame while a prompt is up, and
    // formatting it each time would allocate a new string per frame.
    private string shownText;
    private string shownButton;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();

        // Start hidden, or the prompt flashes on the first frame before the interactor
        // has worked out whether anything is nearby.
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    void Start()
    {
        if (label == null)
            Debug.LogWarning($"[InteractionPrompt] '{name}' has no Label wired, so the prompt will come up blank. Drag the TMP text object from inside the prompt into the Label field.", this);
    }

    public void Show(string text, string buttonLabel, Vector3 worldPosition)
    {
        transform.position = worldPosition;
        visible = true;

        if (text == shownText && buttonLabel == shownButton) return;
        shownText = text;
        shownButton = buttonLabel;
        if (label != null) label.text = string.Format(format, buttonLabel, text);
    }

    public void Hide()
    {
        visible = false;
    }

    void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        // Copying the camera's forward, not LookAt. LookAt aims the text's +Z at the
        // camera, which draws it mirrored.
        if (faceCamera) transform.forward = cam.transform.forward;

        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
    }
}
