using System.Collections.Generic;
using UnityEngine;

// The one thing in the level that knows what the player is meant to be doing.
// It watches enemies die, ticks off objectives, moves quests along, and fires events
// for QuestHud to show. Put ONE of these in the scene.
//
// Progress is kept here, not on the quest and objective assets. Writing to a
// ScriptableObject while playing edits the actual asset file, and in the editor that
// change sticks around after you stop, so a quest would start already half done. The
// assets stay as read-only data, and anything that changes lives in the QuestProgress
// and ObjectiveProgress classes below.
public class QuestTracker : MonoBehaviour
{
    [Header("Quests")]
    [Tooltip("Every quest in the game. Quests ticked Start On Load begin automatically, and this is what StartQuest(id) looks names up in. Drag your QuestDatabase asset here.")]
    [SerializeField] private QuestDatabase database;
    [Tooltip("Extra quests to start in THIS level only, on top of anything already marked Start On Load. Handy for testing one quest without editing its asset.")]
    [SerializeField] private List<QuestDefinition> questsToStart = new List<QuestDefinition>();

    [Header("Debug")]
    [Tooltip("Log every objective activation, tick and completion. Leave this on while building levels — it's the fastest way to see why an objective isn't counting.")]
    [SerializeField] private bool logProgress = true;

    // The live copy of one objective. A class, not a struct, because the HUD gets
    // handed these and we keep changing them afterwards.
    public class ObjectiveProgress
    {
        public ObjectiveDefinition definition;
        public ObjectiveState state = ObjectiveState.Inactive;
        // Kill Count: kills so far, counting up to required.
        // Clear Group: enemies still alive, counting down to zero.
        public int current;
        // Kill Count: kills needed. Clear Group: how many were alive at the start.
        public int required;
    }

    public class QuestProgress
    {
        public QuestDefinition definition;
        public QuestState state = QuestState.Inactive;
        public readonly List<ObjectiveProgress> objectives = new List<ObjectiveProgress>();
    }

    private readonly List<QuestProgress> quests = new List<QuestProgress>();
    // What the HUD reads to draw the list. Read-only so nothing can bypass StartQuest
    // by adding to it directly.
    public IReadOnlyList<QuestProgress> Quests => quests;

    // The HUD listens to these instead of checking every frame.
    public event System.Action<QuestProgress> QuestStarted;
    public event System.Action<QuestProgress> QuestCompleted;
    public event System.Action<ObjectiveProgress> ObjectiveActivated;
    public event System.Action<ObjectiveProgress> ObjectiveProgressed;
    public event System.Action<ObjectiveProgress> ObjectiveCompleted;

    void OnEnable()
    {
        EnemyHealth.AnyDied += OnEnemyDied;
    }

    void OnDisable()
    {
        // Always unsubscribe. AnyDied is static, so a tracker that stays signed up
        // would keep counting kills for a scene that has already gone.
        EnemyHealth.AnyDied -= OnEnemyDied;
    }

    void Start()
    {
        if (database == null && questsToStart.Count == 0)
        {
            Debug.LogWarning($"[QuestTracker] '{name}' has no Database and nothing in Quests To Start, so no quest will ever begin and the HUD stays blank. Drag your QuestDatabase asset into the Database field.", this);
            return;
        }

        // Database first so Start On Load quests come up in list order, then whatever
        // this scene adds on top.
        if (database != null)
        {
            for (int i = 0; i < database.allQuests.Count; i++)
            {
                QuestDefinition quest = database.allQuests[i];
                if (quest != null && quest.startOnLoad) StartQuest(quest);
            }
        }

        for (int i = 0; i < questsToStart.Count; i++)
            StartQuest(questsToStart[i]);
    }

    // ---------------------------------------------------------------- public API

    // Starts a quest by its id, looked up in the Database.
    // For cutscenes, triggers, and dialogue choices.
    public bool StartQuest(string questId)
    {
        if (database == null)
        {
            Debug.LogWarning($"[QuestTracker] StartQuest(\"{questId}\") needs the Database field wired to look the quest up. Drag your QuestDatabase asset into it.", this);
            return false;
        }

        QuestDefinition quest = database.FindById(questId);
        if (quest == null)
        {
            Debug.LogWarning($"[QuestTracker] StartQuest(\"{questId}\") found no quest with that id in '{database.name}'. Check the Id field on the quest asset, and that the quest is in the database's list.", this);
            return false;
        }
        return StartQuest(quest);
    }

    public bool StartQuest(QuestDefinition quest)
    {
        if (quest == null) return false;

        // Already running or already done. Starting it again would wipe the player's
        // progress, so don't.
        if (FindQuest(quest) != null) return false;

        QuestProgress progress = new QuestProgress { definition = quest, state = QuestState.Active };
        for (int i = 0; i < quest.objectives.Count; i++)
        {
            ObjectiveDefinition objective = quest.objectives[i];
            // An empty row left in the inspector list. Skip it and carry on.
            if (objective == null) continue;
            progress.objectives.Add(new ObjectiveProgress { definition = objective });
        }

        if (progress.objectives.Count == 0)
            Debug.LogWarning($"[QuestTracker] Quest '{quest.name}' has no objectives, so it completes the moment it starts. Add ObjectiveDefinition assets to its Objectives list.", this);

        quests.Add(progress);
        if (logProgress)
            Debug.Log($"[QuestTracker] Quest STARTED: \"{quest.title}\" — {progress.objectives.Count} objectives, {quest.flow}.", this);
        QuestStarted?.Invoke(progress);

        ActivateNextObjectives(progress);
        // Catches an empty quest, and one whose objectives were already done.
        TryCompleteQuest(progress);
        return true;
    }

    // Adds progress to a counting objective by its id.
    //
    // This is how other objective types get added later. A trigger volume or an
    // interact prompt just calls this, and nothing in here has to change.
    public bool ReportProgress(string objectiveId, int amount = 1)
    {
        ObjectiveProgress objective = FindObjective(objectiveId, out QuestProgress quest);
        if (objective == null)
        {
            Debug.LogWarning($"[QuestTracker] ReportProgress(\"{objectiveId}\") found no objective with that id in any running quest. Check the Id field on the objective asset, and that its quest has actually started.", this);
            return false;
        }
        if (objective.state != ObjectiveState.Active) return false;

        // Clear Group counts down from however many enemies were there, so adding to
        // it makes no sense. The enemies decide when it's done.
        if (objective.definition.kind == ObjectiveKind.ClearGroup)
        {
            Debug.LogWarning($"[QuestTracker] ReportProgress can't drive '{objective.definition.name}' — it's a CLEAR GROUP objective, which finishes when its enemies are dead. Use CompleteObjective(\"{objectiveId}\") if you need to force it.", this);
            return false;
        }

        objective.current += Mathf.Max(1, amount);
        if (logProgress)
            Debug.Log($"[QuestTracker] Objective PROGRESS: \"{objective.definition.title}\" — {objective.current} / {objective.required}.", this);
        ObjectiveProgressed?.Invoke(objective);

        if (objective.current >= objective.required) MarkComplete(objective);

        ActivateNextObjectives(quest);
        TryCompleteQuest(quest);
        return true;
    }

    // Finishes an objective straight away, whatever kind it is.
    // For scripted moments, and for skipping ahead while testing.
    public bool CompleteObjective(string objectiveId)
    {
        ObjectiveProgress objective = FindObjective(objectiveId, out QuestProgress quest);
        if (objective == null)
        {
            Debug.LogWarning($"[QuestTracker] CompleteObjective(\"{objectiveId}\") found no objective with that id in any running quest. Check the Id field on the objective asset, and that its quest has actually started.", this);
            return false;
        }
        if (objective.state == ObjectiveState.Complete) return false;

        // Set the counter to finished too, so the HUD's last look at this line shows
        // 5 / 5 instead of whatever it was stuck on.
        objective.current = objective.definition.kind == ObjectiveKind.ClearGroup ? 0 : objective.required;
        MarkComplete(objective);

        ActivateNextObjectives(quest);
        TryCompleteQuest(quest);
        return true;
    }

    public bool IsQuestComplete(string questId)
    {
        for (int i = 0; i < quests.Count; i++)
            if (quests[i].definition != null && quests[i].definition.id == questId)
                return quests[i].state == QuestState.Complete;
        return false;
    }

    // ---------------------------------------------------------------- kill tracking

    // An enemy died. Tick every active objective that cares, then move the quests on.
    // This runs off EnemyHealth's death event, so it picks up kills from the player,
    // from companions, and from anything added later without touching any of them.
    private void OnEnemyDied(EnemyHealth enemy)
    {
        if (enemy == null) return;

        // Looked up each time instead of cached. It's a different enemy every call,
        // and ObjectiveTarget can be added to an enemy at any point.
        ObjectiveTarget target = enemy.GetComponent<ObjectiveTarget>();
        string groupId = target != null ? target.groupId : null;

        // Remember the count first. Finishing a quest can start its Next Quest, which
        // adds to this list while we're in the loop, and a quest that only just started
        // shouldn't get ticked by this same kill.
        int questCount = quests.Count;
        for (int q = 0; q < questCount; q++)
        {
            QuestProgress quest = quests[q];

            // Finished quests are not skipped on purpose. One can still be holding
            // optional objectives the player never got round to, and those should keep
            // counting. The Active check below is what actually filters.
            bool changed = false;
            for (int o = 0; o < quest.objectives.Count; o++)
            {
                ObjectiveProgress objective = quest.objectives[o];
                if (objective.state != ObjectiveState.Active) continue;
                if (TickObjective(objective, groupId, target)) changed = true;
            }

            if (!changed) continue;

            ActivateNextObjectives(quest);
            TryCompleteQuest(quest);
        }
    }

    // Returns true if this objective's numbers actually moved.
    private bool TickObjective(ObjectiveProgress objective, string groupId, ObjectiveTarget dying)
    {
        ObjectiveDefinition definition = objective.definition;

        if (definition.kind == ObjectiveKind.ClearGroup)
        {
            // Count who's left, not who died. That way pooled enemies can't break it:
            // one that dies, respawns and dies again fires the event twice, which is
            // right for Kill Count but would be wrong here.
            //
            // The dying enemy is skipped because the event fires before it's switched
            // off, so it's still in the list. See ObjectiveTarget.CountMatching.
            int remaining = ObjectiveTarget.CountMatching(definition, dying);
            if (remaining == objective.current) return false;

            objective.current = remaining;
            if (logProgress)
                Debug.Log($"[QuestTracker] Objective PROGRESS: \"{definition.title}\" — {remaining} left.", this);
            ObjectiveProgressed?.Invoke(objective);

            if (remaining <= 0) MarkComplete(objective);
            return true;
        }

        // Kill Count: the death itself is the progress.
        if (!definition.Matches(groupId)) return false;

        objective.current++;
        if (logProgress)
            Debug.Log($"[QuestTracker] Objective PROGRESS: \"{definition.title}\" — {objective.current} / {objective.required}.", this);
        ObjectiveProgressed?.Invoke(objective);

        if (objective.current >= objective.required) MarkComplete(objective);
        return true;
    }

    // ---------------------------------------------------------------- advancing

    // Sequential gives out one objective at a time. All At Once turns on everything
    // that's still waiting.
    private void ActivateNextObjectives(QuestProgress quest)
    {
        bool sequential = quest.definition.flow == ObjectiveFlow.Sequential;

        // Don't start anything new while a required objective is still running.
        // Without this check, every bit of progress would fall through to the loop
        // below and turn on the next objective too early.
        //
        // Only non-optional objectives block. An optional one is a side objective, so
        // the main chain carries on past it.
        if (sequential)
        {
            for (int i = 0; i < quest.objectives.Count; i++)
            {
                ObjectiveProgress running = quest.objectives[i];
                if (running.state == ObjectiveState.Active && !running.definition.optional) return;
            }
        }

        for (int i = 0; i < quest.objectives.Count; i++)
        {
            ObjectiveProgress objective = quest.objectives[i];
            if (objective.state != ObjectiveState.Inactive) continue;

            Activate(objective);

            // Only stop on a required objective that's actually still running. One that
            // finished the moment it turned on, and optional ones, have to fall through
            // or they'd block the rest of the quest.
            if (sequential
                && objective.state == ObjectiveState.Active
                && !objective.definition.optional)
                return;
        }
    }

    // Turns an objective on and works out what finishing it means.
    private void Activate(ObjectiveProgress objective)
    {
        ObjectiveDefinition definition = objective.definition;
        objective.state = ObjectiveState.Active;

        bool clearedOnArrival = false;

        if (definition.kind == ObjectiveKind.ClearGroup)
        {
            // Clear Group can only see enemies with an ObjectiveTarget, so an empty
            // Target Group here quietly means "any enemy that happens to have one".
            if (string.IsNullOrWhiteSpace(definition.targetGroup))
                Debug.LogWarning($"[QuestTracker] Objective '{definition.name}' is CLEAR GROUP with an EMPTY Target Group. Clear Group can only see enemies carrying an ObjectiveTarget component, so this will only clear the ones that happen to have one. Fill in Target Group, or switch Kind to Kill Count.", this);

            // Count how many are there right now. That's what lets "clear this room"
            // keep working when you add or remove enemies, instead of a typed-in
            // number going out of date.
            objective.required = ObjectiveTarget.CountMatching(definition);
            objective.current = objective.required;
            clearedOnArrival = objective.required == 0;

            if (clearedOnArrival)
                Debug.LogWarning($"[QuestTracker] Objective '{definition.name}' is CLEAR GROUP for Target Group \"{definition.targetGroup}\", but NO enemy in the level has an ObjectiveTarget with that Group Id. Completing it so the quest isn't stuck — check the Group Id spelling on the enemies.", this);
        }
        else
        {
            objective.required = Mathf.Max(1, definition.requiredCount);
            objective.current = 0;
        }

        if (logProgress)
            Debug.Log($"[QuestTracker] Objective ACTIVE: \"{definition.title}\" — {Describe(objective)}.", this);
        ObjectiveActivated?.Invoke(objective);

        // Done after the Activated event so listeners always see an objective show up
        // before they see it finish, even if that's the same frame.
        if (clearedOnArrival) MarkComplete(objective);
    }

    private void MarkComplete(ObjectiveProgress objective)
    {
        if (objective.state == ObjectiveState.Complete) return;

        objective.state = ObjectiveState.Complete;
        if (logProgress)
            Debug.Log($"[QuestTracker] Objective COMPLETE: \"{objective.definition.title}\".", this);
        ObjectiveCompleted?.Invoke(objective);
    }

    private void TryCompleteQuest(QuestProgress quest)
    {
        if (quest.state != QuestState.Active) return;

        for (int i = 0; i < quest.objectives.Count; i++)
        {
            ObjectiveProgress objective = quest.objectives[i];
            // Optional objectives never hold a quest up. They stay active afterwards so
            // the player can still go back and finish them.
            if (objective.definition.optional) continue;
            if (objective.state != ObjectiveState.Complete) return;
        }

        quest.state = QuestState.Complete;
        if (logProgress)
            Debug.Log($"[QuestTracker] Quest COMPLETE: \"{quest.definition.title}\".", this);
        QuestCompleted?.Invoke(quest);

        // Quest chains are set up in the assets, so no code needs to know the order.
        if (quest.definition.nextQuest != null) StartQuest(quest.definition.nextQuest);
    }

    // ---------------------------------------------------------------- lookups

    private QuestProgress FindQuest(QuestDefinition definition)
    {
        for (int i = 0; i < quests.Count; i++)
            if (quests[i].definition == definition) return quests[i];
        return null;
    }

    private ObjectiveProgress FindObjective(string objectiveId, out QuestProgress owner)
    {
        for (int q = 0; q < quests.Count; q++)
        {
            QuestProgress quest = quests[q];
            for (int o = 0; o < quest.objectives.Count; o++)
            {
                ObjectiveProgress objective = quest.objectives[o];
                if (objective.definition != null && objective.definition.id == objectiveId)
                {
                    owner = quest;
                    return objective;
                }
            }
        }
        owner = null;
        return null;
    }

    // Progress as text, only used for the log line. The HUD has its own formats.
    private static string Describe(ObjectiveProgress objective)
    {
        return objective.definition.kind == ObjectiveKind.ClearGroup
            ? $"{objective.current} left to clear"
            : $"{objective.current} / {objective.required} kills";
    }
}
