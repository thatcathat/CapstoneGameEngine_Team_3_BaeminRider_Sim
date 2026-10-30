using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage03Paths {
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");var roots=scene.GetRootGameObjects();var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=145||Mathf.Abs(net.Path(4).points.First().x-61)>.1f)throw new Exception("Expected verified Stage02 routes. Refusing to overwrite a different layout.");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToArray();
  Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(p+Vector3.up*30,Vector3.down),out var h,60))throw new Exception("No floor");return h.point+Vector3.up*.02f;}
  PedestrianPath Line(Vector3 a,Vector3 b,float half,int reverse,int[] next){int count=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);var points=Enumerable.Range(0,count+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)count))).ToArray();var cumulative=new float[points.Length];for(int i=1;i<points.Length;i++)cumulative[i]=cumulative[i-1]+Vector3.Distance(points[i-1],points[i]);return new PedestrianPath{points=points,cumulative=cumulative,length=cumulative.Last(),halfWidth=half,reverse=reverse,connector=false,next=next};}
  var start=paths[2].points.First();var join=P(31.5f,-5.95f);var end=P(105.5f,-5.95f);var returnNext=paths[3].next;
  paths[2]=Line(start,join,.4f,3,new[]{4});paths[3]=Line(join,start,.4f,2,returnNext);paths[4]=Line(join,end,1f,5,Array.Empty<int>());paths[5]=Line(end,join,1f,4,new[]{3});
  int samples=0;Physics.SyncTransforms();foreach(int id in new[]{2,3,4,5})foreach(var p in paths[id].points){samples++;if(Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,paths[id].halfWidth,~0,QueryTriggerInteraction.Ignore).Any(c=>c!=floor&&c.attachedRigidbody==null))throw new Exception("Path obstruction "+id+" at "+p);}
  for(int i=0;i<paths.Length;i++)foreach(int next in paths[i].next)if(Vector3.Distance(paths[i].points.Last(),paths[next].points.First())>.1f)throw new Exception("Disconnected path "+i+"->"+next);
  var data=new SerializedObject(net);var colors=data.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();net.Configure((DeliverySession)data.FindProperty("session").objectReferenceValue,paths,(TrafficPedestrian)data.FindProperty("prefab").objectReferenceValue,coats);
  EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "B frontage: 74m continuous two-way path; 2m checked width. A/alley connections retained; "+samples+" footprint samples passed. All 145 graph endpoints connected as declared; A routes unchanged.";
 }
}
