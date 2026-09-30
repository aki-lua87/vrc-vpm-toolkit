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
        EditorGUILayout.HelpBox("複数のオブジェクトを登録して結合すると、メッシュを1つに結合した新しいオブジェクトを作成します。元のオブジェクトは非表示になります。同じマテリアルはマージされます。SkinnedMeshRenderer (ボーン・ブレンドシェイプ) にも対応しています。", MessageType.Info);

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

        var skinnedMeshRenderers = validTargets
            .SelectMany(t => t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            .Where(smr => smr.sharedMesh != null)
            .Distinct()
            .ToList();

        if (meshFilters.Count == 0 && skinnedMeshRenderers.Count == 0)
        {
            EditorUtility.DisplayDialog("メッシュなし", "登録したオブジェクトに MeshFilter/MeshRenderer または SkinnedMeshRenderer を持つメッシュが見つかりませんでした。", "OK");
            return;
        }

        if (skinnedMeshRenderers.Count > 0)
        {
            ExecuteSkinnedCombine(validTargets, meshFilters, skinnedMeshRenderers);
        }
        else
        {
            ExecuteStaticCombine(validTargets, meshFilters);
        }
    }

    private void ExecuteStaticCombine(List<GameObject> validTargets, List<MeshFilter> meshFilters)
    {
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

    // SkinnedMeshRenderer を含む場合、ボーン・バインドポーズ・ブレンドシェイプを保ったまま
    // 1つの SkinnedMeshRenderer へ手動で結合する。
    // (Mesh.CombineMeshes はボーンウェイト/ブレンドシェイプを扱えないため)
    private void ExecuteSkinnedCombine(List<GameObject> validTargets, List<MeshFilter> meshFilters, List<SkinnedMeshRenderer> skinnedMeshRenderers)
    {
        var combinedBones = new List<Transform>();
        var combinedBindposes = new List<Matrix4x4>();
        var boneIndexMap = new Dictionary<Transform, int>();

        int GetOrAddBone(Transform bone, Matrix4x4 bindpose)
        {
            if (bone != null && boneIndexMap.TryGetValue(bone, out int existingIndex))
            {
                return existingIndex;
            }

            combinedBones.Add(bone);
            combinedBindposes.Add(bindpose);
            int newIndex = combinedBones.Count - 1;
            if (bone != null)
            {
                boneIndexMap[bone] = newIndex;
            }
            return newIndex;
        }

        var materialsList = new List<Material>();
        var triangleListsByMaterial = new List<List<int>>();

        int AddMaterialSubmesh(Material material)
        {
            int index = materialsList.IndexOf(material);
            if (index < 0)
            {
                materialsList.Add(material);
                triangleListsByMaterial.Add(new List<int>());
                index = materialsList.Count - 1;
            }
            return index;
        }

        var allPositions = new List<Vector3>();
        var allNormals = new List<Vector3>();
        var allTangents = new List<Vector4>();
        var allUv0 = new List<Vector2>();
        var allUv1 = new List<Vector2>();
        var allColors = new List<Color32>();
        var allBoneWeights = new List<BoneWeight>();

        var blendShapeSources = new List<(Mesh mesh, int vertexOffset, int vertexCount)>();
        var blendShapeNames = new List<string>();

        foreach (var smr in skinnedMeshRenderers)
        {
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            var bindposes = mesh.bindposes;
            var meshBoneWeights = mesh.boneWeights;

            // ボーンが割り当てられていない (ブレンドシェイプ専用などの) SkinnedMeshRenderer は
            // スキニングによる変形が一切行われず、自身のトランスフォームがそのまま位置として使われる。
            // その場合は自身のトランスフォームを単一の剛体ボーンとして結合する。
            bool isRigid = bones.Length == 0 || meshBoneWeights.Length == 0;
            int rigidBoneIndex = -1;
            int[] localBoneRemap = null;
            if (isRigid)
            {
                rigidBoneIndex = GetOrAddBone(smr.transform, Matrix4x4.identity);
            }
            else
            {
                localBoneRemap = new int[bones.Length];
                for (int boneIdx = 0; boneIdx < bones.Length; boneIdx++)
                {
                    var bindpose = boneIdx < bindposes.Length ? bindposes[boneIdx] : Matrix4x4.identity;
                    localBoneRemap[boneIdx] = GetOrAddBone(bones[boneIdx], bindpose);
                }
            }

            int vertexOffset = allPositions.Count;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv0 = mesh.uv;
            var uv1 = mesh.uv2;
            var colors32 = mesh.colors32;

            for (int v = 0; v < vertices.Length; v++)
            {
                allPositions.Add(vertices[v]);
                allNormals.Add(v < normals.Length ? normals[v] : Vector3.up);
                allTangents.Add(v < tangents.Length ? tangents[v] : new Vector4(1f, 0f, 0f, 1f));
                allUv0.Add(v < uv0.Length ? uv0[v] : Vector2.zero);
                allUv1.Add(v < uv1.Length ? uv1[v] : Vector2.zero);
                allColors.Add(v < colors32.Length ? colors32[v] : new Color32(255, 255, 255, 255));

                if (isRigid)
                {
                    allBoneWeights.Add(new BoneWeight { boneIndex0 = rigidBoneIndex, weight0 = 1f });
                    continue;
                }

                var bw = v < meshBoneWeights.Length ? meshBoneWeights[v] : default;
                allBoneWeights.Add(new BoneWeight
                {
                    boneIndex0 = bw.weight0 > 0f ? localBoneRemap[bw.boneIndex0] : 0,
                    weight0 = bw.weight0,
                    boneIndex1 = bw.weight1 > 0f ? localBoneRemap[bw.boneIndex1] : 0,
                    weight1 = bw.weight1,
                    boneIndex2 = bw.weight2 > 0f ? localBoneRemap[bw.boneIndex2] : 0,
                    weight2 = bw.weight2,
                    boneIndex3 = bw.weight3 > 0f ? localBoneRemap[bw.boneIndex3] : 0,
                    weight3 = bw.weight3,
                });
            }

            var materials = smr.sharedMaterials;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                var material = subMeshIndex < materials.Length ? materials[subMeshIndex] : materials.LastOrDefault();
                if (material == null)
                {
                    continue;
                }

                int matIndex = AddMaterialSubmesh(material);
                var tris = mesh.GetTriangles(subMeshIndex);
                var list = triangleListsByMaterial[matIndex];
                for (int t = 0; t < tris.Length; t++)
                {
                    list.Add(tris[t] + vertexOffset);
                }
            }

            if (mesh.blendShapeCount > 0)
            {
                blendShapeSources.Add((mesh, vertexOffset, vertices.Length));
                for (int s = 0; s < mesh.blendShapeCount; s++)
                {
                    var shapeName = mesh.GetBlendShapeName(s);
                    if (!blendShapeNames.Contains(shapeName))
                    {
                        blendShapeNames.Add(shapeName);
                    }
                }
            }
        }

        // 非スキンメッシュは、自身のトランスフォームを単一のボーン (バインドポーズ = identity) として扱うことで
        // 同じ SkinnedMeshRenderer へ剛体として結合する。
        foreach (var mf in meshFilters)
        {
            var mesh = mf.sharedMesh;
            var renderer = mf.GetComponent<MeshRenderer>();
            int boneIndex = GetOrAddBone(mf.transform, Matrix4x4.identity);

            int vertexOffset = allPositions.Count;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv0 = mesh.uv;
            var uv1 = mesh.uv2;
            var colors32 = mesh.colors32;

            for (int v = 0; v < vertices.Length; v++)
            {
                allPositions.Add(vertices[v]);
                allNormals.Add(v < normals.Length ? normals[v] : Vector3.up);
                allTangents.Add(v < tangents.Length ? tangents[v] : new Vector4(1f, 0f, 0f, 1f));
                allUv0.Add(v < uv0.Length ? uv0[v] : Vector2.zero);
                allUv1.Add(v < uv1.Length ? uv1[v] : Vector2.zero);
                allColors.Add(v < colors32.Length ? colors32[v] : new Color32(255, 255, 255, 255));
                allBoneWeights.Add(new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f });
            }

            var materials = renderer.sharedMaterials;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                var material = subMeshIndex < materials.Length ? materials[subMeshIndex] : materials.LastOrDefault();
                if (material == null)
                {
                    continue;
                }

                int matIndex = AddMaterialSubmesh(material);
                var tris = mesh.GetTriangles(subMeshIndex);
                var list = triangleListsByMaterial[matIndex];
                for (int t = 0; t < tris.Length; t++)
                {
                    list.Add(tris[t] + vertexOffset);
                }
            }
        }

        if (materialsList.Count == 0)
        {
            EditorUtility.DisplayDialog("メッシュなし", "結合可能なサブメッシュが見つかりませんでした。", "OK");
            return;
        }

        var combinedMesh = new Mesh { name = combinedObjectName };
        if (allPositions.Count > 65535)
        {
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        combinedMesh.SetVertices(allPositions);
        combinedMesh.SetNormals(allNormals);
        combinedMesh.SetTangents(allTangents);
        combinedMesh.SetUVs(0, allUv0);
        combinedMesh.SetUVs(1, allUv1);
        combinedMesh.SetColors(allColors);
        combinedMesh.boneWeights = allBoneWeights.ToArray();
        combinedMesh.bindposes = combinedBindposes.ToArray();

        combinedMesh.subMeshCount = materialsList.Count;
        for (int i = 0; i < materialsList.Count; i++)
        {
            combinedMesh.SetTriangles(triangleListsByMaterial[i], i);
        }

        // ブレンドシェイプを名前単位でマージする (欠けているメッシュ/フレームは差分ゼロで埋める)
        foreach (var shapeName in blendShapeNames)
        {
            int maxFrameCount = 0;
            foreach (var src in blendShapeSources)
            {
                int shapeIndex = src.mesh.GetBlendShapeIndex(shapeName);
                if (shapeIndex >= 0)
                {
                    maxFrameCount = Mathf.Max(maxFrameCount, src.mesh.GetBlendShapeFrameCount(shapeIndex));
                }
            }

            for (int frame = 0; frame < maxFrameCount; frame++)
            {
                var deltaVertices = new Vector3[allPositions.Count];
                var deltaNormals = new Vector3[allPositions.Count];
                var deltaTangents = new Vector3[allPositions.Count];
                float frameWeight = 100f;
                bool weightSet = false;

                foreach (var src in blendShapeSources)
                {
                    int shapeIndex = src.mesh.GetBlendShapeIndex(shapeName);
                    if (shapeIndex < 0)
                    {
                        continue;
                    }

                    int frameCount = src.mesh.GetBlendShapeFrameCount(shapeIndex);
                    if (frame >= frameCount)
                    {
                        continue;
                    }

                    var dv = new Vector3[src.vertexCount];
                    var dn = new Vector3[src.vertexCount];
                    var dt = new Vector3[src.vertexCount];
                    float w = src.mesh.GetBlendShapeFrameWeight(shapeIndex, frame);
                    src.mesh.GetBlendShapeFrameVertices(shapeIndex, frame, dv, dn, dt);

                    for (int v = 0; v < src.vertexCount; v++)
                    {
                        deltaVertices[src.vertexOffset + v] = dv[v];
                        deltaNormals[src.vertexOffset + v] = dn[v];
                        deltaTangents[src.vertexOffset + v] = dt[v];
                    }

                    if (!weightSet)
                    {
                        frameWeight = w;
                        weightSet = true;
                    }
                }

                combinedMesh.AddBlendShapeFrame(shapeName, frameWeight, deltaVertices, deltaNormals, deltaTangents);
            }
        }

        combinedMesh.RecalculateBounds();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        var combinedObject = new GameObject(string.IsNullOrEmpty(combinedObjectName) ? "CombinedMesh" : combinedObjectName);
        Undo.RegisterCreatedObjectUndo(combinedObject, "メッシュ結合");

        var newSkinnedMeshRenderer = combinedObject.AddComponent<SkinnedMeshRenderer>();
        newSkinnedMeshRenderer.sharedMesh = combinedMesh;
        newSkinnedMeshRenderer.bones = combinedBones.ToArray();
        newSkinnedMeshRenderer.sharedMaterials = materialsList.ToArray();
        newSkinnedMeshRenderer.localBounds = combinedMesh.bounds;

        var rootBone = skinnedMeshRenderers.Select(s => s.rootBone).FirstOrDefault(rb => rb != null);
        if (rootBone != null)
        {
            newSkinnedMeshRenderer.rootBone = rootBone;
        }

        // 結合前の各メッシュに設定されていたブレンドシェイプのウェイトを引き継ぐ
        foreach (var shapeName in blendShapeNames)
        {
            int combinedIndex = combinedMesh.GetBlendShapeIndex(shapeName);
            if (combinedIndex < 0)
            {
                continue;
            }

            foreach (var smr in skinnedMeshRenderers)
            {
                int srcIndex = smr.sharedMesh.GetBlendShapeIndex(shapeName);
                if (srcIndex < 0)
                {
                    continue;
                }

                float weight = smr.GetBlendShapeWeight(srcIndex);
                if (weight != 0f)
                {
                    newSkinnedMeshRenderer.SetBlendShapeWeight(combinedIndex, weight);
                    break;
                }
            }
        }

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

        int totalMeshCount = skinnedMeshRenderers.Count + meshFilters.Count;
        EditorUtility.DisplayDialog("完了", $"{totalMeshCount} 個のメッシュ、{materialsList.Count} 個のマテリアルで結合しました (SkinnedMeshRenderer)。", "OK");
    }
}
