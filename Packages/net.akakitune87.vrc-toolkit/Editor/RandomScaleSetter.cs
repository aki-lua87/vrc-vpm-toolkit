using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// RandomScaleSetter

public class RandomScaleSetter : EditorWindow
{
    private readonly List<GameObject> targets = new List<GameObject>();
    private Vector2 scrollPosition;
    private float minScale = 1f;
    private float maxScale = 2f;
    private readonly bool[] axisEnabled = new bool[3] { true, false, false };
    private readonly AxisMode[] axisModes = new AxisMode[3] { AxisMode.Set, AxisMode.Set, AxisMode.Set };

    [MenuItem("Tools/aki_lua87/Random Scale Setter")]
    public static void ShowWindow()
    {
        GetWindow<RandomScaleSetter>("Random Scale Setter");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("ランダムスケール設定", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("オブジェクトを登録して、指定した2つの値の間でランダムにスケールを設定します。", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("対象オブジェクト", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(240f));
        for (int i = 0; i < targets.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            targets[i] = (GameObject)EditorGUILayout.ObjectField("オブジェクト", targets[i], typeof(GameObject), true);
            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                targets.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("追加欄"))
        {
            targets.Add(null);
        }

        if (GUILayout.Button("Hierarchy 選択を追加"))
        {
            foreach (var selectedObject in Selection.gameObjects)
            {
                if (!targets.Contains(selectedObject))
                {
                    targets.Add(selectedObject);
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("Hierarchy で1つ以上のオブジェクトを選択して、\"Hierarchy 選択を追加\" を押すと対象に追加できます。", MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("値の範囲", EditorStyles.boldLabel);
        minScale = EditorGUILayout.FloatField("最小値", minScale);
        maxScale = EditorGUILayout.FloatField("最大値", maxScale);

        if (minScale > maxScale)
        {
            float temp = minScale;
            minScale = maxScale;
            maxScale = temp;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("軸の設定", EditorStyles.boldLabel);
        for (int i = 0; i < 3; i++)
        {
            string axisName = i == 0 ? "X" : i == 1 ? "Y" : "Z";
            EditorGUILayout.BeginHorizontal();
            axisEnabled[i] = EditorGUILayout.ToggleLeft(axisName, axisEnabled[i], GUILayout.Width(60f));

            if (axisEnabled[i])
            {
                axisModes[i] = (AxisMode)EditorGUILayout.EnumPopup(axisModes[i], GUILayout.Width(120f));
            }
            else
            {
                EditorGUILayout.LabelField("無効", GUILayout.Width(120f));
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("ランダム適用"))
        {
            ApplyRandomScale();
        }

        if (GUILayout.Button("クリア"))
        {
            targets.Clear();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void ApplyRandomScale()
    {
        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("対象なし", "適用する前に少なくとも1つオブジェクトを登録してください。", "OK");
            return;
        }

        bool anyAxisEnabled = false;
        for (int i = 0; i < 3; i++)
        {
            if (axisEnabled[i])
            {
                anyAxisEnabled = true;
                break;
            }
        }

        if (!anyAxisEnabled)
        {
            EditorUtility.DisplayDialog("軸の設定", "適用する軸を1つ以上選択してください。", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        foreach (var target in targets)
        {
            if (target == null)
            {
                continue;
            }

            Undo.RecordObject(target.transform, "ランダムスケール適用");
            Vector3 originalScale = target.transform.localScale;
            float randomValue = Random.Range(minScale, maxScale);
            Vector3 newScale = originalScale;

            if (axisEnabled[0])
            {
                newScale.x = axisModes[0] == AxisMode.Set ? randomValue : originalScale.x * randomValue;
            }

            if (axisEnabled[1])
            {
                newScale.y = axisModes[1] == AxisMode.Set ? randomValue : originalScale.y * randomValue;
            }

            if (axisEnabled[2])
            {
                newScale.z = axisModes[2] == AxisMode.Set ? randomValue : originalScale.z * randomValue;
            }

            target.transform.localScale = newScale;
            EditorUtility.SetDirty(target.transform);
        }

        Undo.CollapseUndoOperations(group);
        Repaint();
    }

    private enum AxisMode
    {
        Set,
        Multiply
    }
}
