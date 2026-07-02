using UnityEngine;

public class EnemyHighlight : MonoBehaviour
{
    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.2f, 0.2f, 1f);
    [Tooltip("How far the base color shifts toward the highlight at full blend. 1 = full override, 0.5 = 50% mix.")]
    [SerializeField, Range(0f, 1f)] private float intensity = 0.7f;
    [Tooltip("Blend units per second — higher = snappier fade in/out.")]
    [SerializeField] private float fadeSpeed = 12f;

    private Renderer[] renderers;
    private Color[][] baseColors;      // per-renderer, per-submaterial cached original color
    private MaterialPropertyBlock block;
    private float blend;
    private bool highlighted;

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

    void Update()
    {
        float target = highlighted ? 1f : 0f;
        float next = Mathf.MoveTowards(blend, target, fadeSpeed * Time.deltaTime);
        // Nothing changed — skip the per-renderer work entirely. Matters when a lot
        // of enemies exist and only one is being tinted at a time.
        if (Mathf.Approximately(next, blend)) return;
        blend = next;
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
                renderers[i].GetPropertyBlock(block, j);
                block.SetColor(BaseColorId, final);
                block.SetColor(ColorId, final);
                renderers[i].SetPropertyBlock(block, j);
            }
        }
    }
}
