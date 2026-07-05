using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAimZoom : MonoBehaviour
{
    [Header("Zoom")]
    [Tooltip("FOV while not aiming. Grabbed from the camera on Start if left default.")]
    [SerializeField] private float defaultFov = 60f;
    [Tooltip("FOV while aiming. Lower = tighter zoom.")]
    [SerializeField] private float aimFov = 35f;
    [Tooltip("FOV blend speed.")]
    [SerializeField] private float zoomSpeed = 8f;

    [Header("References")]
    [Tooltip("Camera to zoom. Falls back to Camera.main.")]
    [SerializeField] private Camera aimCamera;
    [Tooltip("CanvasGroup on the crosshair — alpha follows the aim blend.")]
    [SerializeField] private CanvasGroup reticleGroup;

    [Header("Reticle Opacity")]
    [Tooltip("Reticle alpha when not aiming.")]
    [SerializeField, Range(0f, 1f)] private float restAlpha = 0.15f;
    [Tooltip("Reticle alpha when fully aiming.")]
    [SerializeField, Range(0f, 1f)] private float aimAlpha = 1f;

    private InputAction aimAction;

    // 0 = not aiming, 1 = fully aimed. Other scripts (PlayerCrosshair) piggyback
    // on this so their visuals stay in sync with the FOV blend.
    public float AimBlend { get; private set; }
    public bool IsAiming => aimAction != null && aimAction.IsPressed();

    void Awake()
    {
        aimAction = new InputAction("Aim", InputActionType.Button);
        aimAction.AddBinding("<Gamepad>/leftTrigger");
        // Left Alt is the third-person "aim/focus" convention on KBM — doesn't
        // collide with attack (LMB) or companion command (RMB).
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
