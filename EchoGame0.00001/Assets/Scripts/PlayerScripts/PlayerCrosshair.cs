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

    private Camera cam;
    private RectTransform reticleRect;
    private Vector3 baseScale;
    private float lockBlend;

    // Exposed so other scripts (e.g. PlayerCompanionCommander) can use the same
    // target the player sees highlighted, instead of running a second raycast.
    public bool IsOverEnemy { get; private set; }
    public Transform CurrentEnemy { get; private set; }

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

        CurrentEnemy = Raycast();
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
        if (Physics.Raycast(ray, out RaycastHit hit, aimRange, enemyMask, QueryTriggerInteraction.Ignore))
            return hit.transform;
        return null;
    }
}
