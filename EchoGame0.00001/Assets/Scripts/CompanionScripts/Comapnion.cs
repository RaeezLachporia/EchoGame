using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Comapnion : MonoBehaviour, IDamageable, IHealable
{
    // Every live companion, maintained here so nothing has to sweep the scene to
    // find teammates. Enemies poll this while deciding who to swing at, and the
    // combat brain scores peel targets off it — both on a timer, both often
    // enough that a FindObjectsOfType per caller would allocate constantly.
    private static readonly List<Comapnion> active = new List<Comapnion>();
    public static IReadOnlyList<Comapnion> Active => active;

    void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    void OnDisable()
    {
        active.Remove(this);
    }

    [Header("Identity")]
    [Tooltip("Drag a companion asset here (e.g. Layla). Its name and health replace the values below. Leave empty to use the values below instead.")]
    [SerializeField] private CompanionDefinition definition;
    [SerializeField] private string displayName = "Companion";
    [SerializeField] private string playerTag = "Player";

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;
    [Tooltip("Log every hit this companion takes, showing whether a resistance buff reduced it.")]
    [SerializeField] private bool logDamage = true;

    [Header("Push Response")]
    [Tooltip("Scales player impact speed into companion push speed. 0.3 = a 6 m/s sprint pushes the companion at ~1.8 m/s.")]
    [SerializeField] private float pushScale = 0.35f;
    [Tooltip("How quickly the push decays once the player stops bashing (higher = settles sooner).")]
    [SerializeField] private float pushDamping = 6f;
    [Tooltip("How fast sustained contact blends the push toward the player's current impact speed.")]
    [SerializeField] private float sustainResponse = 10f;
    [Tooltip("Cap on push speed in m/s — keeps a sprint-into-companion from launching them.")]
    [SerializeField] private float maxPushSpeed = 4f;

    private int hudSlot = -1;
    private Rigidbody rb;
    private NavMeshAgent agent;
    private Vector3 pushVelocity;

    void Awake()
    {
        if (definition != null)
            ApplyDefinition(definition);

        // Companion movement is driven by NavMeshAgent (BasicPlayerFollowScript).
        // A dynamic Rigidbody fights the agent — collisions impart velocity, the agent
        // loses its path, and the companion slides off-mesh. Kinematic lets physics
        // events still fire while leaving position fully under the agent's control;
        // any "push back" from a bump is applied via NavMeshAgent.Move so it stays
        // on-mesh and never builds runaway momentum.
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        if (CompanionHealthHud.Instance != null)
            hudSlot = CompanionHealthHud.Instance.Claim(this, displayName, maxHealth, currentHealth, definition);
    }

    // The spawner (coming later) calls this right after creating a companion,
    // so it starts with the right name and health from its definition asset.
    public void Initialize(CompanionDefinition def)
    {
        definition = def;
        ApplyDefinition(def);
    }

    private void ApplyDefinition(CompanionDefinition def)
    {
        displayName = def.displayName;
        maxHealth = def.maxHealth;
        currentHealth = maxHealth;
    }

    void OnValidate()
    {
        // Runs in the editor whenever this component changes in the Inspector.
        // Copies the definition's name and health into the fields above so you
        // can see them straight away. If the game is running, it also updates
        // the health bar HUD so you don't have to restart.
        if (definition == null) return;
        ApplyDefinition(definition);
        if (Application.isPlaying && CompanionHealthHud.Instance != null)
            hudSlot = CompanionHealthHud.Instance.Claim(this, displayName, maxHealth, currentHealth, definition);
    }

    void Update()
    {
        if (agent == null || pushVelocity.sqrMagnitude < 0.0025f)
        {
            pushVelocity = Vector3.zero;
            return;
        }

        // Apply the push via the agent so it slides along the navmesh, not through it.
        agent.Move(pushVelocity * Time.deltaTime);
        // Exponential decay — the step-back eases out instead of stopping dead.
        pushVelocity *= Mathf.Exp(-pushDamping * Time.deltaTime);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!collision.collider.CompareTag(playerTag)) return;
        pushVelocity = ComputePush(collision);
        Interact();
    }

    void OnCollisionStay(Collision collision)
    {
        if (!collision.collider.CompareTag(playerTag)) return;
        // Lean into the companion and they keep getting nudged proportional to your speed.
        Vector3 desired = ComputePush(collision);
        pushVelocity = Vector3.Lerp(pushVelocity, desired, sustainResponse * Time.fixedDeltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
            Interact();
    }

    private Vector3 ComputePush(Collision collision)
    {
        Vector3 away = transform.position - collision.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) return Vector3.zero;
        away.Normalize();

        float impactSpeed = collision.relativeVelocity.magnitude;
        Vector3 push = away * (impactSpeed * pushScale);
        return Vector3.ClampMagnitude(push, maxPushSpeed);
    }

    private void Interact()
    {
        // TODO: hook up dialogue / give item / follow toggle here.
        Debug.Log($"{displayName} interacted with player");
    }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    // The name shown on the HUD bar and the floating panel. Reads the definition's
    // name once one is applied, and the inspector fallback until then.
    public string DisplayName => displayName;
    // The command wheel reads this to put the companion's portrait on its slice.
    public CompanionDefinition Definition => definition;

    public void TakeDamage(float damage)
    {
        // A damage-resistance buff (Zara's) scales the hit down before it lands.
        // Done here rather than at the attacker so every damage source is covered.
        float incoming = damage;
        AllyBuff buff = GetComponent<AllyBuff>();
        if (buff != null && buff.DamageMultiplier < 1f)
        {
            incoming = damage * buff.DamageMultiplier;
            if (logDamage)
                Debug.Log($"[Comapnion] {displayName} BUFFED hit: {damage} x{buff.DamageMultiplier:F2} = {incoming} damage ({buff.SecondsRemaining:F1}s of buff left).", this);
        }
        else if (logDamage)
        {
            Debug.Log($"[Comapnion] {displayName} normal hit: {incoming} damage.", this);
        }

        currentHealth = Mathf.Max(0f, currentHealth - incoming);
        if (CompanionHealthHud.Instance != null)
            CompanionHealthHud.Instance.SetHealth(hudSlot, currentHealth);

        if (currentHealth <= 0f)
            Destroy(gameObject);
    }

    // Temporary max-health boost (Layla's taunt). The bonus is granted as real
    // health too, so it's usable the moment it lands rather than being empty
    // headroom she has to be healed into.
    public void ApplyMaxHealthBonus(float bonus)
    {
        if (bonus <= 0f) return;
        maxHealth += bonus;
        currentHealth += bonus;
        PushMaxHealthToHud();
    }

    // Removing clamps current health DOWN to the reduced max — losing the boost
    // should cost her the temporary hit points, not leave her over her own cap.
    public void RemoveMaxHealthBonus(float bonus)
    {
        if (bonus <= 0f) return;
        maxHealth = Mathf.Max(1f, maxHealth - bonus);
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        PushMaxHealthToHud();
    }

    private void PushMaxHealthToHud()
    {
        if (CompanionHealthHud.Instance == null) return;
        // refill: false — we manage currentHealth above; a refill would silently
        // top her up every time the boost expires.
        CompanionHealthHud.Instance.SetMaxHealth(hudSlot, maxHealth, false);
        CompanionHealthHud.Instance.SetHealth(hudSlot, currentHealth);
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        if (CompanionHealthHud.Instance != null)
            CompanionHealthHud.Instance.SetHealth(hudSlot, currentHealth);
    }

    void OnDestroy()
    {
        if (CompanionHealthHud.Instance != null)
            CompanionHealthHud.Instance.Release(hudSlot);
    }
}
