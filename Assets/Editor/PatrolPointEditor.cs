using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PatrolPoints))]
[CanEditMultipleObjects]
public class PatrolPointsEditor : Editor
{
    SerializedProperty pointType;
    SerializedProperty linkedRoom;
    SerializedProperty manualRoomOwner;
    SerializedProperty partnerPoint; // <--- NEW PROPERTY

    private void OnEnable()
    {
        pointType = serializedObject.FindProperty("pointType");
        linkedRoom = serializedObject.FindProperty("linkedRoom");
        manualRoomOwner = serializedObject.FindProperty("manualRoomOwner");
        partnerPoint = serializedObject.FindProperty("partnerPoint"); // <--- LINK IT
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(pointType);

        PointType currentType = (PointType)pointType.enumValueIndex;

        if (currentType == PointType.Doorway)
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("Doorway Connections", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(linkedRoom);
            EditorGUILayout.PropertyField(manualRoomOwner);
            EditorGUILayout.PropertyField(partnerPoint); // <--- DRAW IT
        }

        serializedObject.ApplyModifiedProperties();
    }
}