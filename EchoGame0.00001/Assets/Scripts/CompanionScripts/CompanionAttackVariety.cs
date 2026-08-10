using UnityEngine;

// Layla-only right now: alternates her attack between two states in her animator
// by flipping a bool before each swing. The animator's Any/Motion transitions
// route the Attack trigger into CompanionAttackPunch or CompanionAttackKick
// depending on this bool, so the actual "which clip plays" decision lives in the
// controller — this component just picks.
//
// Toggles on the TRAILING edge of a swing rather than the leading one so the
// bool is set well before the animator picks up the next Attack trigger.
// Set the parameter's initial value in the controller (default false) and the
// first swing plays the "false" branch; each subsequent swing flips it.
[RequireComponent(typeof(CompanionCommand))]
[RequireComponent(typeof(Animator))]
public class CompanionAttackVariety : MonoBehaviour
{
    [Header("Variety")]
    [Tooltip("Bool parameter on the Animator that routes the Attack trigger to a variant. This component flips it after every swing.")]
    [SerializeField] private string toggleParameter = "UseKick";

    [Header("Debug")]
    [SerializeField] private bool logSwaps = false;

    private CompanionCommand command;
    private Animator animator;
    private int toggleHash;
    private bool current;
    private bool wasAttacking;

    void Awake()
    {
        command = GetComponent<CompanionCommand>();
        animator = GetComponent<Animator>();
        toggleHash = Animator.StringToHash(toggleParameter);
    }

    void Start()
    {
        if (!HasBool(animator, toggleParameter))
        {
            Debug.LogWarning($"[CompanionAttackVariety] Animator on '{name}' has no bool parameter '{toggleParameter}' — attack variety disabled. Add it to the controller or remove this component.", this);
            enabled = false;
            return;
        }

        current = animator.GetBool(toggleHash);
    }

    void Update()
    {
        bool attacking = command.IsAttacking;

        // Trailing edge: the swing that just ended used the CURRENT value; queue
        // the opposite for the next one.
        if (!attacking && wasAttacking)
        {
            current = !current;
            animator.SetBool(toggleHash, current);
            if (logSwaps)
                Debug.Log($"[CompanionAttackVariety] {name} next swing '{toggleParameter}' -> {current}", this);
        }

        wasAttacking = attacking;
    }

    private static bool HasBool(Animator a, string name)
    {
        AnimatorControllerParameter[] parameters = a.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].type == AnimatorControllerParameterType.Bool && parameters[i].name == name)
                return true;
        return false;
    }
}
