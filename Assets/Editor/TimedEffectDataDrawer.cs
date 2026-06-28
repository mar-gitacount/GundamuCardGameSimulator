#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TimedEffectData の Inspector。effectsName は JSON マスタからドロップダウン選択。
/// </summary>
[CustomPropertyDrawer(typeof(TimedEffectData))]
public class TimedEffectDataDrawer : PropertyDrawer
{
    private static string[] popupLabels;
    private static string[] popupValues;

    private static void RefreshPopupOptions()
    {
        System.Collections.Generic.List<NamedEffectSetJsonEntry> entries = NamedEffectSetCatalog.LoadEntries();
        int count = entries != null ? entries.Count : 0;
        popupValues = new string[count + 1];
        popupLabels = new string[count + 1];
        popupValues[0] = string.Empty;
        popupLabels[0] = "(なし / インライン effects)";

        for (int i = 0; i < count; i++)
        {
            NamedEffectSetJsonEntry entry = entries[i];
            string key = entry.effectSetName.Trim();
            popupValues[i + 1] = key;
            string label = string.IsNullOrWhiteSpace(entry.displayName) ? key : entry.displayName;
            popupLabels[i + 1] = $"{key} — {label}";
        }
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float y = position.y;

        Rect foldoutRect = new Rect(position.x, y, position.width, lineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
        y += lineHeight;

        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;

        SerializedProperty timing = property.FindPropertyRelative("timing");
        SerializedProperty activationConditions = property.FindPropertyRelative("activationConditions");
        SerializedProperty effectsName = property.FindPropertyRelative("effectsName");
        SerializedProperty effects = property.FindPropertyRelative("effects");

        y = DrawChildProperty(position, ref y, timing, false);
        y = DrawChildProperty(position, ref y, activationConditions, true);

        if (Event.current.type == EventType.Layout || popupLabels == null || popupValues == null)
        {
            RefreshPopupOptions();
        }

        Rect popupRect = new Rect(position.x, y, position.width, lineHeight);
        int currentIndex = IndexOfValue(popupValues, effectsName.stringValue);
        if (currentIndex < 0)
        {
            EditorGUI.PropertyField(popupRect, effectsName, new GUIContent("Effects Name (JSON未登録)"));
        }
        else if (popupLabels == null || popupLabels.Length == 0)
        {
            EditorGUI.PropertyField(popupRect, effectsName, new GUIContent("Effects Name"));
        }
        else
        {
            int selectedIndex = EditorGUI.Popup(popupRect, "Effects Name", currentIndex, popupLabels);
            if (selectedIndex >= 0 && selectedIndex < popupValues.Length)
            {
                effectsName.stringValue = popupValues[selectedIndex];
            }
        }

        y += lineHeight + spacing;

        bool usePreset = !string.IsNullOrWhiteSpace(effectsName.stringValue);
        if (usePreset)
        {
            int effectCount = CountEffectsInJson(effectsName.stringValue);
            string preview = effectCount >= 0
                ? $"プリセット「{effectsName.stringValue}」({effectCount} 件) — named_effect_master.json"
                : $"プリセット「{effectsName.stringValue}」が JSON に見つかりません。";
            float helpHeight = EditorGUIUtility.singleLineHeight * 2f;
            Rect helpRect = new Rect(position.x, y, position.width, helpHeight);
            EditorGUI.HelpBox(helpRect, preview, effectCount >= 0 ? MessageType.Info : MessageType.Warning);
            y += helpHeight + spacing;
        }

        EditorGUI.BeginDisabledGroup(usePreset);
        y = DrawChildProperty(position, ref y, effects, true);
        EditorGUI.EndDisabledGroup();

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        if (!property.isExpanded)
        {
            return lineHeight;
        }

        float height = lineHeight + spacing;
        height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("timing"), true);
        height += spacing;
        height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("activationConditions"), true);
        height += spacing;
        height += lineHeight + spacing;

        SerializedProperty effectsName = property.FindPropertyRelative("effectsName");
        if (!string.IsNullOrWhiteSpace(effectsName.stringValue))
        {
            height += EditorGUIUtility.singleLineHeight * 2f + spacing;
        }

        height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("effects"), true);
        return height;
    }

    private static float DrawChildProperty(Rect position, ref float y, SerializedProperty child, bool includeChildren)
    {
        float h = EditorGUI.GetPropertyHeight(child, includeChildren);
        Rect rect = new Rect(position.x, y, position.width, h);
        EditorGUI.PropertyField(rect, child, includeChildren);
        y += h + EditorGUIUtility.standardVerticalSpacing;
        return y;
    }

    private static int IndexOfValue(string[] values, string current)
    {
        if (values == null || values.Length == 0)
        {
            return string.IsNullOrEmpty(current) ? 0 : -1;
        }

        if (string.IsNullOrEmpty(current))
        {
            return 0;
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (string.Equals(values[i], current, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static int CountEffectsInJson(string effectSetName)
    {
        System.Collections.Generic.List<NamedEffectSetJsonEntry> entries = NamedEffectSetCatalog.LoadEntries();
        for (int i = 0; i < entries.Count; i++)
        {
            NamedEffectSetJsonEntry entry = entries[i];
            if (string.Equals(entry.effectSetName, effectSetName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.effects != null ? entry.effects.Length : 0;
            }
        }

        return -1;
    }
}
#endif
