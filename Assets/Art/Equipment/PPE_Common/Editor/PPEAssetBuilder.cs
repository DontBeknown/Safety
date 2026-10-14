using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Generated art setup only. Does not modify scenes or implement gameplay.
public static class PPEAssetBuilder
{
    const string Base = "Assets/Art/Equipment/";
    const string Shared = Base + "PPE_Common/";
    [Serializable] public class Spec
    {
        public string name, root, folder;
        public Vector3 dimensions, anchor;
        public int triangles, budget;
        public string Folder => Base + folder + "/";
    }
    [Serializable] public class Manifest { public Spec[] assets; }
    [Serializable] public class Result
    {
        public string name, status = "passed";
        public Vector3 dimensions, anchor;
        public int triangles, renderers, colliders;
    }
    [Serializable] public class Report { public string unityVersion; public Result[] assets; }
    static Spec[] Specs() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Shared + "asset-manifest.json")).assets;
    static void Require(bool ok, string message) { if (!ok) throw new Exception("PPE: " + message); }
    static Transform Find(GameObject o, string name) => o.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    static Bounds BoundsOf(GameObject o)
    {
        var rs = o.GetComponentsInChildren<MeshRenderer>(true);
        Require(rs.Length > 0, o.name + " has no renderers");
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material ResolveMaterial(string name)
    {
        string path = name.StartsWith("CM_") ? Base + "CurrentMeter/Materials/" + name + ".mat" : Shared + "Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Require(!name.StartsWith("CM_"), "Missing shared current-meter palette material: " + name);
        Require(name == "PPE_InsulatingRubber" || name == "PPE_Interior", "Unknown material: " + name);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        Require(shader != null, "URP Lit shader unavailable");
        material = new Material(shader) { name = name };
        bool interior = name == "PPE_Interior";
        material.SetColor("_BaseColor", (interior ? new Color(.045f,.013f,.008f) : new Color(.65f,.045f,.012f)).gamma);
        material.SetFloat("_Smoothness", interior ? .15f : .48f);
        material.SetFloat("_Metallic", 0);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Vector3 ReflectX(Vector3 v) => new Vector3(-v.x, v.y, v.z);
    static void BakeGeometry(GameObject imported, GameObject root, Spec spec)
    {
        string meshFolder = spec.Folder + "Meshes";
        if (!AssetDatabase.IsValidFolder(meshFolder)) AssetDatabase.CreateFolder(spec.Folder.TrimEnd('/'), "Meshes");
        var geometry = new GameObject("Geometry"); geometry.transform.SetParent(root.transform, false);
        foreach (var filter in imported.GetComponentsInChildren<MeshFilter>())
        {
            // FBX handedness conversion reflects source X when semantic up/forward are aligned.
            // Bake the compensating reflection, inverse-transpose normals and winding together.
            var source = filter.sharedMesh;
            var baked = UnityEngine.Object.Instantiate(source); baked.name = filter.name;
            Matrix4x4 m = filter.transform.localToWorldMatrix;
            baked.vertices = source.vertices.Select(v => ReflectX(m.MultiplyPoint3x4(v))).ToArray();
            Matrix4x4 normalMatrix = m.inverse.transpose;
            baked.normals = source.normals.Select(n => ReflectX(normalMatrix.MultiplyVector(n)).normalized).ToArray();
            if (source.tangents.Length > 0)
                baked.tangents = source.tangents.Select(t => { var xyz = ReflectX(m.MultiplyVector(new Vector3(t.x,t.y,t.z))).normalized; return new Vector4(xyz.x,xyz.y,xyz.z,-t.w); }).ToArray();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var indices = source.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3) { int swap = indices[i+1]; indices[i+1] = indices[i+2]; indices[i+2] = swap; }
                baked.SetTriangles(indices, sub);
            }
            baked.RecalculateBounds();
            string path = meshFolder + "/" + filter.name + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(baked, path); saved = baked; }
            else { EditorUtility.CopySerialized(baked, saved); UnityEngine.Object.DestroyImmediate(baked); EditorUtility.SetDirty(saved); }
            var part = new GameObject(filter.name); part.transform.SetParent(geometry.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = saved;
            part.AddComponent<MeshRenderer>().sharedMaterials = filter.GetComponent<MeshRenderer>().sharedMaterials.Select(mat => ResolveMaterial(mat.name)).ToArray();
        }
        foreach (string axis in new[] { "Up", "Forward", "Right" })
        {
            string name = spec.name + "_Import" + axis;
            var marker = new GameObject(name); marker.transform.SetParent(geometry.transform, false);
            marker.transform.localPosition = ReflectX(Find(imported, name).position);
        }
        var anchor = new GameObject("EquipAnchor"); anchor.transform.SetParent(root.transform, false);
        anchor.transform.localPosition = ReflectX(Find(imported, spec.name + "_EquipAnchor").position);
    }

    [MenuItem("Tools/Safety/Rebuild PPE Prefabs")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Shared + "Materials")) AssetDatabase.CreateFolder(Shared.TrimEnd('/'), "Materials");
        foreach (var spec in Specs())
        {
            string modelPath = spec.Folder + spec.name + ".fbx";
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            Require(importer != null, "Missing model: " + modelPath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true; // Needed only while baking a positive-scale Unity mesh.
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), ResolveMaterial(source.name));
            importer.SaveAndReimport();
            var root = new GameObject(spec.root);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                model.name = "Geometry"; model.transform.SetParent(root.transform, false);
                var up = (Find(model, spec.name + "_ImportUp").position - model.transform.position).normalized;
                var forward = (Find(model, spec.name + "_ImportForward").position - model.transform.position).normalized;
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.transform.rotation;
                model.transform.localPosition = Vector3.zero;
                BakeGeometry(model, root, spec);
                UnityEngine.Object.DestroyImmediate(model);
                if (spec.name.StartsWith("Boot"))
                {
                    var foot = root.AddComponent<BoxCollider>(); foot.center = new Vector3(0,.038f,0); foot.size = new Vector3(.110f,.076f,.290f);
                    var ankle = root.AddComponent<BoxCollider>(); ankle.center = new Vector3(spec.anchor.x,.125f,-.061f); ankle.size = new Vector3(.087f,.09f,.10f);
                }
                else
                {
                    var b = BoundsOf(root); var collider = root.AddComponent<BoxCollider>(); collider.center = b.center; collider.size = b.size;
                }
                PrefabUtility.SaveAsPrefabAsset(root, spec.Folder + spec.root + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
        Validate();
    }

    [MenuItem("Tools/Safety/Validate PPE Prefabs")]
    public static void Validate()
    {
        var results = new List<Result>();
        foreach (var spec in Specs())
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Folder + spec.root + ".prefab");
            Require(asset != null, "Missing prefab: " + spec.name);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                Require(root.name == spec.root, "Root name: " + root.name);
                Require(root.transform.localPosition == Vector3.zero && root.transform.localScale == Vector3.one && Quaternion.Angle(root.transform.localRotation, Quaternion.identity) < .01f, spec.name + " root transform");
                var bounds = BoundsOf(root);
                Require((bounds.size - spec.dimensions).sqrMagnitude < .000001f, spec.name + " dimensions " + bounds.size.ToString("F6"));
                Require(Mathf.Abs(bounds.min.y) < .001f, spec.name + " ground pivot");
                var anchor = Find(root, "EquipAnchor");
                Require(anchor.parent == root.transform, spec.name + " anchor must be a direct child");
                Require((anchor.localPosition - spec.anchor).magnitude < .0001f, spec.name + " anchor position");
                Require(Quaternion.Angle(anchor.localRotation, Quaternion.identity) < .01f, spec.name + " anchor axes");
                foreach (var item in new[] { ("Up", Vector3.up), ("Forward", Vector3.forward), ("Right", Vector3.right) })
                {
                    var basis = Find(root, spec.name + "_Import" + item.Item1);
                    Require((basis.position - item.Item2 * .1f).magnitude < .0001f, spec.name + " incorrect " + item.Item1 + " basis: " + basis.position);
                }
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                foreach (var r in renderers)
                {
                    Require(r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.name == "Universal Render Pipeline/Lit"), r.name + " materials");
                    Require(r.sharedMaterials.All(m => AssetDatabase.GetAssetPath(m).EndsWith(".mat")), r.name + " embedded material");
                }
                int tris = root.GetComponentsInChildren<MeshFilter>().Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)) / 3);
                Require(tris == spec.triangles && tris <= spec.budget, spec.name + " triangle count " + tris);
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = filter.sharedMesh; var vertices = mesh.vertices; var normals = mesh.normals;
                    double signedVolume = 0;
                    Require(normals.Length == vertices.Length, filter.name + " normals missing");
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var indices = mesh.GetTriangles(sub);
                        for (int i = 0; i < indices.Length; i += 3)
                        {
                            int a = indices[i], b = indices[i+1], c = indices[i+2];
                            Vector3 cross = Vector3.Cross(vertices[b]-vertices[a], vertices[c]-vertices[a]);
                            Require(cross.sqrMagnitude > 1e-24f, filter.name + " degenerate triangle");
                            Require(Vector3.Dot(cross, normals[a]+normals[b]+normals[c]) > 0, filter.name + " normal/winding disagreement");
                            signedVolume += Vector3.Dot(vertices[a], Vector3.Cross(vertices[b], vertices[c])) / 6.0;
                        }
                    }
                    Require(signedVolume > 0, filter.name + " inward closed mesh");
                }
                var colliders = root.GetComponentsInChildren<Collider>();
                Require(colliders.Length == (spec.name.StartsWith("Boot") ? 2 : 1), spec.name + " collider count");
                Require(colliders.All(c => c is BoxCollider && c.enabled && !c.isTrigger), spec.name + " collider setup");
                Require(root.GetComponentsInChildren<Rigidbody>().Length == 0, spec.name + " unexpected Rigidbody");
                Require(root.GetComponentsInChildren<Animator>().Length == 0 && root.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0, spec.name + " unexpected rig");
                Require(root.GetComponentsInChildren<Light>().Length == 0 && root.GetComponentsInChildren<Camera>().Length == 0, spec.name + " studio objects exported");
                foreach (var t in root.GetComponentsInChildren<Transform>())
                {
                    Require(t.lossyScale.x > 0 && t.lossyScale.y > 0 && t.lossyScale.z > 0, t.name + " negative scale");
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, t.name + " missing script");
                }
                results.Add(new Result { name = spec.name, dimensions = bounds.size, anchor = anchor.localPosition, triangles = tris, renderers = renderers.Length, colliders = colliders.Length });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        string json = JsonUtility.ToJson(new Report { unityVersion = Application.unityVersion, assets = results.ToArray() }, true);
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Art/Models/PPE_Common/unity-validation.json"));
        File.WriteAllText(output, json);
        Debug.Log("PPE_VALIDATION_PASSED " + json);
    }
}
