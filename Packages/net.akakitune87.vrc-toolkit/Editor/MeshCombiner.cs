using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// MeshCombiner

public class MeshCombiner : EditorWindow
{
    private readonly List<GameObject> targets = new List<GameObject>();
    private Vector2 scrollPosition;
    private string combinedObjectName = "CombinedMesh";

    [MenuItem("Tools/aki_lua87/Mesh Combiner")]
    public static void ShowWindow()
    {
        GetWindow<MeshCombiner>("Mesh Combiner");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("メッシュ結合", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("複数のオブジェクトを登録して結合すると、メッシュを1つに結合した新しいオブジェクトを作成します。元のオブジェクトは非表示になります。同じマテリアルはマージされます。", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("対象オブジェクト", EditorStyles.boldLabel);

        DrawDropArea();

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

        EditorGUILayout.Space();
        combinedObjectName = EditorGUILayout.TextField("結合後オブジェクト名", combinedObjectName);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("実行"))
        {
            Execute();
        }

        if (GUILayout.Button("クリア"))
        {
            targets.Clear();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDropArea()
    {
        var dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "ここにオブジェクトをドラッグ&ドロップして追加", EditorStyles.helpBox);

        var evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition))
        {
            return;
        }

        switch (evt.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
                break;

            case EventType.DragPerform:
                DragAndDrop.AcceptDrag();

                int skippedAssetCount = 0;
                foreach (var draggedObject in DragAndDrop.objectReferences)
                {
                    var go = draggedObject as GameObject;
                    if (go == null && draggedObject is Component component)
                    {
                        go = component.gameObject;
                    }

                    if (go == null)
                    {
                        continue;
                    }

                    if (EditorUtility.IsPersistent(go))
                    {
                        // プロジェクト上のアセット (プレハブ本体) はシーン上のインスタンスではないので対象外
                        skippedAssetCount++;
                        continue;
                    }

                    if (!targets.Contains(go))
                    {
                        targets.Add(go);
                    }
                }

                if (skippedAssetCount > 0)
                {
                    EditorUtility.DisplayDialog("追加できないオブジェクトがあります", $"{skippedAssetCount} 個のプレハブアセットはシーン上のインスタンスではないため追加できませんでした。", "OK");
                }

                evt.Use();
                Repaint();
                break;
        }
    }

    private void Execute()
    {
        var validTargets = targets.Where(t => t != null).Distinct().ToList();
        if (validTargets.Count == 0)
        {
            EditorUtility.DisplayDialog("対象なし", "結合する前に少なくとも1つオブジェクトを登録してください。", "OK");
            return;
        }

        var meshFilters = validTargets
            .SelectMany(t => t.GetComponentsInChildren<MeshFilter>(true))
            .Where(mf => mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null)
            .Distinct()
            .ToList();

        if (meshFilters.Count == 0)
        {
            EditorUtility.DisplayDialog("メッシュなし", "登録したオブジェクトに MeshFilter と MeshRenderer を持つメッシュが見つかりませんでした。", "OK");
            return;
        }

        // マテリアルごとにサブメッシュ (メッシュ・サブメッシュインデックス・ワールド行列) をまとめる
        var submeshesByMaterial = new List<Material>();
        var combineListByMaterial = new List<List<CombineInstance>>();

        foreach (var meshFilter in meshFilters)
        {
            var renderer = meshFilter.GetComponent<MeshRenderer>();
            var mesh = meshFilter.sharedMesh;
            var materials = renderer.sharedMaterials;
            var matrix = meshFilter.transform.localToWorldMatrix;

            int subMeshCount = mesh.subMeshCount;
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                var material = subMeshIndex < materials.Length ? materials[subMeshIndex] : materials.LastOrDefault();
                if (material == null)
                {
                    continue;
                }

                int materialListIndex = submeshesByMaterial.IndexOf(material);
                if (materialListIndex < 0)
                {
                    submeshesByMaterial.Add(material);
                    combineListByMaterial.Add(new List<CombineInstance>());
                    materialListIndex = submeshesByMaterial.Count - 1;
                }

                combineListByMaterial[materialListIndex].Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = matrix
                });
            }
        }

        // マテリアルごとに1つのサブメッシュへ結合する
        var perMaterialCombine = new CombineInstance[submeshesByMaterial.Count];
        for (int i = 0; i < submeshesByMaterial.Count; i++)
        {
            var perMaterialMesh = new Mesh { name = $"CombinedSubmesh_{i}" };
            perMaterialMesh.CombineMeshes(combineListByMaterial[i].ToArray(), true, true);
            perMaterialCombine[i] = new CombineInstance
            {
                mesh = perMaterialMesh,
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            };
        }

        // 各マテリアルのメッシュを、サブメッシュを保ったまま最終メッシュへ結合する
        var combinedMesh = new Mesh { name = combinedObjectName };
        if (perMaterialCombine.Sum(c => c.mesh.vertexCount) > 65535)
        {
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        combinedMesh.CombineMeshes(perMaterialCombine, false, false);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        var combinedObject = new GameObject(string.IsNullOrEmpty(combinedObjectName) ? "CombinedMesh" : combinedObjectName);
        Undo.RegisterCreatedObjectUndo(combinedObject, "メッシュ結合");

        var newMeshFilter = combinedObject.AddComponent<MeshFilter>();
        newMeshFilter.sharedMesh = combinedMesh;

        var newMeshRenderer = combinedObject.AddComponent<MeshRenderer>();
        newMeshRenderer.sharedMaterials = submeshesByMaterial.ToArray();

        foreach (var target in validTargets)
        {
            Undo.RecordObject(target, "メッシュ結合 (元オブジェクト非表示)");
            target.SetActive(false);
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MoveGameObjectToScene(combinedObject, activeScene);

        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = combinedObject;
        EditorSceneManager.MarkSceneDirty(activeScene);

        EditorUtility.DisplayDialog("完了", $"{meshFilters.Count} 個のメッシュ、{submeshesByMaterial.Count} 個のマテリアルで結合しました。", "OK");
    }
}
