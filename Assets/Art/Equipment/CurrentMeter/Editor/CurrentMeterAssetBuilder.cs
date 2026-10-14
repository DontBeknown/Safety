using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CurrentMeterAssetBuilder
{
    const string Folder = "Assets/Art/Equipment/CurrentMeter";
    const string ModelPath = Folder + "/current-meter.fbx";
    const string PrefabPath = Folder + "/CurrentMeter.prefab";
    static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    static void Require(bool condition, string message) { if (!condition) throw new Exception("CurrentMeter: " + message); }
    static readonly string[] Names = { "Yellow", "Graphite", "Rubber", "Metal", "LCD" };
    // Blender shader inputs are linear. Convert to Unity material UI (sRGB) colors.
    static readonly Color[] Colors = { new Color(.95f,.58f,.008f), new Color(.065f,.073f,.077f), new Color(.018f,.021f,.022f), new Color(.48f,.52f,.54f), new Color(.47f,.55f,.49f) };
    static readonly float[] Roughness = { .48f,.65f,.85f,.34f,.72f };

    [MenuItem("Tools/Safety/Rebuild Current Meter Prefab")]
    public static void Build()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.globalScale = 1;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        if (!AssetDatabase.IsValidFolder(Folder + "/Materials")) AssetDatabase.CreateFolder(Folder,"Materials");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        Require(shader != null, "URP Lit shader missing");
        var materials = new Material[Names.Length];
        for (int i=0;i<Names.Length;i++)
        {
            var path=Folder+"/Materials/CM_"+Names[i]+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m==null) { m=new Material(shader); AssetDatabase.CreateAsset(m,path); }
            m.shader=shader; m.SetColor("_BaseColor",Colors[i].gamma);
            m.SetFloat("_Metallic",i==3?.7f:0); m.SetFloat("_Smoothness",1-Roughness[i]);
            EditorUtility.SetDirty(m); materials[i]=m;
        }
        foreach (var source in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
        {
            int i=Array.FindIndex(Names,n=>source.name=="CM_"+n);
            Require(i>=0,"Unexpected material: "+source.name);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source),materials[i]);
        }
        importer.SaveAndReimport();
        var root=new GameObject("CurrentMeter");
        try
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.name="Geometry"; model.transform.SetParent(root.transform,false);
            // Resolve the imported FBX basis from semantic anchors, independent of importer root rotation.
            var up=(Find(model,"ContactAnchor").position-model.transform.position).normalized;
            var face=Find(model,"DisplayAnchor").position-model.transform.position;
            face=Vector3.ProjectOnPlane(face,up).normalized;
            model.transform.rotation=Quaternion.Inverse(Quaternion.LookRotation(face,up))*model.transform.rotation;
            model.transform.localPosition=Vector3.zero;
            foreach(var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterials=r.sharedMaterials.Select(m=> {
                    int i=Array.FindIndex(Names,n=>m!=null && m.name=="CM_"+n);
                    Require(i>=0,"Unmapped material on "+r.name); return materials[i];
                }).ToArray();
            }
            var body=root.AddComponent<BoxCollider>(); body.center=new Vector3(0,.075f,0); body.size=new Vector3(.065f,.15f,.032f);
            var probe=new GameObject("ProbeCollider"); probe.transform.SetParent(root.transform,false);
            var capsule=probe.AddComponent<CapsuleCollider>(); capsule.direction=1; capsule.center=new Vector3(0,.183f,0); capsule.radius=.0053f; capsule.height=.054f;
            var collar=new GameObject("CollarCollider"); collar.transform.SetParent(root.transform,false);
            var collarBox=collar.AddComponent<BoxCollider>(); collarBox.center=new Vector3(0,.153f,0); collarBox.size=new Vector3(.016f,.006f,.016f);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
        Validate();
    }
    [MenuItem("Tools/Safety/Validate Current Meter Prefab")]
    public static void Validate()
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(asset!=null,"Prefab missing");
        var root=(GameObject)PrefabUtility.InstantiatePrefab(asset);
        try
        {
            var renderers=root.GetComponentsInChildren<MeshRenderer>();
            Require(renderers.Length>0,"No renderers");
            var bounds=renderers[0].bounds;
            foreach(var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                Require(r.sharedMaterials.All(m=>m!=null && m.shader.name=="Universal Render Pipeline/Lit"),"Invalid material: "+r.name);
            }
            Require((bounds.size-new Vector3(.065f,.210f,.032f)).magnitude<.0002f,"Dimensions: "+bounds.size.ToString("F6"));
            Require(Mathf.Abs(bounds.min.y)<.0001f,"Base pivot incorrect");
            Require((Find(root,"ContactAnchor").position-new Vector3(0,.21f,0)).magnitude<.0001f,"Contact anchor incorrect");
            Require((Find(root,"GripAnchor").position-new Vector3(0,.05f,0)).magnitude<.0001f,"Grip anchor incorrect");
            Require(Find(root,"DisplayAnchor").position.z>.016f,"Screen not facing +Z");
            Require(root.GetComponentsInChildren<Collider>().Length==3,"Expected three primitive colliders");
            Require(root.GetComponentsInChildren<Rigidbody>().Length==0,"Unexpected Rigidbody");
            int tris=root.GetComponentsInChildren<MeshFilter>().Sum(f=>(int)Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(long)f.sharedMesh.GetIndexCount(i))/3);
            Require(tris<5000,"Triangle budget exceeded");
            foreach(var t in root.GetComponentsInChildren<Transform>()) Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"Missing script");
            string report="{\n  \"status\": \"passed\",\n  \"unity_version\": \""+Application.unityVersion+"\",\n  \"triangles\": "+tris+",\n  \"mesh_renderers\": "+renderers.Length+",\n  \"dimensions_xyz_m\": ["+bounds.size.x.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+", "+bounds.size.y.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+", "+bounds.size.z.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+"],\n  \"materials_colliders_anchors\": \"passed\"\n}";
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../Art/Models/CurrentMeter/unity-validation.json"));
            File.WriteAllText(output,report);
            Debug.Log("CURRENT_METER_VALIDATION_PASSED " + report);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}

