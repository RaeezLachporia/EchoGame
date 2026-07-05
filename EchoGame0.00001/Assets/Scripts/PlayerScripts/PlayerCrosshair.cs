using UnityEngine;
using UnityEngine.UI;

public class PlayerCrosshair : MonoBehaviour
{
    [Header("Reticle")]
    [Tooltip("UI graphic to drive — an Image, RawImage, or Text all work. Its RectTransform is the one that scales.")]
    [SerializeField] private Graphic reticle;
    [Tooltip("Optional extra graphics that share the reticle's tint — e.g. the 4 line-Images that make up a '+' crosshair. Leave empty for a single-image reticle.")]
    [SerializeField] private Graphic[] extraGraphics;
    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.65f);
    [SerializeField] private Color targetColor = new Color(1f, 0.3f, 0.3f, 1f);
    [Tooltip("Scale multiplier applied when the reticle is over a valid target.")]
    [SerializeField, Range(1f, 2.5f)] private float targetScale = 1.25f;
    [Tooltip("How fast the reticle blends between idle and locked visuals.")]
    [SerializeField] private float transitionSpeed = 12f;

    [Header("Aim")]
    [SerializeField] private float aimRange = 60f;
    [Tooltip("Layers that count as valid lock-on targets. Set to EnemyLayer.")]
    [SerializeField] private LayerMask enemyMask;
    [Tooltip("World-space radius swept out from the crosshair ray. Larger = more forgiving aim assist. 0 falls back to a hard raycast.")]
    [SerializeField, Min(0f)] private float assistRadius = 1.5f;

    private Camera cam;
    private RectTransform reticleRect;
    private Vector3 baseScale;
    private float lockBlend;

    // Exposed so other scripts (e.g. PlayerCompanionCommander) can use the same
    // target the player sees highlighted, instead of running a second raycast.
    public bool IsOverEnemy { get; private set; }
    public Transform CurrentEnemy { get; private set; }

    // When set, forces CurrentEnemy to this transform regardless of where the
    // crosshair points. PlayerLockOn writes this so lock-on drives the same
    // highlight/command-target pipeline as free-aim.
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

        // Auto-clear a lock override that died or got disabled — otherwise the
        // crosshair would keep pointing at a corpse.
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

        // Hard raycast first — a direct hit always wins over anything the assist
        // sphere finds, so the player can still cleanly pick between overlapping
        // enemies by pointing precisely.
        if (Physics.Raycast(ray, out RaycastHit direct, aimRange, enemyMask, QueryTriggerInteraction.Ignore))
            return direct.transform;

        if (assistRadius <= 0f) return null;

        RaycastHit[] hits = Physics.SphereCastAll(ray, assistRadius, aimRange, enemyMask, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0) return null;

        // Among enemies inside the assist sphere, pick the one closest to the
        // crosshair in screen space — matches the visual expectation of "the
        // one my reticle is nearest to" better than closest-by-world-distance.
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
