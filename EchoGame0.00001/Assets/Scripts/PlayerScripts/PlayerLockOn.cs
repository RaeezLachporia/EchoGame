using UnityEngine;
using UnityEngine.InputSystem;

// Runs after CameraManager so we can overwrite the free-look rotation with the
// lock framing on the same frame.
[DefaultExecutionOrder(200)]
public class PlayerLockOn : MonoBehaviour
{
    [Header("Range")]
    [Tooltip("Max distance to grab a lock.")]
    [SerializeField] private float lockRange = 25f;
    [Tooltip("Lock drops if the target gets past this. Kept slightly bigger than Lock Range so edges don't flap.")]
    [SerializeField] private float breakRange = 30f;

    [Header("Framing")]
    [Tooltip("Pitch clamps while locked on. Match CameraManager's free-look clamps.")]
    [SerializeField] private float minPivotAngle = -35f;
    [SerializeField] private float maxPivotAngle = 35f;
    [Tooltip("Raises the framing point up the enemy (feet → torso).")]
    [SerializeField] private float verticalTargetOffset = 1.2f;

    [Header("References")]
    [Tooltip("Camera rig. Auto-found if empty.")]
    [SerializeField] private CameraManager cameraManager;
    [Tooltip("Crosshair — set as LockOverride so the red highlight and command pipeline follow the lock. Auto-found if empty.")]
    [SerializeField] private PlayerCrosshair crosshair;

    private InputAction lockAction;
    private Transform locked;

    public bool IsLocked => locked != null;
    public Transform LockedTarget => locked;

    void Awake()
    {
        // Bind in code so this doesn't force a regen of the input asset.
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
        // If we're disabled mid-lock, drop the highlight — otherwise the crosshair
        // stays stuck on an enemy the player can't unlock from.
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
        // One call per button press and enemy counts are small — this is cheap.
        // Swap to OverlapSphere or a registry if that changes.
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

        // Target died, disabled, or drifted too far — drop the lock.
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
        // eulerAngles is 0..360; bring pitch into -180..180 or the clamp won't work.
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, minPivotAngle, maxPivotAngle);

        // Push angles back so free-look picks up where the lock left off instead
        // of snapping to a stale angle when disengaged.
        cameraManager.lookAngle = yaw;
        cameraManager.pivotAngle = pitch;

        // CameraManager already rotated from stick input earlier this frame — overwrite
        // now so the render sees the lock framing.
        cameraManager.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cameraManager.cameraPivot != null)
            cameraManager.cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
