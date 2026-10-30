using System;
using System.IO;
using System.Linq;
using DeliveryRider;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class SidewalkStage02Review {
 public static string Run(){
  var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Edit Myeongji scene required");var roots=scene.GetRootGameObjects();var map=roots.Single(g=>g.name=="MyeongjiMap");var floor=map.transform.Find("Ground").GetComponentInChildren<MeshCollider>();var session=roots.Single(g=>g.name=="DeliverySession").GetComponent<DeliverySession>();var so=new SerializedObject(session);var first=(Transform)so.FindProperty("doorPoints").GetArrayElementAtIndex(0).objectReferenceValue;var resident=(DeliveryResident)so.FindProperty("recipient").objectReferenceValue;resident.transform.SetPositionAndRotation(first.position+Vector3.up*so.FindProperty("recipientHeight").floatValue,first.rotation);Physics.SyncTransforms();
  bool Solid(Collider c)=>c!=floor&&c.attachedRigidbody==null&&!c.isTrigger;
  int anchors=0;foreach(var row in JArray.Parse(File.ReadAllText("Docs/MapLayout/Myeongji/anchors.json")).Where(r=>((string)r["building"]).StartsWith("A"))){var p=row["position"];var point=new Vector3((float)p[0],(float)p[1],(float)p[2]);if(Physics.OverlapCapsule(point+Vector3.up*.65f,point+Vector3.up*1.5f,.5f,~0,QueryTriggerInteraction.Ignore).Any(Solid))throw new Exception("Blocked anchor "+row["name"]);anchors++;}
  var traffic=roots.Single(g=>g.name=="Traffic").GetComponent<TrafficNetwork>();int trafficProbes=0;
  for(int i=0;i<traffic.PathCount;i++){var p=traffic.Path(i);for(int j=1;j<p.points.Length;j++){var a=p.points[j-1];var b=p.points[j];var middle=(a+b)*.5f;if(middle.x>30||middle.z<-57)continue;trafficProbes++;if(Physics.OverlapBox(middle+Vector3.up*1.2f,new Vector3(1.02f,.85f,2.5f),Quaternion.LookRotation(b-a),~0,QueryTriggerInteraction.Ignore).Any(Solid))throw new Exception("Traffic obstruction at "+middle);}}
  var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Myeongji/Models/Ground.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
  double Area(Mesh mesh){var v=mesh.vertices;var ids=mesh.triangles;double result=0;for(int i=0;i<ids.Length;i+=3)result+=Vector3.Cross(v[ids[i+1]]-v[ids[i]],v[ids[i+2]]-v[ids[i]]).magnitude*.5;return result;}
  double areaDelta=Area(floor.sharedMesh)-Area(source);if(Math.Abs(areaDelta)>.1f)throw new Exception("Ground surface area differs");
  int missing=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));if(missing>0)throw new Exception("Missing scripts");
  var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();int valid=0;
  var data=AssetDatabase.LoadAssetAtPath<HazardCandidates>("Assets/_Game/Settings/MyeongjiHazardCandidates.asset");
  foreach(var c in data.Candidates.Where(c=>c.type=="muscleman"&&c.position.x>3&&c.position.x<24.5f&&c.position.z>-48.5f&&c.position.z<-4)){
   foreach(float sign in new[]{1f,-1f})if(!MusclemanHazard.TryRoute(net,c.position,c.rotation*Vector3.forward*sign,20f,.75f,out _,out _,20f))throw new Exception("Invalid A event approach");valid++;
  }
  EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  var report=new JObject{{"anchorsChecked",anchors},{"trafficProbes",trafficProbes},{"areaDelta",areaDelta},{"missingScripts",missing},{"AEventCandidates",valid},{"playTest",false}};File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage02_review.json",report.ToString());return report.ToString();
 }
}
