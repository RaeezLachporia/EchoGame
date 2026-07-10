using System.Collections.Generic;
using UnityEngine;

// The list of every companion in the game.
// The companion select screen will read this list to know what to show.
// When you create a new companion asset, add it to this list.
[CreateAssetMenu(fileName = "CompanionDatabase", menuName = "EchoGame/Companion Database")]
public class CompanionDatabase : ScriptableObject
{
    [Tooltip("Every companion the player can pick. Add new companions here.")]
    public List<CompanionDefinition> allCompanions = new List<CompanionDefinition>();

    // Finds a companion by its id, e.g. "layla". Used later when loading a save.
    public CompanionDefinition FindById(string id)
    {
        foreach (CompanionDefinition def in allCompanions)
        {
            if (def != null && def.id == id)
                return def;
        }
        return null;
    }
}
