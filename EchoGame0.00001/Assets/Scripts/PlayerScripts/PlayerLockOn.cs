using UnityEngine;
using UnityEngine.InputSystem;

// Runs after CameraManager (default order 0) so we override the free-look
// rotation with the lock-on framing on the same frame it was written.
[DefaultExecutionOrder(200)]
public class PlayerLockOn : MonoBehaviour
{
    [Header("Range")]
    [Tooltip("Max distance from the player when acquiring a lock target.")]
    [SerializeField] private float lockRange = 25f;
    [Tooltip("Lock breaks if the target drifts beyond this distance. Slightly larger than lockRange so grazing edges don't ping-pong the lock.")]
    [SerializeField] private float breakRange = 30f;

    [Header("Framing")]
    [Tooltip("Camera pitch clamps while locked on. Match CameraManager's free-look clamps unless you want the lock to tilt further.")]
    [SerializeField] private float minPivotAngle = -35f;
    [SerializeField] private float maxPivotAngle = 35f;
    [Tooltip("World-space vertical offset added to the target position when framing — usually raises the pivot from the enemy's feet to their torso.")]
    [SerializeField] private float verticalTargetOffset = 1.2f;

    [Header("References")]
    [Tooltip("Camera rig. Auto-found if left empty.")]
    [SerializeField] private CameraManager cameraManager;
    [Tooltip("Crosshair — its LockOverride is set to the locked target so the same red highlight and companion-command pipeline see it. Auto-found if left empty.")]
    [SerializeField] private PlayerCrosshair crosshair;

    private InputAction lockAction;
    private Transform locked;

    public bool IsLocked => locked != null;
    public Transform LockedTarget => locked;

    void Awake()
    {
        // Bound in code so this doesn't force a regen of PlayerControls.inputactions.
        lockAction = new InputAction("LockOn", InputActionType.Button);
        lockAction.AddBinding("<Gamepad>/rightStickPress");
        lockAction.AddBinding("<Mouse>/middleButton");

        if (cameraManager == null) cameraManager = FindObjectOfType<CameraManager>();
        if (crosshair == null) crosshair = FindObjectOfType<PlayerCrosshair>();
    }

    void OnEnable()
    {
        lockAction.performed += OnLockPressed;
        lockAction.Enable();
    }

    void OnDisable()
    {
        lockAction.performed -= OnLockPressed;
        lockAction.Disable();
        // Drop the highlight override if the component gets disabled mid-lock —
        // don't leave the crosshair stuck on an enemy the player can't unlock from.
        Disengage();
    }

    private void OnLockPressed(InputAction.CallbackContext ctx)
    {
        if (locked != null)
        {
            Disengage();
            return;
        }
        Transform t = FindNearestEnemy();
        if (t != null) Engage(t);
    }

    private Transform FindNearestEnemy()
    {
        // Small enemy counts and one call per press → cheap. If enemy counts grow
        // we can swap this for an OverlapSphere or a registry.
        EnemyHighlight[] enemies = FindObjectsOfType<EnemyHighlight>();
        Transform best = null;
        float bestSqr = lockRange * lockRange;
        for (int i = 0; i < enemies.Length; i++)
        {
            Transform t = enemies[i].transform;
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            float sqr = (t.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = t;
            }
        }
        return best;
    }

    private void Engage(Transform target)
    {
        locked = target;
        if (crosshair != null) crosshair.LockOverride = target;
    }

    private void Disengage()
    {
        locked = null;
        if (crosshair != null) crosshair.LockOverride = null;
    }

    void LateUpdate()
    {
        if (locked == null) return;

        // Target died, was disabled, or drifted out of the break radius.
        if (!locked.gameObject.activeInHierarchy ||
            (locked.position - transform.position).sqrMagnitude > breakRange * breakRange)
        {
            Disengage();
            return;
        }

        if (cameraManager == null) return;

        Vector3 pivotPos = cameraManager.cameraPivot != null
            ? cameraManager.cameraPivot.position
            : cameraManager.transform.position;

        Vector3 focus = locked.position + Vector3.up * verticalTargetOffset;
        Vector3 dir = focus - pivotPos;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion look = Quaternion.LookRotation(dir);
        Vector3 e = look.eulerAngles;

        float yaw = e.y;
        float pitch = e.x;
        // eulerAngles returns 0..360 — bring the pitch into -180..180 so the clamp works.
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, minPivotAngle, maxPivotAngle);

        // Write back to CameraManager so free-look resumes from this framing when
        // the player disengages, instead of snapping back to a stale angle.
        cameraManager.lookAngle = yaw;
        cameraManager.pivotAngle = pitch;

        // Apply this frame — CameraManager already set rotations from stick input
        // earlier in the frame; we overwrite so the render sees the locked framing.
        cameraManager.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cameraManager.cameraPivot != null)
            cameraManager.cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
