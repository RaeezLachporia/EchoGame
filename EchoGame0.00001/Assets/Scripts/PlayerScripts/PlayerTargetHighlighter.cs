using UnityEngine;

public class PlayerTargetHighlighter : MonoBehaviour
{
    [Tooltip("If true, enemies only glow while the aim button is held. Uncheck to highlight any enemy the crosshair sits on.")]
    [SerializeField] private bool requireAim = true;
    [Tooltip("Log every time the highlighted target changes — helps diagnose why nothing is glowing.")]
    [SerializeField] private bool debugLog = false;

    [Header("References")]
    [Tooltip("Crosshair driving the aim reticle. Usually lives on the HUD canvas, not the Player. Auto-found if left empty.")]
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

        // No aim button held → no highlight. Also covers the "no PlayerAimZoom
        // attached" case when requireAim is on: aim ref is null, treated as "not aiming".
        // Lock-on bypasses the aim gate — the whole point of a lock is to keep the
        // target framed and highlighted whether the player is aiming or not.
        bool aimActive = !requireAim || (aim != null && aim.IsAiming);
        bool locked = crosshair.LockOverride != null;

        EnemyHighlight desired = null;
        if ((aimActive || locked) && crosshair.CurrentEnemy != null)
        {
            // Raycast can land on a child collider (hitbox, weapon, mesh) — the
            // EnemyHighlight component lives on the enemy's root. Walk up until
            // we find it so child colliders still resolve to the right enemy.
            desired = crosshair.CurrentEnemy.GetComponentInParent<EnemyHighlight>();

            if (debugLog && desired == null)
                Debug.Log($"[TargetHighlighter] Ray hit {crosshair.CurrentEnemy.name}, but no EnemyHighlight found on it or any parent.");
        }

        if (desired == currentHighlight) return;

        // Swap the tint over. Both scripts drive their own fade, so if the player
        // pans A → B mid-fade, A fades out and B fades in at the same time.
        if (currentHighlight != null) currentHighlight.SetHighlighted(false);
        if (desired != null) desired.SetHighlighted(true);
        currentHighlight = desired;

        if (debugLog)
            Debug.Log($"[TargetHighlighter] target = {(desired != null ? desired.name : "none")}");
    }
}
