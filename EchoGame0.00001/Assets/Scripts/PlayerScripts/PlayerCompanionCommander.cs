using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCompanionCommander : MonoBehaviour
{
    [Header("Aim")]
    [Tooltip("Max distance from the camera the reticle will pick up an enemy.")]
    [SerializeField] private float aimRange = 60f;
    [Tooltip("Layers considered valid command targets. Set to EnemyLayer.")]
    [SerializeField] private LayerMask enemyMask;

    [Header("Feedback")]
    [SerializeField] private bool logCommands = true;

    [Header("References")]
    [Tooltip("Crosshair driving the aim reticle. Usually lives on the HUD canvas, not the Player. Auto-found if left empty.")]
    [SerializeField] private PlayerCrosshair crosshair;

    private Camera cam;
    private InputAction commandAction;

    void Awake()
    {
        // Bindings live in code rather than PlayerControls.inputactions so this
        // new action doesn't force a regeneration of PlayerControls.cs.
        commandAction = new InputAction("CommandCompanion", InputActionType.Button);
        commandAction.AddBinding("<Mouse>/rightButton");
        commandAction.AddBinding("<Gamepad>/leftShoulder");

        if (crosshair == null) crosshair = FindObjectOfType<PlayerCrosshair>();
    }

    void OnEnable()
    {
        commandAction.performed += OnCommand;
        commandAction.Enable();
    }

    void OnDisable()
    {
        commandAction.performed -= OnCommand;
        commandAction.Disable();
    }

    void Start()
    {
        cam = Camera.main;
    }

    private void OnCommand(InputAction.CallbackContext ctx)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Transform enemy = PickEnemyUnderReticle();
        if (enemy == null)
        {
            if (logCommands) Debug.Log("Companion command: no enemy under reticle.");
            return;
        }

        DispatchAttack(enemy);
    }

    private Transform PickEnemyUnderReticle()
    {
        // Reuse whatever the crosshair is currently highlighting — that way the
        // enemy the player sees locked-on is exactly the one who gets commanded,
        // even if a second raycast on this frame would disagree due to motion.
        if (crosshair != null && crosshair.IsOverEnemy)
            return crosshair.CurrentEnemy;

        // Screen center is the "point at" reticle for a third-person cam — using the
        // mouse position would break gamepad and give inconsistent results when the
        // cursor drifts off-center.
        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        Ray ray = cam.ScreenPointToRay(screenCenter);
        if (Physics.Raycast(ray, out RaycastHit hit, aimRange, enemyMask, QueryTriggerInteraction.Ignore))
            return hit.transform;
        return null;
    }

    private void DispatchAttack(Transform enemy)
    {
        CompanionCommand[] companions = FindObjectsOfType<CompanionCommand>();
        for (int i = 0; i < companions.Length; i++)
            companions[i].CommandAttack(enemy);

        if (logCommands) Debug.Log($"Companion command: {companions.Length} companion(s) → {enemy.name}");
    }
}
