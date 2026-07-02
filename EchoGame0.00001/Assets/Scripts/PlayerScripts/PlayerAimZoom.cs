using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAimZoom : MonoBehaviour
{
    [Header("Zoom")]
    [Tooltip("Field of view while not aiming. Auto-populated from the camera on Start if left at default.")]
    [SerializeField] private float defaultFov = 60f;
    [Tooltip("Field of view while aim is held. Lower = tighter zoom.")]
    [SerializeField] private float aimFov = 35f;
    [Tooltip("How fast the FOV blends between default and aim (units per second of the 0..1 blend).")]
    [SerializeField] private float zoomSpeed = 8f;

    [Header("References")]
    [Tooltip("Camera whose FOV is driven. Falls back to Camera.main.")]
    [SerializeField] private Camera aimCamera;
    [Tooltip("CanvasGroup wrapping the crosshair — its alpha is driven by the aim blend.")]
    [SerializeField] private CanvasGroup reticleGroup;

    [Header("Reticle Opacity")]
    [Tooltip("Crosshair alpha while the player is NOT holding aim.")]
    [SerializeField, Range(0f, 1f)] private float restAlpha = 0.15f;
    [Tooltip("Crosshair alpha while aim is fully held.")]
    [SerializeField, Range(0f, 1f)] private float aimAlpha = 1f;

    private InputAction aimAction;

    // 0 = not aiming, 1 = fully aimed. Other scripts (PlayerCrosshair) can read
    // this to drive their own visuals off the same blend, so opacity and FOV
    // stay perfectly in sync.
    public float AimBlend { get; private set; }
    public bool IsAiming => aimAction != null && aimAction.IsPressed();

    void Awake()
    {
        aimAction = new InputAction("Aim", InputActionType.Button);
        aimAction.AddBinding("<Gamepad>/leftTrigger");
        // Left Alt is the "aim/focus" convention on KBM in third-person games —
        // avoids colliding with attack (LMB) and companion command (RMB).
        aimAction.AddBinding("<Keyboard>/leftAlt");
    }

    void OnEnable() { aimAction.Enable(); }
    void OnDisable() { aimAction.Disable(); }

    void Start()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (aimCamera != null) defaultFov = aimCamera.fieldOfView;
    }

    void Update()
    {
        float target = IsAiming ? 1f : 0f;
        AimBlend = Mathf.MoveTowards(AimBlend, target, zoomSpeed * Time.deltaTime);

        if (aimCamera != null)
            aimCamera.fieldOfView = Mathf.Lerp(defaultFov, aimFov, AimBlend);

        if (reticleGroup != null)
            reticleGroup.alpha = Mathf.Lerp(restAlpha, aimAlpha, AimBlend);
    }
}
