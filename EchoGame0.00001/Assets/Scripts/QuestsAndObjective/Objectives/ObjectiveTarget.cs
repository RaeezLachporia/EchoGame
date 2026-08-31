using System.Collections.Generic;
using UnityEngine;

// Put this on an enemy to say which objective group it belongs to.
// An objective with a matching Target Group will count this enemy.
//
// You only need it when an objective cares about specific enemies. An enemy without
// one still counts toward any objective that left Target Group empty.
public class ObjectiveTarget : MonoBehaviour
{
    // A list of every live target, so counting them doesn't have to search the whole
    // scene each time an enemy dies. Same idea as Comapnion.Active.
    private static readonly List<ObjectiveTarget> active = new List<ObjectiveTarget>();
    public static IReadOnlyList<ObjectiveTarget> Active => active;

    [Header("Objective")]
    [Tooltip("Which group this enemy belongs to, e.g. \"warehouse-guards\" or \"captain\". An objective whose Target Group matches counts this enemy. A KILL-THE-CAPTAIN objective is just a Clear Group on an id only the captain carries.")]
    public string groupId;

    // OnEnable and OnDisable, not Start and OnDestroy. A pooled enemy gets switched
    // back on rather than remade, so it has to sign up again each life. Same reason
    // EnemyHealth resets its health in OnEnable.
    void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    void OnDisable()
    {
        active.Remove(this);
    }

    // Counts how many live enemies match this objective.
    //
    // ignore is there for the death event. EnemyHealth fires it before the enemy gets
    // switched off, so the dying one is still in the list. Skip it, or the count comes
    // out one too high and the last kill never finishes the objective.
    public static int CountMatching(ObjectiveDefinition objective, ObjectiveTarget ignore = null)
    {
        if (objective == null) return 0;

        int count = 0;
        for (int i = 0; i < active.Count; i++)
        {
            ObjectiveTarget target = active[i];
            if (target == null || target == ignore) continue;
            if (objective.Matches(target.groupId)) count++;
        }
        return count;
    }
}
