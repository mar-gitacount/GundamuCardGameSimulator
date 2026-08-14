#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>収録ラインに応じて、対応するセット名プルダウンだけを表示する。</summary>
[CustomEditor(typeof(CardData))]
public class CardDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "productLine",
            "boosterSet",
            "starterSet",
            "eternalBoosterSet");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("収録セット（プルダウン）", EditorStyles.boldLabel);

        SerializedProperty productLine = serializedObject.FindProperty("productLine");
        SerializedProperty boosterSet = serializedObject.FindProperty("boosterSet");
        SerializedProperty starterSet = serializedObject.FindProperty("starterSet");
        SerializedProperty eternalSet = serializedObject.FindProperty("eternalBoosterSet");

        EditorGUILayout.PropertyField(productLine);

        CardProductLine line = (CardProductLine)productLine.intValue;
        switch (line)
        {
            case CardProductLine.Booster:
                EditorGUILayout.PropertyField(boosterSet, new GUIContent("ブースター作品"));
                break;
            case CardProductLine.Starter:
                EditorGUILayout.PropertyField(starterSet, new GUIContent("スターター作品"));
                break;
            case CardProductLine.EternalBooster:
                EditorGUILayout.PropertyField(eternalSet, new GUIContent("Eternal Booster 作品"));
                break;
            default:
                EditorGUILayout.HelpBox("収録ラインを選ぶと、セット名プルダウンが表示されます。", MessageType.Info);
                break;
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            foreach (Object obj in targets)
            {
                CardData card = obj as CardData;
                if (card != null)
                {
                    card.SyncProductFieldsFromLine();
                    EditorUtility.SetDirty(card);
                }
            }
        }
    }
}
#endif
