using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

// The quest list on the player's HUD. Goes on a corner panel inside PlayerUi, next to
// the health bars and the command wheel.
//
// Anchor it in the opposite corner to the command wheel. The wheel's ally and enemy
// pickers open over the middle and lower screen, and quest text under them can't be
// read.
//
// This writes one block of text rather than using fixed slots like CompanionHealthHud.
// The number of lines changes as quests come and go, so there's nothing to set up in
// the inspector ahead of time.
public class QuestHud : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The tracker driving this list. Auto-found in the scene if left empty — only set it by hand if there's more than one.")]
    [SerializeField] private QuestTracker tracker;
    [Tooltip("The TMP text this writes into. Drag the text object from inside your quest panel. Nothing shows without it.")]
    [SerializeField] private TMP_Text questText;

    [Header("Format")]
    [Tooltip("The quest header line. {0} = the quest's Title.")]
    [SerializeField] private string questTitleFormat = "<b>{0}</b>";
    [Tooltip("A KILL COUNT objective line. {0} = title, {1} = kills so far, {2} = kills needed.")]
    [SerializeField] private string killCountFormat = "   {0}   {1} / {2}";
    [Tooltip("A CLEAR GROUP objective line. {0} = title, {1} = enemies still alive.")]
    [SerializeField] private string clearGroupFormat = "   {0}   {1} left";
    [Tooltip("Wrapped around an OPTIONAL objective's line so side objectives read differently. {0} = the line. Leave empty to show them like any other.")]
    [SerializeField] private string optionalFormat = "{0}  (optional)";

    [Header("Completed Lines")]
    [Tooltip("Colour a finished objective's line turns before it drops off the list.")]
    [SerializeField] private Color completedTint = new Color(0.45f, 0.9f, 0.5f);
    [Tooltip("Seconds a finished objective stays on the list, ticked off, before disappearing. 0 removes it the instant it completes.")]
    [SerializeField, Min(0f)] private float completedHoldSeconds = 3f;
    [Tooltip("Put in front of a finished objective's line, e.g. a tick. Leave empty if your TMP font has no tick glyph — the colour alone still reads as done.")]
    [SerializeField] private string completedPrefix = "";

    // When each finished objective completed, and which ones have already had their
    // time on screen and gone. Keyed on the progress object rather than the asset, so
    // the same objective used in two quests is tracked separately.
    private readonly Dictionary<QuestTracker.ObjectiveProgress, float> completedAt =
        new Dictionary<QuestTracker.ObjectiveProgress, float>();
    private readonly HashSet<QuestTracker.ObjectiveProgress> dropped =
        new HashSet<QuestTracker.ObjectiveProgress>();

    // Reused instead of remade. The list gets redrawn on every kill, and a new
    // StringBuilder each time would make rubbish for the garbage collector to clear up
    // in the middle of a fight.
    private readonly StringBuilder builder = new StringBuilder();
    private readonly List<string> lines = new List<string>();

    void Awake()
    {
        if (tracker == null) tracker = FindObjectOfType<QuestTracker>();
    }

    // Signing up here rather than in Start. The tracker begins its quests in Start, and
    // every OnEnable runs before any Start, so no quest can start before this panel is
    // listening for it.
    void OnEnable()
    {
        if (tracker == null) return;
        tracker.QuestStarted += OnQuestChanged;
        tracker.QuestCompleted += OnQuestChanged;
        tracker.ObjectiveActivated += OnObjectiveChanged;
        tracker.ObjectiveProgressed += OnObjectiveChanged;
        tracker.ObjectiveCompleted += OnObjectiveCompleted;
    }

    void OnDisable()
    {
        if (tracker == null) return;
        tracker.QuestStarted -= OnQuestChanged;
        tracker.QuestCompleted -= OnQuestChanged;
        tracker.ObjectiveActivated -= OnObjectiveChanged;
        tracker.ObjectiveProgressed -= OnObjectiveChanged;
        tracker.ObjectiveCompleted -= OnObjectiveCompleted;
    }

    void Start()
    {
        // A blank quest panel doesn't look like anything else going wrong, so say which
        // field needs filling in.
        if (questText == null)
            Debug.LogWarning($"[QuestHud] '{name}' has no Quest Text wired, so the quest list will never show anything. Drag the TMP text object from inside your quest panel into the Quest Text field.", this);
        if (tracker == null)
            Debug.LogWarning($"[QuestHud] '{name}' found no QuestTracker in the scene. Add a QuestTracker component to a scene object and wire its Database, or drag it into this panel's Tracker field.", this);

        Rebuild();
    }

    private void OnQuestChanged(QuestTracker.QuestProgress quest) => Rebuild();
    private void OnObjectiveChanged(QuestTracker.ObjectiveProgress objective) => Rebuild();

    private void OnObjectiveCompleted(QuestTracker.ObjectiveProgress objective)
    {
        // Start the timer. Update takes the line off once it runs out.
        completedAt[objective] = Time.time;
        Rebuild();
    }

    // Only does anything while a finished line is waiting to disappear. The rest of the
    // time the list is driven by events and this quits on the first line.
    void Update()
    {
        if (completedAt.Count == 0) return;

        bool expired = false;
        foreach (KeyValuePair<QuestTracker.ObjectiveProgress, float> entry in completedAt)
        {
            if (dropped.Contains(entry.Key)) continue;
            if (Time.time < entry.Value + completedHoldSeconds) continue;
            // Fine to add here. dropped is a different collection to the one being
            // looped over, so this isn't changing a list while reading it.
            dropped.Add(entry.Key);
            expired = true;
        }

        if (expired) Rebuild();
    }

    private void Rebuild()
    {
        if (questText == null || tracker == null) return;

        builder.Clear();
        IReadOnlyList<QuestTracker.QuestProgress> quests = tracker.Quests;

        for (int q = 0; q < quests.Count; q++)
        {
            QuestTracker.QuestProgress quest = quests[q];
            if (quest.definition == null) continue;

            // Work out this quest's lines first. A quest whose objectives have all
            // finished and gone shouldn't be left with a title sitting over nothing.
            lines.Clear();
            for (int o = 0; o < quest.objectives.Count; o++)
            {
                QuestTracker.ObjectiveProgress objective = quest.objectives[o];
                // Not given out yet. A Sequential quest keeps its later objectives
                // hidden so the list doesn't give away what's coming.
                if (objective.state == ObjectiveState.Inactive) continue;
                if (dropped.Contains(objective)) continue;
                lines.Add(FormatObjective(objective));
            }

            if (lines.Count == 0) continue;

            // Blank line between quests, but never one at the very top.
            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine(string.Format(questTitleFormat, quest.definition.title));
            for (int i = 0; i < lines.Count; i++) builder.AppendLine(lines[i]);
        }

        questText.text = builder.ToString().TrimEnd();
    }

    private string FormatObjective(QuestTracker.ObjectiveProgress objective)
    {
        ObjectiveDefinition definition = objective.definition;

        string line = definition.kind == ObjectiveKind.ClearGroup
            ? string.Format(clearGroupFormat, definition.title, Mathf.Max(0, objective.current))
            : string.Format(killCountFormat, definition.title, objective.current, objective.required);

        if (definition.optional && !string.IsNullOrEmpty(optionalFormat))
            line = string.Format(optionalFormat, line);

        // Coloured with a rich text tag rather than the label's own colour, because the
        // other lines share the same text object and have to stay as they are.
        if (objective.state == ObjectiveState.Complete)
            line = $"<color=#{ColorUtility.ToHtmlStringRGB(completedTint)}>{completedPrefix}{line}</color>";

        return line;
    }
}
