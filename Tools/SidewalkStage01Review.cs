using System;
using System.Linq;
using DeliveryRider;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class SidewalkStage01Review {
 public static string Run(){
  var s=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||s.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Edit scene required");var roots=s.GetRootGameObjects();var session=roots.Single(g=>g.name=="DeliverySession").GetComponent<DeliverySession>();var so=new SerializedObject(session);var first=(Transform)so.FindProperty("doorPoints").GetArrayElementAtIndex(0).objectReferenceValue;var resident=(DeliveryResident)so.FindProperty("recipient").objectReferenceValue;
  resident.transform.SetPositionAndRotation(first.position+Vector3.up*so.FindProperty("recipientHeight").floatValue,first.rotation);
  var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();
  int missing=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
  var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Myeongji/Models/Ground.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
  double Area(Mesh m){var v=m.vertices;var tris=m.triangles;double a=0;for(int i=0;i<tris.Length;i+=3)a+=Vector3.Cross(v[tris[i+1]]-v[tris[i]],v[tris[i+2]]-v[tris[i]]).magnitude*.5;return a;}
  double delta=Area(floor.sharedMesh)-Area(source);if(Math.Abs(delta)>.1)throw new Exception("Unexpected area change: "+delta);
  var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();int samples=0;float maxGap=0;var hitPoints=new System.Collections.Generic.List<float>();
  for(int p=0;p<net.PathCount;p++){var path=net.Path(p);for(int i=0;i<path.points.Length;i++){var q=path.points[i];if(q.x>=5.5f&&q.x<=22.5f&&q.z<-4.1f&&q.z>-7.3f){samples++;hitPoints.Add(q.x);}}}
  hitPoints.Sort();for(int i=1;i<hitPoints.Count;i++)maxGap=Mathf.Max(maxGap,hitPoints[i]-hitPoints[i-1]);if(samples<20||maxGap>1.5f)throw new Exception("Pilot walking route missing");
  if(missing>0)throw new Exception("Missing script");EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);
  return "saved, missing scripts=0; ground surface area delta="+delta.ToString("F5")+"m2 (no added overlay), frontage path samples="+samples+", max sample gap="+maxGap.ToString("F2")+"m, recipients aligned";
 }
}
