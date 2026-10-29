using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;

public static class MyeongjiMapImport
{
    const string Art="Assets/_Game/Art/Myeongji";
    const string Source="C:/Users/PC/Documents/Codex/Myeongji_District/UnityExport";
    const string Original="Assets/_Game/Scenes/FourDistrictPrototype.unity";
    const string Target="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity";
    static readonly string[] Groups={"Ground","Buildings0","Buildings1","Buildings2","Buildings3","Markings","Furniture"};
    static void Guard(){if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("Stop Play Mode / compilation first.");}
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
    static string Safe(string name)=>string.Concat(name.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_'));
    static Material MakeMaterial(JToken item)
    {
        string path=Art+"/Materials/"+Safe((string)item["name"])+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=(string)item["name"];AssetDatabase.CreateAsset(m,path);}
        var a=item["linearColor"].Values<float>().ToArray();var c=new Color(a[0],a[1],a[2],a[3]);m.SetColor("_BaseColor",c.gamma);m.SetFloat("_Smoothness",.12f);m.SetFloat("_Metallic",0);
        float strength=(float)item["emissionStrength"];
        if(strength>0){var e=item["emission"].Values<float>().ToArray();m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(e[0],e[1],e[2])*strength);}else{m.DisableKeyword("_EMISSION");m.SetColor("_EmissionColor",Color.black);}
        EditorUtility.SetDirty(m);return m;
    }
    public static string Import(int first,int last)
    {
        Guard();Folder(Art+"/Materials");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        for(int i=first;i<=last;i++)
        {
            string group=Groups[i];var data=JObject.Parse(File.ReadAllText(Source+"/"+group+".json"));var mi=(ModelImporter)AssetImporter.GetAtPath(Art+"/Models/"+group+".fbx");
            if(mi==null)throw new Exception("Missing model "+group);
            mi.globalScale=1;mi.useFileScale=true;mi.importAnimation=false;mi.importCameras=false;mi.importLights=false;mi.isReadable=true;mi.addCollider=false;
            mi.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;mi.importNormals=ModelImporterNormals.Import;
            foreach(var item in data["materials"])mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),(string)item["name"]),MakeMaterial(item));
            mi.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();return "Imported "+first+".."+last;
    }
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    public static object Inspect()
    {
        Guard();var results=new List<object>();
        foreach(string g in new[]{"Ground","Buildings0"})
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Models/"+g+".fbx");var go=UnityEngine.Object.Instantiate(source);
            foreach(var r in go.GetComponentsInChildren<MeshRenderer>())results.Add(new{name=r.name,center=V(r.bounds.center),size=V(r.bounds.size),scale=V(r.transform.lossyScale)});
            UnityEngine.Object.DestroyImmediate(go);
        }
        return Newtonsoft.Json.JsonConvert.SerializeObject(results);
    }
    static Scene TargetScene()
    {
        Guard();var scene=SceneManager.GetActiveScene();
        if(scene.path!=Target)throw new Exception("Open the generated Myeongji scene first.");
        return scene;
    }
    public static string BeginScene()
    {
        Guard();var current=SceneManager.GetActiveScene();
        if(current.isDirty)throw new Exception("Unsaved scene changes; keep them before opening the copy.");
        if(AssetDatabase.LoadAssetAtPath<SceneAsset>(Target)!=null)throw new Exception("Target already exists; use AddGroups to continue.");
        if(!AssetDatabase.CopyAsset(Original,Target))throw new Exception("Could not copy scene.");
        var scene=EditorSceneManager.OpenScene(Target,OpenSceneMode.Single);
        var disable=new[]{"NeighborhoodBuildings","FourDistrictWorld","Roads","Traffic","Pedestrians","Hazards","DeliverySession","People","ActiveDestination","SceneManager","UI"};
        foreach(var go in scene.GetRootGameObjects())if(disable.Contains(go.name))go.SetActive(false);
        var player=scene.GetRootGameObjects().Single(g=>g.name=="Player");
        foreach(var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
        var rb=player.GetComponent<Rigidbody>();if(rb!=null)rb.isKinematic=true;
        player.transform.SetPositionAndRotation(new Vector3(13.5f,.9f,-2f),Quaternion.Euler(0,90,0));
        var recovery=player.GetComponent<RiderSafeRecovery>();
        if(recovery!=null){var so=new SerializedObject(recovery);so.FindProperty("network").objectReferenceValue=null;so.ApplyModifiedPropertiesWithoutUndo();}
        var camera=scene.GetRootGameObjects().Single(g=>g.name=="Main Camera");
        foreach(var behaviour in camera.GetComponents<ThirdPersonCamera>())behaviour.enabled=false;
        camera.transform.SetPositionAndRotation(new Vector3(24,8,7),Quaternion.Euler(25,220,0));
        var root=new GameObject("MyeongjiMap");root.transform.rotation=Quaternion.Euler(0,180,0);
        EditorSceneManager.SaveScene(scene);
        return "Created map staging scene; legacy gameplay disabled until coordinates are migrated.";
    }
    public static string AddGroups(int first,int last)
    {
        var scene=TargetScene();var root=scene.GetRootGameObjects().Single(g=>g.name=="MyeongjiMap");
        for(int i=first;i<=last;i++)
        {
            string group=Groups[i];if(root.transform.Find(group)!=null)continue;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Models/"+group+".fbx");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);go.name=group;
            go.transform.SetParent(root.transform,false);
            if(group=="Ground"||group.StartsWith("Buildings"))
                foreach(var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {var mc=mf.gameObject.AddComponent<MeshCollider>();mc.sharedMesh=mf.sharedMesh;mc.convex=false;mf.gameObject.isStatic=true;}
            if(group=="Furniture")
            {
                var data=JObject.Parse(File.ReadAllText(Source+"/Furniture.json"));
                var meshes=go.GetComponentsInChildren<MeshFilter>(true);
                foreach(var item in data["objects"])
                {
                    string name=(string)item["name"];
                    var mf=meshes.SingleOrDefault(m=>m.name==name||m.name.StartsWith(name+"."));
                    if(mf==null)throw new Exception("Missing furniture "+name);
                    if(name.Contains("Green_Walk"))mf.gameObject.SetActive(false);
                    if(item["baseXY"].Count()==2)
                    {
                        var box=new GameObject(name+"_PostCollider");box.transform.SetParent(go.transform,false);
                        box.transform.localPosition=new Vector3(-(float)item["baseXY"][0],1.5f,-(float)item["baseXY"][1]);
                        var col=box.AddComponent<BoxCollider>();col.size=new Vector3(.4f,3,.4f);
                    }
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Saved groups "+first+".."+last;
    }
    public static string FinishScene()
    {
        var scene=TargetScene();var root=scene.GetRootGameObjects().Single(g=>g.name=="MyeongjiMap");
        if(root.transform.childCount!=7)throw new Exception("All seven groups are required.");
        Folder("Assets/_Game/Prefabs/Myeongji");
        PrefabUtility.SaveAsPrefabAssetAndConnect(root,"Assets/_Game/Prefabs/Myeongji/MyeongjiMap.prefab",InteractionMode.AutomatedAction);
        var player=scene.GetRootGameObjects().Single(g=>g.name=="Player");
        Physics.SyncTransforms();
        var ground=root.transform.Find("Ground").GetComponentInChildren<MeshCollider>();
        if(!ground.Raycast(new Ray(new Vector3(13.5f,20,-2),Vector3.down),out var hit,30))throw new Exception("Spawn has no ground.");
        var capsule=player.GetComponent<CapsuleCollider>();
        player.transform.position=new Vector3(13.5f,hit.point.y+capsule.height*player.transform.lossyScale.y/2-capsule.center.y+.04f,-2);
        var cam=scene.GetRootGameObjects().Single(g=>g.name=="Main Camera");
        cam.transform.position=new Vector3(24,7.5f,6);cam.transform.LookAt(new Vector3(13.5f,3,-6));
        var sv=SceneView.lastActiveSceneView;
        if(sv!=null)sv.LookAtDirect(new Vector3(57.5f,0,-50.5f),Quaternion.Euler(55,25,0),100);
        Selection.activeGameObject=root;
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return Audit();
    }
    public static string Audit()
    {
        var scene=TargetScene();var root=scene.GetRootGameObjects().Single(g=>g.name=="MyeongjiMap");
        var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
        int missing=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        var ground=root.transform.Find("Ground").GetComponentInChildren<Renderer>();
        var player=scene.GetRootGameObjects().Single(g=>g.name=="Player");
        Physics.SyncTransforms();
        var cp=player.transform.position;var overlaps=Physics.OverlapCapsule(cp+Vector3.up*.5f,cp-Vector3.up*.5f,.48f).Where(c=>!c.transform.IsChildOf(player.transform)).Select(c=>c.name).ToArray();
        return Newtonsoft.Json.JsonConvert.SerializeObject(new{
            path=scene.path,groundSize=V(ground.bounds.size),mapMeshes=renderers.Length,
            buildings=root.GetComponentsInChildren<MeshCollider>(true).Count(c=>c.transform.parent!=null&&c.transform.parent.name.StartsWith("Buildings")),
            colliders=root.GetComponentsInChildren<Collider>(true).Length,
            missingScripts=missing,invalidMaterials=renderers.Sum(r=>r.sharedMaterials.Count(m=>m==null||m.shader==null||m.shader.name!="Universal Render Pipeline/Lit")),
            greenOff=renderers.Count(r=>r.name.Contains("Green_Walk")&&!r.gameObject.activeSelf),
            playerPosition=V(cp),spawnOverlaps=overlaps,
            disabledRoots=scene.GetRootGameObjects().Where(g=>!g.activeSelf).Select(g=>g.name).ToArray(),saved=!scene.isDirty});
    }

    public static string Polish()
    {
        var scene=TargetScene();
        foreach(var g in Groups)foreach(var m in JObject.Parse(File.ReadAllText(Source+"/"+g+".json"))["materials"])MakeMaterial(m);
        AssetDatabase.SaveAssets();
        var root=scene.GetRootGameObjects().Single(g=>g.name=="MyeongjiMap");
        SceneVisibilityManager.instance.Show(root,true);
        var sv=SceneView.lastActiveSceneView;
        if(sv!=null){sv.in2DMode=false;sv.sceneLighting=true;sv.cameraMode=SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);sv.LookAtDirect(new Vector3(57.5f,0,-50.5f),Quaternion.Euler(55,25,0),100);sv.Repaint();}
        EditorSceneManager.SaveScene(scene);
        return "Blender linear colors converted to Unity base-color values; view restored.";
    }
}
