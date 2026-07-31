using UnityEngine;

// Ground-fan visualization of what an enemy can currently see. Reads FOV
// angle, range, obstacle mask, and brain state from EnemyFollowPlayer;
// renders a translucent wedge that tints yellow for Patrol, orange for
// Alert-searching (lost sight, walking the last-known grip), and red for
// Alert with live sight of the player.
//
// The fan is a child GameObject with a procedural triangle-fan mesh. Each
// LateUpdate we raycast every arc vertex against the enemy's LOS mask and
// clip the fan to the first hit — so the cone stops at walls instead of
// poking through them.
[RequireComponent(typeof(EnemyFollowPlayer))]
public class EnemyVisionCone : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Color patrolColor = new Color(1f, 0.9f, 0.2f, 0.28f);
    [SerializeField] private Color alertSearchColor = new Color(1f, 0.55f, 0.1f, 0.38f);
    [SerializeField] private Color alertSightColor = new Color(1f, 0.15f, 0.15f, 0.5f);
    [SerializeField] private float groundOffset = 0.05f;
    [SerializeField] private int arcSegments = 32;
    [SerializeField] private float colorLerpSpeed = 8f;

    [Header("Wall Clipping")]
    [Tooltip("Raycast each arc vertex against the enemy's obstacle mask and clip the fan to the first hit. Off = the fan bleeds through walls but saves the per-frame casts.")]
    [SerializeField] private bool clipToObstacles = true;
    [Tooltip("Height above the enemy's feet at which the clip raycast fires. A little above ground avoids snagging on the floor collider but stays below the target's midsection.")]
    [SerializeField] private float clipCastHeight = 0.5f;

    private EnemyFollowPlayer enemy;
    private Mesh mesh;
    private Vector3[] vertices;
    private Material coneMaterial;
    private MeshRenderer meshRenderer;

    void Awake()
    {
        enemy = GetComponent<EnemyFollowPlayer>();
        BuildConeChild();
    }

    void OnDestroy()
    {
        // Material and mesh are runtime instances we own — Unity leaks both
        // if we don't destroy explicitly. Child GameObject follows the parent
        // out on its own.
        if (coneMaterial != null) Destroy(coneMaterial);
        if (mesh != null) Destroy(mesh);
    }

    private void BuildConeChild()
    {
        GameObject go = new GameObject("VisionCone");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, groundOffset, 0f);
        go.transform.localRotation = Quaternion.identity;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.allowOcclusionWhenDynamic = false;

        // Sprites/Default supports vertex colors + alpha blending and ships
        // with URP. Swap for a custom material if it ever gets stripped from
        // a build's Always Included Shaders list.
        coneMaterial = new Material(Shader.Find("Sprites/Default"));
        coneMaterial.color = patrolColor;
        meshRenderer.sharedMaterial = coneMaterial;

        int vertCount = arcSegments + 2;
        vertices = new Vector3[vertCount];
        int[] tris = new int[arcSegments * 3];

        // Vertex 0 is the enemy's feet. The rest are arc points at radius.
        vertices[0] = Vector3.zero;

        float halfRad = enemy.FovAngle * 0.5f * Mathf.Deg2Rad;
        float radius = enemy.DetectionRange;
        for (int i = 0; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            float ang = -halfRad + t * (halfRad * 2f);
            vertices[i + 1] = new Vector3(Mathf.Sin(ang) * radius, 0f, Mathf.Cos(ang) * radius);
        }
        for (int i = 0; i < arcSegments; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2;
        }

        mesh = new Mesh { name = "VisionConeFan" };
        mesh.vertices = vertices;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;
    }

    // LateUpdate so the fan reflects the enemy's rotation *this* frame —
    // running in Update would leave the cone one frame behind their facing.
    void LateUpdate()
    {
        if (clipToObstacles) UpdateFan();
        UpdateColor();
    }

    private void UpdateFan()
    {
        float halfRad = enemy.FovAngle * 0.5f * Mathf.Deg2Rad;
        float radius = enemy.DetectionRange;
        LayerMask mask = enemy.SightObstacles;
        Vector3 worldOrigin = transform.position + Vector3.up * clipCastHeight;
        Quaternion rot = transform.rotation;

        for (int i = 0; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            float ang = -halfRad + t * (halfRad * 2f);
            Vector3 localDir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            Vector3 worldDir = rot * localDir;
            float dist = radius;
            if (Physics.Raycast(worldOrigin, worldDir, out RaycastHit hit, radius, mask, QueryTriggerInteraction.Ignore))
            {
                dist = hit.distance;
            }
            vertices[i + 1] = new Vector3(localDir.x * dist, 0f, localDir.z * dist);
        }
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }

    private void UpdateColor()
    {
        Color target = enemy.State switch
        {
            EnemyFollowPlayer.EnemyState.Alert =>
                enemy.PlayerInSight ? alertSightColor : alertSearchColor,
            // Combat isn't reachable yet; when it lands it reads as full red too.
            EnemyFollowPlayer.EnemyState.Combat => alertSightColor,
            _ => patrolColor,
        };
        coneMaterial.color = Color.Lerp(coneMaterial.color, target, colorLerpSpeed * Time.deltaTime);
    }
}
