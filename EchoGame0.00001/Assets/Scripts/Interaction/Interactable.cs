using System.Collections.Generic;
using UnityEngine;

// Base for anything the player can interact with. Put a subclass of this on the
// object, like QuestGiver, rather than this script itself.
public abstract class Interactable : MonoBehaviour, IInteractable
{
    // Every live interactable, so PlayerInteractor doesn't search the scene each
    // frame. Same registry as Comapnion.Active and ObjectiveTarget.Active.
    private static readonly List<Interactable> active = new List<Interactable>();
    public static IReadOnlyList<Interactable> Active => active;

    [Header("Prompt")]
    [Tooltip("What the floating prompt says, e.g. \"Read the notice\". The button letter is added by the prompt itself, so don't type it here.")]
    [SerializeField] private string promptText = "Interact";
    [Tooltip("Where the prompt floats. Leave empty to use this object with Prompt Height added on top.")]
    [SerializeField] private Transform promptAnchor;
    [Tooltip("How far above this object the prompt sits when there's no Prompt Anchor. Raise it for tall objects.")]
    [SerializeField] private float promptHeight = 2f;

    [Header("Range")]
    [Tooltip("How close the player has to be. Shown as a yellow wire sphere when this object is selected in the scene view.")]
    [SerializeField] private float interactRange = 3f;
    [Tooltip("How near the middle of the view this has to be, in degrees off where the camera points. Bigger is more forgiving. 35 reads as 'looking at it'.")]
    [SerializeField, Range(1f, 90f)] private float lookAngle = 35f;

    public virtual string PromptText => promptText;
    public virtual bool CanInteract => true;

    public Vector3 PromptPosition => promptAnchor != null
        ? promptAnchor.position
        : transform.position + Vector3.up * promptHeight;

    public float InteractRange => interactRange;
    public float LookAngle => lookAngle;

    // Virtual so a subclass that needs its own OnEnable can override and call base.
    // Declaring a second OnEnable in a subclass instead would stop this one running
    // and the object would never register.
    protected virtual void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    protected virtual void OnDisable()
    {
        active.Remove(this);
    }

    public bool Interact(GameObject player)
    {
        if (!CanInteract) return false;
        return OnInteract(player);
    }

    // What this interactable actually does. Return true if it happened.
    protected abstract bool OnInteract(GameObject player);

    // There's no trigger collider to see in the scene view, so draw the range.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
        Gizmos.DrawWireCube(PromptPosition, Vector3.one * 0.15f);
    }
}
