using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Inspector for CombatProfile. Three jobs, all of them about making a character's
// DELIBERATE deviations from their role legible:
//
//  1. Banner — the template's own summary, so whoever opens Naledi's profile reads
//     what "Support" is trying to feel like without going and finding the template.
//  2. Override markers — a "template: X" hint under every field whose value differs
//     from the template. Nothing is hidden and nothing is disabled; a designer can
//     always see and edit everything. Hiding fields by role was the alternative and
//     it's a trap: a hidden field is still serialized and still read by the brain,
//     so a stale value from a previous role keeps running invisibly.
//  3. Sync — copies the template's values in, but shows exactly what it's about to
//     overwrite first. Sync is destructive by design (that's the point), so it gets
//     a diff the same way a force-push does.
[CustomEditor(typeof(CombatProfile))]
[CanEditMultipleObjects]
public class CombatProfileEditor : Editor
{
    // Drawn by hand in the archetype section at the top, so the generic field loop
    // must not draw them a second time.
    private static readonly HashSet<string> HandDrawnFields = new HashSet<string>
    {
        "m_Script", "template", "archetypeNote"
    };

    // Live on CombatProfile but deliberately absent from the template: who a
    // character goes out of their way for is a relationship, not a role.
    private static readonly HashSet<string> UntemplatedFields = new HashSet<string>
    {
        "defaultPeelWeight", "peelWeights"
    };

    // Describe the template itself rather than tuning the brain — never synced.
    private static readonly HashSet<string> TemplateOwnFields = new HashSet<string>
    {
        "m_Script", "role", "summary"
    };

    private static readonly Color OverrideTint = new Color(1f, 0.72f, 0.25f);

    // Rebuilt only when the assigned template changes — a new SerializedObject per
    // repaint would allocate on every mouse move.
    private CombatProfileTemplate cachedTemplate;
    private SerializedObject cachedTemplateSerialized;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty templateProp = serializedObject.FindProperty("template");
        CombatProfileTemplate template = templateProp.objectReferenceValue as CombatProfileTemplate;

        // Multi-select can hold profiles on different templates, so a diff against
        // "the" template is meaningless. Fall back to plain fields rather than
        // showing markers that would be wrong for at least one of the selection.
        bool multiEditing = serializedObject.isEditingMultipleObjects;
        SerializedObject templateSerialized = multiEditing ? null : GetTemplateSerialized(template);

        DrawArchetypeSection(templateProp, template, templateSerialized, multiEditing);
        DrawTunableFields(templateSerialized);

        serializedObject.ApplyModifiedProperties();
    }

    private SerializedObject GetTemplateSerialized(CombatProfileTemplate template)
    {
        if (template == null)
        {
            cachedTemplate = null;
            cachedTemplateSerialized = null;
            return null;
        }

        if (cachedTemplate != template || cachedTemplateSerialized == null)
        {
            cachedTemplate = template;
            cachedTemplateSerialized = new SerializedObject(template);
        }

        // The template asset can be edited in another inspector while this one is
        // open; without this the diff goes stale and shows phantom overrides.
        cachedTemplateSerialized.Update();
        return cachedTemplateSerialized;
    }

    private void DrawArchetypeSection(SerializedProperty templateProp, CombatProfileTemplate template,
                                      SerializedObject templateSerialized, bool multiEditing)
    {
        EditorGUILayout.LabelField("Archetype", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(templateProp);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("archetypeNote"));

        if (multiEditing)
        {
            EditorGUILayout.HelpBox("Multiple profiles selected — template comparison and sync are per-asset. Select one to see overrides.", MessageType.None);
            EditorGUILayout.Space();
            return;
        }

        if (template == null)
        {
            EditorGUILayout.HelpBox(
                "No template. Assign one of the role templates in Assets/Companion/CombatTemplates to get sensible starting numbers " +
                "and to have this inspector show which values you've deliberately changed.",
                MessageType.Info);
            EditorGUILayout.Space();
            return;
        }

        if (!string.IsNullOrWhiteSpace(template.summary))
            EditorGUILayout.HelpBox(template.summary, MessageType.Info);

        List<FieldDiff> diffs = CollectDiffs(templateSerialized);

        string overrideLine = diffs.Count == 0
            ? $"Identical to {template.name} — no overrides yet."
            : $"{diffs.Count} field{(diffs.Count == 1 ? "" : "s")} overridden. Marked ● below.";
        EditorGUILayout.LabelField(overrideLine, EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(diffs.Count == 0))
        {
            if (GUILayout.Button($"Sync From {template.name}"))
                SyncFromTemplate(template, templateSerialized, diffs);
        }

        EditorGUILayout.Space();
    }

    private void DrawTunableFields(SerializedObject templateSerialized)
    {
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            // Never descend: we want top-level fields only. PropertyField below
            // still draws each one's children (lists, structs) itself.
            enterChildren = false;

            string fieldName = iterator.name;
            if (HandDrawnFields.Contains(fieldName)) continue;

            EditorGUILayout.PropertyField(iterator, true);

            // The four intercept knobs stay visible and editable while intercept is
            // off, so flipping it on for one fight shows what values would apply.
            // Saying so beats silently rendering four dead fields.
            if (fieldName == "interceptEnabled" && !iterator.boolValue)
                EditorGUILayout.HelpBox("Intercept is off — the four fields below are stored but never read by the brain.", MessageType.None);

            if (templateSerialized == null) continue;
            if (UntemplatedFields.Contains(fieldName)) continue;

            SerializedProperty templateProp = templateSerialized.FindProperty(fieldName);
            if (templateProp == null) continue;
            if (ValuesMatch(iterator, templateProp)) continue;

            DrawOverrideHint(templateProp);
        }
    }

    private static void DrawOverrideHint(SerializedProperty templateProp)
    {
        Color previous = GUI.color;
        GUI.color = OverrideTint;
        EditorGUILayout.LabelField(" ", "● template: " + ValueLabel(templateProp), EditorStyles.miniLabel);
        GUI.color = previous;
    }

    private struct FieldDiff
    {
        public string propertyName;
        public string displayName;
        public string currentValue;
        public string templateValue;
    }

    private List<FieldDiff> CollectDiffs(SerializedObject templateSerialized)
    {
        List<FieldDiff> diffs = new List<FieldDiff>();
        if (templateSerialized == null) return diffs;

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            string fieldName = iterator.name;
            if (HandDrawnFields.Contains(fieldName)) continue;
            if (UntemplatedFields.Contains(fieldName)) continue;

            SerializedProperty templateProp = templateSerialized.FindProperty(fieldName);
            if (templateProp == null) continue;
            if (ValuesMatch(iterator, templateProp)) continue;

            diffs.Add(new FieldDiff
            {
                propertyName = fieldName,
                displayName = iterator.displayName,
                currentValue = ValueLabel(iterator),
                templateValue = ValueLabel(templateProp)
            });
        }

        return diffs;
    }

    // Destructive on purpose — but a designer who spent an afternoon tuning this
    // character shouldn't lose it to a misclick, so show the damage first.
    private void SyncFromTemplate(CombatProfileTemplate template, SerializedObject templateSerialized, List<FieldDiff> diffs)
    {
        System.Text.StringBuilder body = new System.Text.StringBuilder();
        body.Append("These values will be overwritten:\n\n");
        for (int i = 0; i < diffs.Count; i++)
            body.Append($"    {diffs[i].displayName}:  {diffs[i].currentValue}  →  {diffs[i].templateValue}\n");
        body.Append("\nPeel Priority is per-character and will not change.");

        if (!EditorUtility.DisplayDialog($"Sync from {template.name}", body.ToString(), "Overwrite", "Cancel"))
            return;

        SerializedProperty iterator = templateSerialized.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (TemplateOwnFields.Contains(iterator.name)) continue;
            if (serializedObject.FindProperty(iterator.name) == null) continue;
            // Matches by property path, which is why the two classes have to keep
            // identical field names.
            serializedObject.CopyFromSerializedProperty(iterator);
        }

        // Registers the undo entry itself — no separate Undo.RecordObject needed.
        serializedObject.ApplyModifiedProperties();

        foreach (Object t in targets)
        {
            EditorUtility.SetDirty(t);
            AssetDatabase.SaveAssetIfDirty(t);
        }
    }

    private static bool ValuesMatch(SerializedProperty a, SerializedProperty b)
    {
        if (a.propertyType != b.propertyType) return false;

        switch (a.propertyType)
        {
            case SerializedPropertyType.Float: return Mathf.Approximately(a.floatValue, b.floatValue);
            case SerializedPropertyType.Integer: return a.intValue == b.intValue;
            case SerializedPropertyType.Boolean: return a.boolValue == b.boolValue;
            case SerializedPropertyType.Enum: return a.enumValueIndex == b.enumValueIndex;
            case SerializedPropertyType.String: return a.stringValue == b.stringValue;
            default: return SerializedProperty.DataEquals(a, b);
        }
    }

    private static string ValueLabel(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Float: return property.floatValue.ToString("0.###");
            case SerializedPropertyType.Integer: return property.intValue.ToString();
            case SerializedPropertyType.Boolean: return property.boolValue ? "on" : "off";
            case SerializedPropertyType.String: return property.stringValue;
            case SerializedPropertyType.Enum:
                return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                    ? property.enumNames[property.enumValueIndex]
                    : "?";
            default: return "(value)";
        }
    }
}
