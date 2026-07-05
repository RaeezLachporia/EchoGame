using UnityEngine;
using UnityEngine.UI;

public class PlayerCrosshair : MonoBehaviour
{
    [Header("Reticle")]
    [Tooltip("Reticle graphic (Image / RawImage / Text). Its RectTransform is what scales.")]
    [SerializeField] private Graphic reticle;
    [Tooltip("Extra graphics that share the reticle's tint (e.g. the 4 lines of a '+' crosshair).")]
    [SerializeField] private Graphic[] extraGraphics;
    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.65f);
    [SerializeField] private Color targetColor = new Color(1f, 0.3f, 0.3f, 1f);
    [Tooltip("Scale multiplier when over a target.")]
    [SerializeField, Range(1f, 2.5f)] private float targetScale = 1.25f;
    [Tooltip("Blend speed between idle and locked visuals.")]
    [SerializeField] private float transitionSpeed = 12f;

    [Header("Aim")]
    [SerializeField] private float aimRange = 60f;
    [Tooltip("Layers that count as targets. Set to EnemyLayer.")]
    [SerializeField] private LayerMask enemyMask;
    [Tooltip("Aim assist radius around the crosshair. Bigger = more forgiving. 0 = no assist.")]
    [SerializeField, Min(0f)] private float assistRadius = 1.5f;

    private Camera cam;
    private RectTransform reticleRect;
    private Vector3 baseScale;
    private float lockBlend;

    // So other scripts (e.g. PlayerCompanionCommander) can grab the same target
    // without doing their own raycast.
    public bool IsOverEnemy { get; private set; }
    public Transform CurrentEnemy { get; private set; }

    // PlayerLockOn writes this to force CurrentEnemy. Keeps lock-on on the same
    // highlight / command pipeline as free-aim.
    public Transform LockOverride { get; set; }

    void Awake()
    {
        if (reticle != null)
        {
            reticleRect = reticle.rectTransform;
            baseScale = reticleRect.localScale;
        }
    }

    void Start()
    {
        cam = Camera.main;
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        // Drop a dead/disabled lock so the crosshair isn't stuck on a corpse.
        if (LockOverride != null && !LockOverride.gameObject.activeInHierarchy)
            LockOverride = null;

        CurrentEnemy = LockOverride != null ? LockOverride : Raycast();
        IsOverEnemy = CurrentEnemy != null;

        if (reticle == null) return;

        float target = IsOverEnemy ? 1f : 0f;
        lockBlend = Mathf.MoveTowards(lockBlend, target, transitionSpeed * Time.deltaTime);

        Color tint = Color.Lerp(idleColor, targetColor, lockBlend);
        reticle.color = tint;
        if (extraGraphics != null)
        {
            for (int i = 0; i < extraGraphics.Length; i++)
                if (extraGraphics[i] != null) extraGraphics[i].color = tint;
        }

        if (reticleRect != null)
            reticleRect.localScale = baseScale * Mathf.Lerp(1f, targetScale, lockBlend);
    }

    private Transform Raycast()
    {
        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        Ray ray = cam.ScreenPointToRay(screenCenter);

        // Direct hit wins over anything the assist sphere finds — precise aiming
        // still lets the player pick between overlapping enemies.
        if (Physics.Raycast(ray, out RaycastHit direct, aimRange, enemyMask, QueryTriggerInteraction.Ignore))
            return direct.transform;

        if (assistRadius <= 0f) return null;

        RaycastHit[] hits = Physics.SphereCastAll(ray, assistRadius, aimRange, enemyMask, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0) return null;

        // Pick the enemy closest to the crosshair on screen — matches "which one
        // is my reticle nearest to" better than closest-by-world-distance.
        Transform best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (t == null) continue;
            Vector3 vp = cam.WorldToViewportPoint(t.position);
            if (vp.z <= 0f) continue;

            float dx = vp.x - 0.5f;
            float dy = vp.y - 0.5f;
            float score = dx * dx + dy * dy;
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }
}
