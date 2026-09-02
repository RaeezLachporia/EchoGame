using UnityEngine;

// Put this on an object to make it hand out a quest when the player interacts with it.
// A notice board, a radio, an NPC.
public class QuestGiver : Interactable
{
    [Header("Quest")]
    [Tooltip("The quest this object gives out. Untick Start On Load on that quest asset, or it will already be running before the player ever gets here.")]
    [SerializeField] private QuestDefinition quest;
    [Tooltip("The tracker that runs the quest. Auto-found in the scene if left empty.")]
    [SerializeField] private QuestTracker tracker;

    [Header("Prompt Text")]
    [Tooltip("Tick to build the prompt from the quest's own Title and Description. Untick to use the plain Prompt Text field above instead.")]
    [SerializeField] private bool useQuestText = true;
    [Tooltip("Tick to put the quest's Description under its Title. Untick for just the title on one line.")]
    [SerializeField] private bool includeDescription = true;
    [Tooltip("Wrapped around the quest title. Needs Rich Text ticked on the prompt's TMP text, or the tags show up as letters. {0} = the title.")]
    [SerializeField] private string titleFormat = "<b>{0}</b>";

    // Built once in Awake rather than in the property. PlayerInteractor reads
    // PromptText every frame, so building the string there would allocate a new one
    // 60 times a second for text that never changes.
    private string questPrompt;

    public override string PromptText => useQuestText && questPrompt != null
        ? questPrompt
        : base.PromptText;

    // Goes quiet once the quest has been taken. StartQuest already refuses to start
    // the same quest twice, this is what makes that refusal visible instead of a
    // prompt that does nothing when you press it.
    public override bool CanInteract => quest != null && tracker != null && !tracker.HasQuest(quest);

    void Awake()
    {
        if (tracker == null) tracker = FindObjectOfType<QuestTracker>();

        // Claim the quest before the tracker's Start runs, so it doesn't hand it out
        // at load and leave this object with nothing to give.
        if (tracker != null && quest != null) tracker.ReserveForGiver(quest);

        questPrompt = BuildQuestPrompt();
    }

    private string BuildQuestPrompt()
    {
        if (quest == null) return null;

        // No title filled in on the asset yet — fall back to the Prompt Text field so
        // the prompt reads as something rather than as an empty line.
        if (string.IsNullOrWhiteSpace(quest.title)) return null;

        string title = string.IsNullOrEmpty(titleFormat)
            ? quest.title
            : string.Format(titleFormat, quest.title);

        if (!includeDescription || string.IsNullOrWhiteSpace(quest.description))
            return title;

        return title + "\n" + quest.description.Trim();
    }

    void Start()
    {
        if (quest == null)
            Debug.LogWarning($"[QuestGiver] '{name}' has no Quest assigned, so it will never show a prompt. Drag a QuestDefinition asset into the Quest field.", this);
        if (tracker == null)
            Debug.LogWarning($"[QuestGiver] '{name}' found no QuestTracker in the scene. Add a QuestTracker to a scene object, or drag it into this object's Tracker field.", this);

        WarnIfQuestStartsByItself();
    }

    // The quest is also set to start on its own. Awake already claimed it so the
    // giver still works, but the settings contradict each other and one of them is
    // going to confuse somebody later.
    private void WarnIfQuestStartsByItself()
    {
        if (quest == null || tracker == null) return;

        bool inQuestsToStart = false;
        for (int i = 0; i < tracker.QuestsToStart.Count; i++)
            if (tracker.QuestsToStart[i] == quest) { inQuestsToStart = true; break; }

        if (!quest.startOnLoad && !inQuestsToStart) return;

        string cause = quest.startOnLoad && inQuestsToStart
            ? $"Start On Load is ticked on '{quest.name}' AND it's in the tracker's Quests To Start list"
            : quest.startOnLoad
                ? $"Start On Load is ticked on '{quest.name}'"
                : $"'{quest.name}' is in the tracker's Quests To Start list";

        Debug.LogWarning($"[QuestGiver] '{name}' hands out '{quest.name}', but that quest is also set to start on its own — {cause}. The giver wins, so it still works, but clear that setting so the two don't disagree.", this);
    }

    protected override bool OnInteract(GameObject player)
    {
        return tracker.StartQuest(quest);
    }
}
