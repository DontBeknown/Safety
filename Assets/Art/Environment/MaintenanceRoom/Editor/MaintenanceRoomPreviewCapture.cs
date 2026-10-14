using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Optional batch-only visual QA. Never saves its temporary cutaway scene changes.
public static class MaintenanceRoomPreviewCapture
{
    public static void Capture()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Run Capture in a separate Unity batch process to preserve open scenes.");
        MaintenanceRoomBuilder.Validate();
        EditorSceneManager.OpenScene("Assets/Art/Environment/MaintenanceRoom/Scenes/MaintenanceRoom_Preview.unity",OpenSceneMode.Single);
        var camera=GameObject.Find("Preview Camera").GetComponent<Camera>();
        Render(camera,"unity-room-interior.png");
        var root=GameObject.Find("MaintenanceRoom").transform;
        root.Find("FrontWall").gameObject.SetActive(false);root.Find("RightWall").gameObject.SetActive(false);
        foreach(Transform module in root.Find("Ceiling"))if(module.name.StartsWith("Placed_CeilingTile"))module.gameObject.SetActive(false);
        camera.transform.position=new Vector3(7,7,-10);camera.transform.LookAt(new Vector3(0,1.15f,0));camera.orthographic=true;camera.orthographicSize=4.25f;
        var key=new GameObject("Capture Key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.5f;key.color=new Color(1,.9f,.77f);key.transform.rotation=Quaternion.Euler(45,-30,0);key.shadows=LightShadows.Soft;
        Render(camera,"unity-room-cutaway.png");
        Debug.Log("MAINTENANCE_ROOM_CAPTURE_PASSED");
    }
    static void Render(Camera camera,string name)
    {
        var target=new RenderTexture(1440,1080,24,RenderTextureFormat.ARGB32);target.Create();
        var texture=new Texture2D(1440,1080,TextureFormat.RGB24,false);
        var old=RenderTexture.active;
        try
        {
            var request=new RenderPipeline.StandardRequest{destination=target};
            RenderPipeline.SubmitRenderRequest(camera,request);
            RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1440,1080),0,0);texture.Apply();
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../Art/Models/MaintenanceRoom",name));
            File.WriteAllBytes(output,texture.EncodeToPNG());
        }
        finally{RenderTexture.active=old;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(texture);}
    }
}
