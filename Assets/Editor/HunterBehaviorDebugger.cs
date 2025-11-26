using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

[CustomEditor(typeof(HunterAI))]
public class HunterAIDebugger : Editor
{
    private HunterAI hunter;

    private void OnEnable()
    {
        hunter = (HunterAI)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(20);
        GUILayout.Label("Behavior Tree Visualization", EditorStyles.boldLabel);

        var field = typeof(HunterAI).GetField("rootNode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Node root = field != null ? (Node)field.GetValue(hunter) : null;

        if (root != null)
        {
            DrawNode(root, 0, true);
        }
        else
        {
            EditorGUILayout.HelpBox("Behavior Tree not initialized (Play mode only).", MessageType.Info);
        }

        if (Application.isPlaying)
        {
            Repaint();
        }
    }

    private void DrawNode(Node node, int indentLevel, bool isParentActive)
    {
        // 1. Determine Effective State
        Node.NodeState state = node.GetNodeState();
        bool isNodeActive = isParentActive && (state == Node.NodeState.RUNNING);

        // 2. Color Logic
        GUIStyle style = new GUIStyle(EditorStyles.label);

        if (!isParentActive)
        {
            style.normal.textColor = Color.gray;
        }
        else
        {
            switch (state)
            {
                case Node.NodeState.RUNNING:
                    style.normal.textColor = Color.yellow;
                    break;
                case Node.NodeState.SUCCESS:
                    style.normal.textColor = Color.green;
                    break;
                case Node.NodeState.FAILURE:
                    style.normal.textColor = new Color(1f, 0.4f, 0.4f); // Softer Red
                    break;
                default:
                    style.normal.textColor = Color.gray;
                    break;
            }
        }

        // 3. Symbol Logic (Updated for Conditions vs Tasks)
        string symbol = "";
        string displayName = "";

        if (!string.IsNullOrEmpty(node.customName))
        {
            displayName = node.customName.ToUpper(); // Make it pop
        }
        else
        {
            // Fallback to class name
            displayName = node.GetType().Name;
            if (displayName.Contains("+")) displayName = displayName.Split('+')[1];
        }

        if (node is Selector)
        {
            symbol = "[?]";
            if (string.IsNullOrEmpty(node.customName)) displayName = "SELECTOR";
        }
        else if (node is Sequence)
        {
            symbol = "[->]";
            if (string.IsNullOrEmpty(node.customName)) displayName = "SEQUENCE";
        }
        else
        {
            // Leaf logic remains the same...
            Type type = node.GetType();
            if (type.IsSubclassOf(typeof(HunterBehaviorNodes.HunterCondition)))
                symbol = "(?)";
            else
                symbol = "< >";
        }

        // 4. Draw Label with Tooltip
        string indent = new string(' ', indentLevel * 4);
        string stateLabel = isParentActive ? state.ToString() : "INACTIVE";
        string labelText = $"{indent}{symbol} {displayName}   ({stateLabel})";

        // --- TOOLTIP LOGIC ---
        // Create GUIContent with (Text, Tooltip)
        string tooltipText = string.IsNullOrEmpty(node.description) ? "No description provided." : node.description;
        GUIContent content = new GUIContent(labelText, tooltipText);

        EditorGUILayout.BeginHorizontal();
        // Use the 'content' object (which contains the text AND the tooltip)
        GUILayout.Label(content, style);
        EditorGUILayout.EndHorizontal();

        // 5. Recursion
        if (node is CompositeNode composite)
        {
            if (composite.Children != null)
            {
                foreach (Node child in composite.Children)
                {
                    DrawNode(child, indentLevel + 1, isNodeActive);
                }
            }
        }
    }
}