using UnityEngine;

public class PlayerTargetHighlighter : MonoBehaviour
{
    [Tooltip("Only glow while aim is held. Uncheck to glow whenever the crosshair sits on an enemy.")]
    [SerializeField] private bool requireAim = true;
    [Tooltip("Log every target change — helps diagnose why nothing's glowing.")]
    [SerializeField] private bool debugLog = false;

    [Header("References")]
    [Tooltip("Crosshair — auto-found if empty.")]
    [SerializeField] private PlayerCrosshair crosshair;

    private PlayerAimZoom aim;
    private EnemyHighlight currentHighlight;
    private bool wasAiming;

    void Awake()
    {
        if (crosshair == null) crosshair = FindObjectOfType<PlayerCrosshair>();
        aim = GetComponent<PlayerAimZoom>();
        if (debugLog)
        {
            Debug.Log($"[TargetHighlighter] Awake — crosshair {(crosshair != null ? "OK" : "MISSING")}, aim {(aim != null ? "OK" : "MISSING")}");
        }
    }

    void Update()
    {
        if (crosshair == null) return;

        if (debugLog)
        {
            bool aiming = aim != null && aim.IsAiming;
            if (aiming != wasAiming)
            {
                Debug.Log($"[TargetHighlighter] aim = {aiming}, crosshair.CurrentEnemy = {(crosshair.CurrentEnemy != null ? crosshair.CurrentEnemy.name : "null")}");
                wasAiming = aiming;
            }
        }

        // No aim → no highlight. If there's no PlayerAimZoom at all, aim is null
        // and this still treats it as "not aiming". Lock-on skips the aim gate —
        // a lock should stay framed and glowing whether the player's aiming or not.
        bool aimActive = !requireAim || (aim != null && aim.IsAiming);
        bool locked = crosshair.LockOverride != null;

        EnemyHighlight desired = null;
        if ((aimActive || locked) && crosshair.CurrentEnemy != null)
        {
            // The ray can land on a child collider (weapon, hitbox mesh). The
            // EnemyHighlight lives on the root, so walk up to find it.
            desired = crosshair.CurrentEnemy.GetComponentInParent<EnemyHighlight>();

            if (debugLog && desired == null)
                Debug.Log($"[TargetHighlighter] Ray hit {crosshair.CurrentEnemy.name}, but no EnemyHighlight found on it or any parent.");
        }

        if (desired == currentHighlight) return;

        // Swap the tint. Each EnemyHighlight fades on its own — panning A → B
        // mid-fade lets A fade out while B fades in.
        if (currentHighlight != null) currentHighlight.SetHighlighted(false);
        if (desired != null) desired.SetHighlighted(true);
        currentHighlight = desired;

        if (debugLog)
            Debug.Log($"[TargetHighlighter] target = {(desired != null ? desired.name : "none")}");
    }
}
