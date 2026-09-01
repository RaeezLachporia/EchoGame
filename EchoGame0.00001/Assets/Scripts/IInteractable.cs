using UnityEngine;

// Put this on anything the player can walk up to and use.
// PlayerInteractor finds these, floats a prompt over the best one, and calls Interact
// when the player presses the button.
public interface IInteractable
{
    // What the prompt says, e.g. "Read the notice". The button letter is added on top.
    string PromptText { get; }

    // Where the prompt floats in the world.
    Vector3 PromptPosition { get; }

    // False means no prompt and no press, e.g. a quest already taken.
    bool CanInteract { get; }

    // Returns true if the interaction actually happened.
    bool Interact(GameObject player);
}
