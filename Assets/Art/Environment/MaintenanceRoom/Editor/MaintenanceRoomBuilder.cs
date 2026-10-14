using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Editor-only asset pipeline; no runtime behaviour or changes to existing scenes.
public static class MaintenanceRoomBuilder
{
    const string Folder = "Assets/Art/Environment/MaintenanceRoom";
    const string RoomPath = Folder + "/Prefabs/MaintenanceRoom.prefab";
    [Serializable] public class MaterialSpec { public string name; public float[] color; public float roughness, metallic, emission; }
    [Serializable] public class ColliderSpec { public float[] center, size; public string parent; }
    [Serializable] public class ModuleSpec { public string name; public ColliderSpec[] colliders; public float[] bounds_min, bounds_max; }
    [Serializable] public class InstanceSpec { public string name, module, group; public float[] position, rotation; }
    [Serializable] public class Manifest { public MaterialSpec[] materials; public ModuleSpec[] modules; public InstanceSpec[] instances; }
    [Serializable] public class ValidationReport
    {
        public string status, unity_version;
        public int modules, instances, triangles, mesh_renderers, colliders, materials;
        public bool module_bounds, uvs, lightmap_uvs, hinges, clear_floor_and_table, entrance, shared_prefabs, no_missing_scripts, assembled_fbx_materials;
    }
    static Manifest Read() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/manifest.json"));
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Art/Models/MaintenanceRoom"));
    static void Require(bool value, string message) { if (!value) throw new Exception("MaintenanceRoom: " + message); }
    // Verified FBX importer basis: Blender X -> Unity X, Y -> Z, Z -> Y.
    // This reflection changes the sign of rotation angles; never mirror mesh scales.
    static Vector3 V(float[] b) => new Vector3(b[0],b[2],b[1]);
    static Vector3 Size(float[] b) => new Vector3(b[0],b[2],b[1]);
    static Quaternion Rotation(float[] b) => Quaternion.AngleAxis(-b[2]*Mathf.Rad2Deg,Vector3.up)*Quaternion.AngleAxis(-b[1]*Mathf.Rad2Deg,Vector3.forward)*Quaternion.AngleAxis(-b[0]*Mathf.Rad2Deg,Vector3.right);
    static Transform Find(Transform root,string prefix) => root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==prefix || t.name.StartsWith(prefix+"."));
    static string PrefabPath(string name) => Folder + "/Prefabs/Modules/" + name + ".prefab";
    public static void InspectBasis()
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Models/WallPanel.fbx"));
        try
        {
            Debug.Log("MR_IMPORTED_BASIS X="+Find(model.transform,"BasisX").position.ToString("F6")+" Y="+Find(model.transform,"BasisY").position.ToString("F6")+" Z="+Find(model.transform,"BasisZ").position.ToString("F6")+" origin="+model.transform.position+" scale="+model.transform.localScale);
        }
        finally{UnityEngine.Object.DestroyImmediate(model);}
    }
    static Bounds BoundsOf(GameObject root)
    {
        var rs=root.GetComponentsInChildren<MeshRenderer>(true); Require(rs.Length>0,"No meshes: "+root.name);
        var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
    }
    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        var parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
    }
    static Dictionary<string,Material> CreateMaterials(Manifest spec)
    {
        EnsureFolder(Folder+"/Materials"); var result=new Dictionary<string,Material>();
        var shader=Shader.Find("Universal Render Pipeline/Lit");Require(shader!=null,"URP Lit missing");
        foreach(var s in spec.materials)
        {
            var path=Folder+"/Materials/"+s.name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
            mat.shader=shader;var linear=new Color(s.color[0],s.color[1],s.color[2]);
            mat.SetColor("_BaseColor",linear.gamma);mat.SetFloat("_Metallic",s.metallic);mat.SetFloat("_Smoothness",1-s.roughness);
            if(s.emission>0){mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",linear.gamma*s.emission);mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;}
            EditorUtility.SetDirty(mat);result[s.name]=mat;
        }
        return result;
    }
    static void ConfigureImport(string path,Dictionary<string,Material> materials)
    {
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);Require(importer!=null,"Missing model "+path);
        importer.globalScale=1;importer.useFileScale=true;importer.bakeAxisConversion=true;
        importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;
        importer.importNormals=ModelImporterNormals.Import;importer.meshCompression=ModelImporterMeshCompression.Off;
        importer.generateSecondaryUV=true;importer.isReadable=false;importer.addCollider=false;
        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        foreach(var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
        {
            var name=source.name.Split('.')[0];Require(materials.ContainsKey(name),"Unexpected material "+source.name);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source),materials[name]);
        }
        importer.SaveAndReimport();
    }
    static GameObject MakeModule(ModuleSpec module,Dictionary<string,Material> materials)
    {
        var path=Folder+"/Models/"+module.name+".fbx";ConfigureImport(path,materials);
        var root=new GameObject(module.name);
        try
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            model.name="Geometry";model.transform.SetParent(root.transform,false);
            var origin=model.transform.position;
            var bx=Find(model.transform,"BasisX").position-origin;
            var by=Find(model.transform,"BasisY").position-origin;
            var bz=Find(model.transform,"BasisZ").position-origin;
            Require(Mathf.Abs(bx.magnitude-by.magnitude)<.001f && Mathf.Abs(by.magnitude-bz.magnitude)<.001f,"Nonuniform model units");
            model.transform.rotation=Quaternion.Inverse(Quaternion.LookRotation(by.normalized,bz.normalized))*model.transform.rotation;
            model.transform.localScale/=bx.magnitude;
            Require((Find(model.transform,"BasisX").position-Vector3.right).magnitude<.001f,"X basis incorrect "+module.name);
            Require((Find(model.transform,"BasisZ").position-Vector3.up).magnitude<.001f,"Up basis incorrect "+module.name);
            // Keep hidden semantic markers inside the imported model; prefab remains linked to its FBX.
            foreach(var marker in model.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Basis")))marker.gameObject.SetActive(false);
            foreach(var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterials=r.sharedMaterials.Select(m=>materials[m.name.Split('.')[0]]).ToArray();
                r.receiveShadows=true;r.shadowCastingMode=ShadowCastingMode.On;
            }
            foreach(var s in module.colliders)
            {
                var host=new GameObject("Collision");host.transform.SetParent(root.transform,false);
                // Collider is expressed in Blender local coordinates of its pivot, transformed through the normalized model.
                if(!string.IsNullOrEmpty(s.parent))
                {
                    var pivot=Find(model.transform,s.parent);host.transform.SetParent(pivot,false);
                    // Imported child coordinate space is determined by the parent's full transform.
                    // Build world-aligned collider under the root, then preserve its world orientation when parenting.
                    host.transform.SetParent(root.transform,false);
                    host.transform.position=pivot.position;
                    host.transform.rotation=Quaternion.identity;
                    host.transform.SetParent(pivot,true);
                }
                var c=host.AddComponent<BoxCollider>();c.center=V(s.center);c.size=Size(s.size);
            }
            if(module.name=="WallLight")
            {
                var go=new GameObject("FixtureLight");go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(0,0,-.16f);
                var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.78f,.48f);light.intensity=.4f;light.range=3;light.shadows=LightShadows.Soft;
            }
            VerifyModule(root,module);
            var saved=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(module.name));Require(saved!=null,"Prefab save failed");return saved;
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    static void VerifyModule(GameObject root,ModuleSpec s)
    {
        var b=BoundsOf(root);var min=V(s.bounds_min);var max=V(s.bounds_max);
        Require((b.min-min).magnitude<.002f && (b.max-max).magnitude<.002f,s.name+" bounds "+b.min.ToString("F4")+" / "+b.max.ToString("F4")+" expected "+min+" / "+max);
        Require(root.GetComponentsInChildren<BoxCollider>(true).Length==s.colliders.Length,s.name+" collider count");
    }
    [MenuItem("Tools/Safety/Maintenance Room/Rebuild Assets")]
    public static void Build()
    {
        var spec=Read();EnsureFolder(Folder+"/Prefabs/Modules");EnsureFolder(Folder+"/Scenes");
        var materials=CreateMaterials(spec);var modules=new Dictionary<string,GameObject>();
        foreach(var s in spec.modules)modules[s.name]=MakeModule(s,materials);
        ConfigureImport(Folder+"/Models/MaintenanceRoom_Assembled.fbx",materials);
        var room=new GameObject("MaintenanceRoom");
        try
        {
            var groups=new Dictionary<string,Transform>();
            foreach(var item in spec.instances)
            {
                if(!groups.TryGetValue(item.group,out var parent)){parent=new GameObject(item.group).transform;parent.SetParent(room.transform,false);groups[item.group]=parent;}
                var go=(GameObject)PrefabUtility.InstantiatePrefab(modules[item.module]);go.name=item.name;go.transform.SetParent(parent,false);
                go.transform.localPosition=V(item.position);go.transform.localRotation=Rotation(item.rotation);
            }
            PrefabUtility.SaveAsPrefabAsset(room,RoomPath);
        }
        finally{UnityEngine.Object.DestroyImmediate(room);}
        AssetDatabase.SaveAssets();Validate();CreatePreviewScene();AssetDatabase.SaveAssets();
        Debug.Log("MAINTENANCE_ROOM_BUILD_PASSED");
    }
    [MenuItem("Tools/Safety/Maintenance Room/Validate Assets")]
    public static void Validate()
    {
        var spec=Read();var asset=AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath);Require(asset!=null,"Room prefab missing");
        foreach(var s in spec.modules)
        {
            var a=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(s.name));Require(a!=null,"Module missing "+s.name);
            var test=(GameObject)PrefabUtility.InstantiatePrefab(a);try{VerifyModule(test,s);}finally{UnityEngine.Object.DestroyImmediate(test);}
        }
        var room=(GameObject)PrefabUtility.InstantiatePrefab(asset);
        try
        {
            Require(room.transform.position==Vector3.zero && room.transform.rotation==Quaternion.identity && room.transform.localScale==Vector3.one,"Room root transform");
            var rs=room.GetComponentsInChildren<MeshRenderer>(true);var filters=room.GetComponentsInChildren<MeshFilter>(true);
            int triangles=filters.Sum(f=>(int)Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(long)f.sharedMesh.GetIndexCount(i))/3);
            Require(triangles<50000,"Triangle budget");
            foreach(var r in rs)Require(r.sharedMaterials.All(m=>m!=null && m.shader.name=="Universal Render Pipeline/Lit" && AssetDatabase.GetAssetPath(m).StartsWith(Folder+"/Materials/")),"Bad material "+r.name);
            foreach(var f in filters){Require(f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0),"UV missing");Require(f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1),"Lightmap UV missing");}
            foreach(var t in room.GetComponentsInChildren<Transform>(true))
            {
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"Missing script");
                Require(!new[]{"ladder","helmet","glove","flashlight","circuit","supplybox","marking","sign"}.Any(s=>t.name.ToLowerInvariant().Contains(s)),"Excluded content: "+t.name);
            }
            Require(room.GetComponentsInChildren<Rigidbody>().Length==0,"Unexpected rigidbody");
            int instances=0;
            foreach(Transform group in room.transform)foreach(Transform module in group)
            {
                instances++;Require(PrefabUtility.GetCorrespondingObjectFromSource(module.gameObject)!=null,"Unlinked module");
            }
            Require(instances==spec.instances.Length,"Instance count");
            Require(room.transform.Find("Floor").childCount==20,"Floor coverage");
            var hinge=Find(room.transform,"MR_DoorHinge");
            Require(hinge.GetComponentsInChildren<MeshRenderer>().Length>0 && hinge.GetComponentsInChildren<Collider>().Length==1,"Door hinge geometry/collider");
            Require(Find(room.transform,"MR_CabinetHingeLeft")!=null && Find(room.transform,"MR_CabinetHingeRight")!=null,"Cabinet hinges");
            Physics.SyncTransforms();var colliders=room.GetComponentsInChildren<Collider>();
            // Check actual collider geometry by points, not aggregate bounds; legs must leave the underside open.
            foreach(var point in new[]{new Vector3(0,.4f,0),new Vector3(0,1.6f,0),new Vector3(-2.16f,.4f,.05f),new Vector3(-2.16f,.86f,.05f)})
                Require(!colliders.Any(c=>(c.ClosestPoint(point)-point).sqrMagnitude<.000001f),"Clearance blocked at "+point);
            var entrancePoint=new Vector3(-2.5f,1,-1.5f);
            var doorCollider=hinge.GetComponentInChildren<Collider>();
            Require(!colliders.Where(c=>c!=doorCollider).Any(c=>(c.ClosestPoint(entrancePoint)-entrancePoint).sqrMagnitude<.000001f),"Doorway blocked by shell");
            var before=doorCollider.bounds.center;var old=hinge.localRotation;hinge.Rotate(Vector3.up,75,Space.World);Physics.SyncTransforms();
            Require(Vector3.Distance(before,doorCollider.bounds.center)>.2f,"Door collider does not follow hinge");
            Require((doorCollider.ClosestPoint(entrancePoint)-entrancePoint).sqrMagnitude>.01f,"Opened door blocks entrance");hinge.localRotation=old;
            var raw=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Models/MaintenanceRoom_Assembled.fbx");Require(raw!=null,"Assembled FBX missing");
            Require(raw.GetComponentsInChildren<MeshRenderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null && m.shader.name=="Universal Render Pipeline/Lit")),"Raw assembly material lost");
            var report=new ValidationReport{status="passed",unity_version=Application.unityVersion,modules=spec.modules.Length,instances=instances,triangles=triangles,mesh_renderers=rs.Length,colliders=colliders.Length,materials=spec.materials.Length,module_bounds=true,uvs=true,lightmap_uvs=true,hinges=true,clear_floor_and_table=true,entrance=true,shared_prefabs=true,no_missing_scripts=true,assembled_fbx_materials=true};
            File.WriteAllText(Path.Combine(Output,"unity-validation.json"),JsonUtility.ToJson(report,true));Debug.Log("MAINTENANCE_ROOM_VALIDATION_PASSED "+JsonUtility.ToJson(report));
        }
        finally{UnityEngine.Object.DestroyImmediate(room);}
    }
    static void CreatePreviewScene()
    {
        var previous=SceneManager.GetActiveScene();
        // Batch mode starts with an untitled scratch scene, which Unity forbids opening additively.
        // Interactive rebuilds preserve the user's existing scene setup.
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        try
        {
            var room=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath),scene);
            // Scene opens with a complete room; camera is inside it. Cutaway is a separate Blender preview.
            var camera=new GameObject("Preview Camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.transform.position=new Vector3(.8f,1.58f,-1.83f);camera.transform.LookAt(new Vector3(-.3f,1.85f,1.5f));camera.fieldOfView=80;camera.nearClipPlane=.03f;camera.farClipPlane=30;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.18f);
            var illumination=new GameObject("Preview Lighting");
            foreach(var p in new[]{new Vector3(-1.2f,2.2f,0),new Vector3(1.2f,2.2f,0)})
            {
                var light=new GameObject("Interior Fill").AddComponent<Light>();light.transform.SetParent(illumination.transform,false);light.transform.position=p;
                light.type=LightType.Point;light.color=new Color(1,.91f,.78f);light.intensity=1;light.range=7;light.shadows=LightShadows.Soft;
            }
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.38f,.42f);RenderSettings.ambientIntensity=1;
            Require(EditorSceneManager.SaveScene(scene,Folder+"/Scenes/MaintenanceRoom_Preview.unity"),"Preview scene save failed");
        }
        finally{if(previous.IsValid() && previous.isLoaded){SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}}
    }
}
