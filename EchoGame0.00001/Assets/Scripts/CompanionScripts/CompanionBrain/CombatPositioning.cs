using UnityEngine;
using UnityEngine.AI;

// Where a companion should stand, and whether she can actually get there.
// Pure functions, no state — the brain owns the decision, this owns the geometry.
public static class CombatPositioning
{
    // Hold the line: out in front of the player, on the side the fight is coming
    // from. The fan angle exists so several brained companions don't all compute
    // the identical point and shove each other off it.
    public static Vector3 Anchor(Vector3 playerPosition, Vector3 playerForward, Vector3 threatCentroid, bool anyEngaged, CombatProfile profile)
    {
        Vector3 direction = anyEngaged ? threatCentroid - playerPosition : playerForward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = playerForward;
        direction.Normalize();

        if (Mathf.Abs(profile.anchorFanAngle) > 0.01f && profile.anchorFanSign != 0)
            direction = Quaternion.Euler(0f, profile.anchorFanAngle * profile.anchorFanSign, 0f) * direction;

        return playerPosition + direction * profile.anchorDistance;
    }

    // Stand on the line between an enemy and whoever it's about to hit, on the
    // VICTIM's side of the gap — she's a shield, not a duellist trading blows out
    // in front. Returns false when the enemy is already close enough that there's
    // no room to get between them; the brain falls through to attacking instead.
    public static bool TryIntercept(Vector3 enemyPosition, Vector3 victimPosition, float enemyReach, CombatProfile profile, out Vector3 point)
    {
        point = victimPosition;

        Vector3 toVictim = victimPosition - enemyPosition;
        toVictim.y = 0f;
        float gap = toVictim.magnitude;
        if (gap < 0.001f) return false;
        toVictim /= gap;

        // No room to interpose without standing inside the person she's covering.
        if (gap < enemyReach + profile.minPlayerClearance) return false;

        float standOff = Mathf.Min(enemyReach + profile.blockPadding, gap - profile.minPlayerClearance);
        point = enemyPosition + toVictim * standOff;
        point.y = victimPosition.y;
        return true;
    }

    // Pull to the edge of the fight, never out of it: directly away from the
    // threats, but still tethered to the player. She backs off facing the fight.
    public static Vector3 SelfPreserveEdge(Vector3 selfPosition, Vector3 playerPosition, Vector3 threatCentroid, CombatProfile profile)
    {
        Vector3 away = selfPosition - threatCentroid;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = playerPosition - threatCentroid;
            away.y = 0f;
        }
        if (away.sqrMagnitude < 0.0001f) return playerPosition;
        away.Normalize();

        Vector3 candidate = playerPosition + away * profile.selfPreserveRadius;

        // Never let "retreat" become "leave" — clamp back inside the leash.
        Vector3 fromPlayer = candidate - playerPosition;
        fromPlayer.y = 0f;
        if (fromPlayer.magnitude > profile.leashRadius)
            candidate = playerPosition + fromPlayer.normalized * profile.leashRadius;

        return candidate;
    }

    // Snap a computed point onto the navmesh AND prove this agent can walk there.
    //
    // SamplePosition alone is not enough: two agent types are baked in this
    // project (companions are type 0, enemies have their own), and SamplePosition
    // is type-agnostic — it will happily hand back a point that only exists on the
    // enemy surface. SetDestination then returns a partial path and the companion
    // walks to the nearest edge and stands there looking broken. Anchor and
    // intercept points are computed from ENEMY positions, so they're exactly the
    // case that hits this. CalculatePath is the instance method and honours the
    // agent's own type and area mask.
    //
    // Falls back toward the player rather than failing outright — an agent left
    // pathless while the brain holds the agent is a companion frozen in place with
    // follow already stood down.
    public static bool TryResolve(NavMeshAgent agent, Vector3 candidate, Vector3 fallback, NavMeshPath pathBuffer, float sampleRadius, out Vector3 result)
    {
        if (TryPoint(agent, candidate, pathBuffer, sampleRadius, out result)) return true;
        if (TryPoint(agent, Vector3.Lerp(candidate, fallback, 0.5f), pathBuffer, sampleRadius, out result)) return true;
        if (TryPoint(agent, fallback, pathBuffer, sampleRadius, out result)) return true;

        result = agent.transform.position;
        return false;
    }

    private static bool TryPoint(NavMeshAgent agent, Vector3 candidate, NavMeshPath pathBuffer, float sampleRadius, out Vector3 result)
    {
        result = candidate;
        if (agent == null || !agent.isOnNavMesh) return false;
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas)) return false;
        if (!agent.CalculatePath(hit.position, pathBuffer)) return false;
        if (pathBuffer.status != NavMeshPathStatus.PathComplete) return false;

        result = hit.position;
        return true;
    }
}
