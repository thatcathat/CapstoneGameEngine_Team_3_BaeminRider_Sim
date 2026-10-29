using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiGameplay
{
 const string Target="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity";
 const string Data="Docs/MapLayout/Myeongji";
 static Scene Scene()=>SceneManager.GetActiveScene();
 static GameObject Root(string n)=>Scene().GetRootGameObjects().Single(g=>g.name==n);
 static void Guard(){if(EditorApplication.isPlaying||EditorApplication.isCompiling||Scene().path!=Target)throw new Exception("Open Myeongji in Edit mode.");}
 static void Save(){EditorSceneManager.MarkSceneDirty(Scene());EditorSceneManager.SaveScene(Scene());AssetDatabase.SaveAssets();}
 static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
 static void SetRef(Component c,string n,UnityEngine.Object value){var so=new SerializedObject(c);so.FindProperty(n).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
 static Vector3 P(JToken a)=>new Vector3((float)a[0],0,(float)a[1]);
 static Vector3 Project(Vector3 p,Vector3 a,Vector3 b){var d=b-a;return a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude);}
 static MeshCollider Ground=>Root("MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();
 static Vector3 GroundPoint(Vector3 p){if(!Ground.Raycast(new Ray(p+Vector3.up*30,Vector3.down),out var hit,60))throw new Exception("No ground "+p);return hit.point;}
 static bool Clear(Vector3 p){return !Physics.OverlapCapsule(p+Vector3.up*.6f,p+Vector3.up*1.65f,.5f,1,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(Root("Player").transform));}
 public static string Prepare()
 {
  Guard();if(Scene().isDirty)throw new Exception("Save your scene edits first.");
  string backup="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji_LayoutBackup.unity";
  if(AssetDatabase.LoadAssetAtPath<SceneAsset>(backup)==null&&!AssetDatabase.CopyAsset(Target,backup))throw new Exception("Backup failed.");
  var layout=JObject.Parse(File.ReadAllText(Data+"/layout.json"));
  if(Scene().GetRootGameObjects().Any(g=>g.name=="MyeongjiLocations"))throw new Exception("Locations already exist; continue rather than duplicate them.");
  var routes=new GameObject("MyeongjiRoutes");var network=routes.AddComponent<RoadNetwork>();network.enabled=false;network.Configure(new Collider[]{Ground});
  foreach(var r in layout["roads"])
  {
   var go=new GameObject((string)r["id"]);go.transform.SetParent(routes.transform,false);
   var sc=go.AddComponent<SplineContainer>();sc.Spline=new Spline();
   foreach(string k in new[]{"a","b"}){var p=GroundPoint(P(r[k]));sc.Spline.Add(new BezierKnot(new float3(p.x,p.y,p.z)),TangentMode.Linear);}
   var rs=go.AddComponent<RoadSurface>();rs.Configure((float)r["width"]>=5?RoadKind.Main:RoadKind.Alley,(float)r["width"],0);
   rs.enabled=false;go.GetComponent<MeshRenderer>().enabled=false;
  }
  SetRef(Root("Player").GetComponent<RiderSafeRecovery>(),"network",network);
  var locations=new GameObject("MyeongjiLocations");
  var session=Root("DeliverySession").GetComponent<DeliverySession>();var oldTable=(DeliveryRouteTable)new SerializedObject(session).FindProperty("routeTable").objectReferenceValue;
  string[] shops={"A01","A02","B01","B03","E01","A06","B15","E06","E12","F03","G02","G04"};
  string[] doors={"A01","A02","A03","A04","A05","A06","A07","A01","B02","B04","B06","B08","B10","B11","B14","B17","E02","E03","E04","E07","E08","E10","E11","E13","F01","F02","F04","F05","F06","G01","G03","G05"};
  var records=new JArray();var used=new List<Vector3>();Physics.SyncTransforms();
  for(int type=0;type<3;type++)
  {
   var ids=type==0?shops:type==1?doors:new[]{"H01"};
   for(int i=0;i<ids.Length;i++)
   {
    string id=ids[i];var lot=layout["lots"].Single(l=>(string)l[0]==id);
    var renderer=Root("MyeongjiMap").GetComponentsInChildren<MeshRenderer>(true).Single(m=>m.name.StartsWith("AB01_"+id+"."));
    var bounds=renderer.bounds;float angle=(float)lot[7]*Mathf.Deg2Rad;
    var normal=new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle));var tangent=new Vector3(-normal.z,0,normal.x);
    float outward=Mathf.Abs(normal.x)*bounds.extents.x+Mathf.Abs(normal.z)*bounds.extents.z;
    float along=Mathf.Abs(tangent.x)*bounds.extents.x+Mathf.Abs(tangent.z)*bounds.extents.z;
    Vector3 best=Vector3.zero,bestRoad=Vector3.zero;string bestRoadId=null;float cost=float.PositiveInfinity;
    foreach(float shift in new[]{0f,-.32f,.32f,-.65f,.65f})foreach(float clearance in new[]{.85f,1.25f,1.8f})
    {
     var p=bounds.center+normal*(outward+clearance)+tangent*(along*shift);p.y=0;p=GroundPoint(p);
     if(!Clear(p)||used.Any(u=>Vector3.Distance(p,u)<2f))continue;
     foreach(var road in layout["roads"])
     {
      var q=Project(new Vector3(p.x,0,p.z),P(road["a"]),P(road["b"]));q=GroundPoint(q);
      var delta=q-p;delta.y=0;if(Vector3.Dot(delta,normal)<-.1f)continue;
      if(Physics.SphereCast(p+Vector3.up*1.05f,.45f,delta.normalized,out var hit,delta.magnitude,1,QueryTriggerInteraction.Ignore))continue;
      float score=delta.magnitude+Mathf.Abs(shift)*.2f+clearance*.05f;
      if(score<cost){cost=score;best=p;bestRoad=q;bestRoadId=(string)road["id"];}
     }
    }
    if(bestRoadId==null)throw new Exception("No accessible frontage for "+id+" type "+type);
    used.Add(best);string zone=type==0?oldTable.ShopZone(i):type==1?oldTable.DoorZone(i):"C";
    string name=type==0?zone+"_Shop_"+(i+1).ToString("00"):type==1?zone+"_Door_"+(i+1).ToString("00"):"C_Home";
    var point=new GameObject(name);point.transform.SetParent(locations.transform,false);point.transform.SetPositionAndRotation(best,Quaternion.LookRotation(normal));
    records.Add(new JObject{{"name",name},{"type",type==0?"shop":type==1?"door":"home"},{"index",i},{"zone",zone},{"building",id},{"position",new JArray(V(best))},{"forward",new JArray(V(normal))},{"road",bestRoadId},{"roadPoint",new JArray(V(bestRoad))},{"shopName",type==0?oldTable.ShopName(i):""}});
   }
  }
  File.WriteAllText(Data+"/anchors.json",records.ToString());
  Save();return "Saved 18 navigation splines and "+records.Count+" accessible points. Gameplay remains off until route/UI wiring finishes.";
 }
    public static string ConnectData()
    {
        Guard();var anchors=JArray.Parse(File.ReadAllText(Data+"/anchors.json"));var session=Root("DeliverySession").GetComponent<DeliverySession>();
        string tablePath="Assets/_Game/Settings/MyeongjiRouteTable.asset";
        var table=AssetDatabase.LoadAssetAtPath<DeliveryRouteTable>(tablePath);
        if(table==null){table=ScriptableObject.CreateInstance<DeliveryRouteTable>();AssetDatabase.CreateAsset(table,tablePath);}
        JsonUtility.FromJsonOverwrite(File.ReadAllText(Data+"/route_table.json"),table);EditorUtility.SetDirty(table);
        if(!table.IsValid)throw new Exception("Route table invalid.");
        var locations=Root("MyeongjiLocations").transform;var so=new SerializedObject(session);
        so.FindProperty("routeTable").objectReferenceValue=table;
        foreach(string kind in new[]{"shop","door"})
        {
            var points=anchors.Where(a=>(string)a["type"]==kind).ToArray();var array=so.FindProperty(kind=="shop"?"shopPoints":"doorPoints");array.arraySize=points.Length;
            for(int i=0;i<points.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=locations.Find((string)points[i]["name"]);
        }
        so.FindProperty("startZone").stringValue="C";so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string texturePath="Assets/_Game/Art/Myeongji/Map/MyeongjiMap.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        var map=Root("UI").GetComponentInChildren<DeliveryMapView>(true);var ms=new SerializedObject(map);
        ms.FindProperty("worldMinimum").vector2Value=new Vector2(-10,-110);ms.FindProperty("worldSize").vector2Value=new Vector2(135,119);ms.ApplyModifiedPropertiesWithoutUndo();
        var area=(RectTransform)ms.FindProperty("mapArea").objectReferenceValue;
        foreach(Transform child in area)if(child.name.StartsWith("Block_"))child.gameObject.SetActive(false);
        var bg=area.Find("MapGeometryBackground").GetComponent<UnityEngine.UI.RawImage>();bg.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);bg.color=Color.white;bg.raycastTarget=false;bg.gameObject.SetActive(true);bg.transform.SetAsFirstSibling();
        var rect=bg.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        var fitter=area.GetComponent<UnityEngine.UI.AspectRatioFitter>();if(fitter!=null)fitter.aspectRatio=135f/119f;
        string materialPath="Assets/_Game/Art/Myeongji/Materials/PickupPoint.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.92f,.65f,.16f));AssetDatabase.CreateAsset(mat,materialPath);}
        foreach(var a in anchors.Where(a=>(string)a["type"]=="shop"))
        {
            var point=locations.Find((string)a["name"]);if(point.Find("PickupMarker")!=null)continue;
            var marker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);marker.name="PickupMarker";marker.transform.SetParent(point,false);marker.transform.localPosition=new Vector3(0,.025f,0);marker.transform.localScale=new Vector3(1.1f,.018f,1.1f);UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());marker.GetComponent<Renderer>().sharedMaterial=mat;
        }
        var home=locations.Find("C_Home");var player=Root("Player");var cap=player.GetComponent<CapsuleCollider>();
        player.transform.position=home.position+Vector3.up*(cap.height*player.transform.lossyScale.y/2-cap.center.y+.04f);player.transform.rotation=home.rotation;
        var cam=Root("Main Camera");cam.transform.rotation=Quaternion.Euler(26,0,0);cam.transform.position=player.transform.position+Vector3.up-cam.transform.forward*7f;
        var resident=(DeliveryResident)so.FindProperty("recipient").objectReferenceValue;var firstDoor=locations.Find((string)anchors.First(a=>(string)a["type"]=="door")["name"]);resident.transform.SetPositionAndRotation(firstDoor.position+Vector3.up*so.FindProperty("recipientHeight").floatValue,firstDoor.rotation);
        Save();return "Saved route table, 12 pickups, 32 destinations, home spawn and new UI map. Gameplay activation follows.";
    }
    public static string Activate()
    {
        Guard();var player=Root("Player");
        foreach(var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=true;
        player.GetComponent<Rigidbody>().isKinematic=false;
        Root("Main Camera").GetComponent<ThirdPersonCamera>().enabled=true;
        var controller=Root("SceneManager").GetComponent<SceneController>();var cs=new SerializedObject(controller);cs.FindProperty("gameSceneName").stringValue="FourDistrictPrototype_Myeongji";cs.ApplyModifiedPropertiesWithoutUndo();
        var scenes=EditorBuildSettings.scenes.ToList();if(!scenes.Any(s=>s.path==Target))scenes.Add(new EditorBuildSettingsScene(Target,true));EditorBuildSettings.scenes=scenes.ToArray();
        foreach(string name in new[]{"SceneManager","DeliverySession","UI","People","ActiveDestination"})Root(name).SetActive(true);
        Save();return Review();
    }
    public static string Review()
    {
        Guard();Physics.SyncTransforms();var anchors=JArray.Parse(File.ReadAllText(Data+"/anchors.json"));
        var blocked=new List<string>();
        foreach(var a in anchors)
        {
            var p=Root("MyeongjiLocations").transform.Find((string)a["name"]).position;
            var hits=Physics.OverlapCapsule(p+Vector3.up*.6f,p+Vector3.up*1.65f,.5f,1,QueryTriggerInteraction.Ignore).Where(c=>c.attachedRigidbody==null).ToArray();
            if(hits.Length>0)blocked.Add((string)a["name"]+":"+string.Join(",",hits.Select(c=>c.name)));
        }
        var session=Root("DeliverySession").GetComponent<DeliverySession>();var so=new SerializedObject(session);var table=(DeliveryRouteTable)so.FindProperty("routeTable").objectReferenceValue;
        int missing=Scene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        return Newtonsoft.Json.JsonConvert.SerializeObject(new{scene=Scene().path,routeValid=table.IsValid,shops=table.ShopCount,doors=table.DoorCount,roads=Root("MyeongjiRoutes").transform.childCount,blockedAnchors=blocked,missingScripts=missing,playerMotor=Root("Player").GetComponent<RiderMotor>().enabled,playerDynamic=!Root("Player").GetComponent<Rigidbody>().isKinematic,cameraFollow=Root("Main Camera").GetComponent<ThirdPersonCamera>().enabled,disabledRoots=Scene().GetRootGameObjects().Where(g=>!g.activeSelf).Select(g=>g.name).ToArray(),saved=!Scene().isDirty,playMode=EditorApplication.isPlaying});
    }

}
