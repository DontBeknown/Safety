using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Rebuilds only the generated flashlight asset. Put gameplay in a prefab variant.</summary>
public static class FlashlightAssetBuilder
{
    const string Folder = "Assets/Art/Equipment/Flashlight";
    const string ModelPath = Folder + "/flashlight.fbx";
    const string PrefabPath = Folder + "/Flashlight.prefab";
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,"../../../Art/Models/Flashlight"));
    static readonly string[] Names = { "Yellow", "Graphite", "Rubber", "Metal", "Lens" };
    static readonly Color[] Colors = { new Color32(250,204,20,255), new Color32(55,65,81,255), new Color32(24,24,27,255), new Color32(192,192,192,255), new Color(235/255f,243/255f,1,.18f) };
    static readonly float[] Roughness = { .46f,.60f,.86f,.32f,.15f };
    static Transform Find(GameObject root,string name) => root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
    static void Require(bool condition,string message) { if(!condition) throw new Exception("Flashlight: "+message); }

    [MenuItem("Tools/Safety/Rebuild Flashlight Prefab")]
    public static void Build()
    {
        AssetDatabase.ImportAsset(ModelPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        Require(importer!=null,"FBX missing");
        importer.globalScale=1; importer.useFileScale=true; importer.bakeAxisConversion=true;
        importer.importAnimation=false; importer.importCameras=false; importer.importLights=false;
        importer.isReadable=false; importer.meshCompression=ModelImporterMeshCompression.Off;
        importer.importNormals=ModelImporterNormals.Import;
        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        if(!AssetDatabase.IsValidFolder(Folder+"/Materials")) AssetDatabase.CreateFolder(Folder,"Materials");
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        Require(shader!=null,"URP Lit unavailable");
        var materials=new Material[Names.Length];
        for(int i=0;i<Names.Length;i++)
        {
            var path=Folder+"/Materials/FL_"+Names[i]+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) { m=new Material(shader); AssetDatabase.CreateAsset(m,path); }
            m.shader=shader; m.SetColor("_BaseColor",Colors[i]);
            m.SetFloat("_Metallic",i==3?.72f:0); m.SetFloat("_Smoothness",1-Roughness[i]);
            m.SetFloat("_Surface",i==4?1:0); m.SetFloat("_Blend",0);
            m.SetFloat("_Cull",(float)CullMode.Back); m.SetFloat("_AlphaClip",0);
            m.SetColor("_EmissionColor",Color.black); m.DisableKeyword("_EMISSION");
            BaseShaderGUI.SetMaterialKeywords(m);
            EditorUtility.SetDirty(m); materials[i]=m;
        }
        foreach(var source in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
        {
            int i=Array.FindIndex(Names,n=>source.name=="FL_"+n);
            Require(i>=0,"Unexpected material "+source.name);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source),materials[i]);
        }
        importer.SaveAndReimport();
        var preview=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Flashlight"); SceneManager.MoveGameObjectToScene(root,preview);
        try
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath),preview);
            model.name="Geometry"; model.transform.SetParent(root.transform,false);
            var origin=Find(model,"FL_GripAnchor").position;
            var forward=(Find(model,"FL_BeamOrigin").position-origin).normalized;
            var up=(Find(model,"FL_ButtonUpAnchor").position-origin).normalized;
            model.transform.rotation=Quaternion.Inverse(Quaternion.LookRotation(forward,up))*model.transform.rotation;
            model.transform.position-=Find(model,"FL_GripAnchor").position;
            foreach(var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterials=r.sharedMaterials.Select(m=> {
                    int i=Array.FindIndex(Names,n=>m!=null && m.name=="FL_"+n);
                    Require(i>=0,"Unmapped material on "+r.name); return materials[i];
                }).ToArray();
                if(r.name=="FL_Lens") r.shadowCastingMode=ShadowCastingMode.Off;
            }
            // Public anchors are root children with an explicit Unity +Z forward basis.
            foreach(var name in new[]{"GripAnchor","BeamOrigin"})
            {
                var imported=Find(model,"FL_"+name);
                var anchor=new GameObject(name).transform; anchor.SetParent(root.transform,false);
                anchor.position=imported.position; anchor.localRotation=Quaternion.identity;
            }
            Find(model,"FL_Lens").name="Lens";
            Find(model,"FL_Button").name="Button";
            var body=root.AddComponent<BoxCollider>(); body.center=new Vector3(0,0,-.005f); body.size=new Vector3(.032f,.032f,.140f);
            var headObject=new GameObject("HeadCollider"); headObject.transform.SetParent(root.transform,false);
            var head=headObject.AddComponent<BoxCollider>(); head.center=new Vector3(0,0,.085f); head.size=new Vector3(.050f,.050f,.040f);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        AssetDatabase.SaveAssets(); Validate();
    }

    [Serializable]
    class ValidationReport
    {
        public string status="passed";
        public string unity_version;
        public int triangles;
        public int mesh_renderers;
        public Vector3 dimensions_xyz_m;
        public string materials_colliders_uvs_anchors="passed";
    }

    [MenuItem("Tools/Safety/Validate Flashlight Prefab")]
    public static void Validate()
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(asset!=null,"Prefab missing");
        var scene=EditorSceneManager.NewPreviewScene();
        var root=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
        try
        {
            Require(root.transform.localPosition==Vector3.zero && root.transform.localScale==Vector3.one && Quaternion.Angle(root.transform.localRotation,Quaternion.identity)<.001f,"Root transform");
            var renderers=root.GetComponentsInChildren<MeshRenderer>();
            Require(renderers.Length==11,"Expected 11 mesh renderers");
            var bounds=renderers[0].bounds;
            foreach(var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                Require(r.sharedMaterials.All(m=>m!=null && m.shader.name=="Universal Render Pipeline/Lit"),"Invalid materials on "+r.name);
            }
            Require((bounds.size-new Vector3(.05f,.05f,.18f)).magnitude<.00002f,"Dimensions "+bounds.size.ToString("F6"));
            Require(Mathf.Abs(bounds.min.z+.075f)<.00001f && Mathf.Abs(bounds.max.z-.105f)<.00001f,"Grip pivot/longitudinal extents");
            var grip=Find(root,"GripAnchor"); var beam=Find(root,"BeamOrigin");
            Require(grip.parent==root.transform && grip.localPosition.magnitude<1e-6f,"Grip anchor");
            Require(beam.parent==root.transform && (beam.localPosition-new Vector3(0,0,.105f)).magnitude<1e-6f && Vector3.Dot(beam.forward,Vector3.forward)>.9999f,"Beam anchor");
            Require(Find(root,"Button").position.y>.015f,"Button not on +Y side");
            var lens=Find(root,"Lens").GetComponent<MeshRenderer>();
            Require(Mathf.Abs(lens.bounds.max.z-.105f)<1e-5f,"Lens plane");
            var mats=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            Require(mats.Length==5,"Five materials required");
            for(int i=0;i<Names.Length;i++)
            {
                var m=mats.Single(x=>x.name=="FL_"+Names[i]);
                Require(Vector4.Distance(m.GetColor("_BaseColor"),Colors[i])<.001f,"Color swatch "+Names[i]);
                Require(!m.IsKeywordEnabled("_EMISSION"),"Unexpected emission");
                Require(m.GetFloat("_Surface")== (i==4?1:0),"Surface mode "+Names[i]);
            }
            var lm=lens.sharedMaterial;
            Require(lm.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") && lm.renderQueue==3000 && lm.GetFloat("_ZWrite")==0,"Lens transparency setup");
            Require(lens.shadowCastingMode==ShadowCastingMode.Off,"Lens casts opaque shadow");
            var colliders=root.GetComponentsInChildren<Collider>();
            Require(colliders.Length==2 && colliders.All(c=>c is BoxCollider && !c.isTrigger),"Primitive colliders");
            var cb=colliders[0].bounds; cb.Encapsulate(colliders[1].bounds);
            Require((cb.min-bounds.min).magnitude<.00002f && (cb.max-bounds.max).magnitude<.00002f,"Collider envelope");
            Require(root.GetComponentsInChildren<Rigidbody>().Length==0 && root.GetComponentsInChildren<Light>().Length==0,"Unexpected physics/light");
            Require(root.GetComponentsInChildren<MonoBehaviour>().Length==0,"Unexpected gameplay or missing script");
            int tris=0;
            foreach(var f in root.GetComponentsInChildren<MeshFilter>())
            {
                var m=f.sharedMesh;
                Require(m!=null && m.HasVertexAttribute(VertexAttribute.TexCoord0) && m.HasVertexAttribute(VertexAttribute.Normal),"Missing UV/normals "+f.name);
                for(int i=0;i<m.subMeshCount;i++) { Require(m.GetTopology(i)==MeshTopology.Triangles,"Non-triangle mesh"); tris+=(int)m.GetIndexCount(i)/3; }
            }
            Require(tris==2124 && tris<3000,"Triangle count "+tris);
            var report=new ValidationReport { unity_version=Application.unityVersion,triangles=tris,mesh_renderers=renderers.Length,dimensions_xyz_m=bounds.size };
            Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output,"unity-validation.json"),JsonUtility.ToJson(report,true));
            Debug.Log("FLASHLIGHT_VALIDATION_PASSED "+JsonUtility.ToJson(report));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/Safety/Render Flashlight Preview")]
    public static void RenderPreview()
    {
        Validate();
        var scene=EditorSceneManager.NewPreviewScene();
        RenderTexture rt=null; Texture2D image=null;
        var previous=RenderTexture.active;
        try
        {
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
            var co=new GameObject("PreviewCamera"); SceneManager.MoveGameObjectToScene(co,scene);
            var camera=co.AddComponent<Camera>(); camera.scene=scene;
            camera.transform.position=new Vector3(.24f,.20f,.28f); camera.transform.LookAt(new Vector3(0,0,.015f));
            camera.orthographic=true; camera.orthographicSize=.095f; camera.nearClipPlane=.001f; camera.farClipPlane=10;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.18f,.21f,.25f);
            camera.allowHDR=false; camera.allowMSAA=false;
            var angles=new[]{new Vector3(35,-35,0),new Vector3(20,135,0),new Vector3(-30,60,0)};
            for(int i=0;i<angles.Length;i++)
            {
                var go=new GameObject("PreviewLight"); SceneManager.MoveGameObjectToScene(go,scene);
                var l=go.AddComponent<Light>(); l.type=LightType.Directional; l.intensity=i==0?1.3f:.5f;
                go.transform.rotation=Quaternion.Euler(angles[i]);
            }
            rt=new RenderTexture(1200,900,24,RenderTextureFormat.ARGB32); rt.Create(); camera.targetTexture=rt;
            camera.Render(); RenderTexture.active=rt;
            image=new Texture2D(1200,900,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1200,900),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output,"flashlight-unity-preview.png"),image.EncodeToPNG());
            Debug.Log("FLASHLIGHT_PREVIEW_RENDERED");
        }
        finally
        {
            RenderTexture.active=previous;
            EditorSceneManager.ClosePreviewScene(scene);
            if(rt!=null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if(image!=null) UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
