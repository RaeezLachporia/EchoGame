using UnityEngine;

public class EnemyHighlight : MonoBehaviour
{
    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.2f, 0.2f, 1f);
    [Tooltip("How far the base color shifts toward the highlight at full blend. 1 = full override, 0.5 = 50% mix.")]
    [SerializeField, Range(0f, 1f)] private float intensity = 0.7f;
    [Tooltip("Blend units per second — higher = snappier fade in/out.")]
    [SerializeField] private float fadeSpeed = 12f;

    [Header("Hit Flash")]
    [Tooltip("Colour flashed when this enemy is hit. White reads as impact; the aim highlight above stays red so the two never look the same.")]
    [SerializeField] private Color flashColor = Color.white;
    [Tooltip("How quickly the hit flash decays back out. Higher = snappier.")]
    [SerializeField] private float flashFadeSpeed = 6f;

    private Renderer[] renderers;
    private Color[][] baseColors;      // per-renderer, per-submaterial cached original color
    private MaterialPropertyBlock block;
    private float blend;
    private bool highlighted;
    // Hit flashes run THROUGH this component rather than tinting the enemy
    // separately — otherwise they'd fight PlayerTargetHighlighter for the same
    // material colours and whichever wrote last would win.
    private float flash;

    // Set both — URP uses _BaseColor, Built-in uses _Color. Setting a property the
    // shader doesn't declare on an MPB is a no-op, so we avoid branching per-frame.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        // Include inactive so weapons/attachments toggled on mid-combat still tint.
        renderers = GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();
        CacheBaseColors();
    }

    // Enemies almost always die while highlighted — you kill what you're aiming at.
    // A pooled enemy keeps blend/highlighted across lives, so without this it comes
    // back still tinted red.
    void OnEnable()
    {
        highlighted = false;
        blend = 0f;
        flash = 0f;
        ApplyTint();
    }

    private void CacheBaseColors()
    {
        baseColors = new Color[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = renderers[i].sharedMaterials;
            baseColors[i] = new Color[mats.Length];
            for (int j = 0; j < mats.Length; j++)
            {
                Color c = Color.white;
                Material m = mats[j];
                if (m != null)
                {
                    if (m.HasProperty(BaseColorId)) c = m.GetColor(BaseColorId);
                    else if (m.HasProperty(ColorId)) c = m.GetColor(ColorId);
                }
                baseColors[i][j] = c;
            }
        }
    }

    public void SetHighlighted(bool on)
    {
        highlighted = on;
    }

    // Punch the enemy to full flash colour, then let it decay. Called when a shot
    // lands (PietDoubletap) so a hit reads on the target itself. Safe to call
    // mid-highlight — the flash sits on top and the aim tint resumes underneath.
    public void Flash()
    {
        flash = 1f;
        ApplyTint();
    }

    void Update()
    {
        float target = highlighted ? 1f : 0f;
        float nextBlend = Mathf.MoveTowards(blend, target, fadeSpeed * Time.deltaTime);
        float nextFlash = Mathf.MoveTowards(flash, 0f, flashFadeSpeed * Time.deltaTime);

        // Nothing changed — skip the per-renderer work entirely. Matters when a lot
        // of enemies exist and only one is being tinted at a time.
        if (Mathf.Approximately(nextBlend, blend) && Mathf.Approximately(nextFlash, flash)) return;
        blend = nextBlend;
        flash = nextFlash;
        ApplyTint();
    }

    private void ApplyTint()
    {
        float t = blend * intensity;
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = renderers[i].sharedMaterials;
            for (int j = 0; j < mats.Length; j++)
            {
                Color final = Color.Lerp(baseColors[i][j], highlightColor, t);
                // Flash layers OVER the aim highlight rather than replacing it, so a
                // hit still reads while the enemy is locked on and the red tint
                // returns cleanly once the flash decays.
                if (flash > 0f) final = Color.Lerp(final, flashColor, flash);
                renderers[i].GetPropertyBlock(block, j);
                block.SetColor(BaseColorId, final);
                block.SetColor(ColorId, final);
                renderers[i].SetPropertyBlock(block, j);
            }
        }
    }
}
