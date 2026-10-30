using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage05Paths {
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
  var roots=scene.GetRootGameObjects();var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=153||floor.sharedMesh.name!="Ground_SidewalkStage05"||Mathf.Abs(net.Path(4).points.Last().x-105.5f)>.1f)throw new Exception("Expected Stage04 routes and Stage05 ground");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToList();var checkedIds=new HashSet<int>();
  Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var hit,60))throw new Exception("No floor");return hit.point+Vector3.up*.02f;}
  Vector3[] Line(Vector3 a,Vector3 b){int n=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);return Enumerable.Range(0,n+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)n))).ToArray();}
  PedestrianPath Make(Vector3[] points,float width,int reverse,bool connector,int[] next){var sum=new float[points.Length];for(int i=1;i<points.Length;i++)sum[i]=sum[i-1]+Vector3.Distance(points[i-1],points[i]);return new PedestrianPath{points=points,cumulative=sum,length=sum.Last(),halfWidth=width,reverse=reverse,connector=connector,next=next};}
  void Set(int id,Vector3 a,Vector3 b,float width,int reverse,int[] next){paths[id]=Make(Line(a,b),width,reverse,false,next);checkedIds.Add(id);}
  int Pair(Vector3 a,Vector3 b){int id=paths.Count;var pts=Line(a,b);paths.Add(Make(pts,1,id+1,false,Array.Empty<int>()));paths.Add(Make(pts.Reverse().ToArray(),1,id,false,Array.Empty<int>()));checkedIds.Add(id);checkedIds.Add(id+1);return id;}
  void Connection(int id,int from,int to,Vector3? corner,float width){var a=paths[from].points.Last();var b=paths[to].points.First();var pts=corner.HasValue?Enumerable.Range(0,9).Select(i=>{float t=i/8f;return Feet((1-t)*(1-t)*a+2*t*(1-t)*corner.Value+t*t*b);}).ToArray():Line(a,b);pts[0]=a;pts[pts.Length-1]=b;var p=Make(pts,width,from,true,new[]{to});if(id==paths.Count)paths.Add(p);else paths[id]=p;paths[from].next=paths[from].next.Concat(new[]{id}).Distinct().ToArray();checkedIds.Add(id);}
  float slope=32f/52f,offset=5.45f*Mathf.Sqrt(1+slope*slope);Vector3 East(float z)=>P(116+slope*z-offset,z);
  var direction=new Vector3(-32,0,-52).normalized;var northCorner=East(-5.95f);var southCorner=East(-46.55f);
  var northEnd=northCorner-Vector3.right*1.6f;var eastStart=northCorner+direction*1.6f;var eastEnd=southCorner-direction*1.6f;var rearEnd=southCorner-Vector3.right*1.6f;
  Set(4,paths[4].points.First(),northEnd,1,5,Array.Empty<int>());Set(5,northEnd,paths[5].points.Last(),1,4,paths[5].next);
  Set(22,eastStart,East(-14),1,23,Array.Empty<int>());Set(23,East(-14),eastStart,1,22,Array.Empty<int>());
  Set(24,East(-15),East(-26.5f),1,25,Array.Empty<int>());Set(25,East(-26.5f),East(-15),1,24,Array.Empty<int>());
  Set(26,East(-32),eastEnd,1,27,Array.Empty<int>());Set(27,eastEnd,East(-32),1,26,Array.Empty<int>());
  Connection(78,22,24,null,1);Connection(79,25,23,null,1);
  Connection(80,24,29,null,.4f);paths[28].next=Array.Empty<int>();Connection(81,28,25,null,.4f);
  int rear=Pair(P(60.95f,-46.55f),rearEnd);
  Connection(paths.Count,147,rear,null,.4f);Connection(paths.Count,rear+1,148,null,.4f);
  Connection(paths.Count,24,26,null,.4f);Connection(paths.Count,27,25,null,.4f);
  Connection(paths.Count,4,22,northCorner,1);Connection(paths.Count,23,5,northCorner,1);
  Connection(paths.Count,26,rear+1,southCorner,1);Connection(paths.Count,rear,27,southCorner,1);
  Physics.SyncTransforms();var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int count=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){count+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(count);}
  int samples=0;foreach(int id in checkedIds)foreach(var p in paths[id].points){samples++;var lane=paths[id];var hits=Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,lane.halfWidth,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=floor&&c.attachedRigidbody==null).ToArray();if(hits.Length>0)throw new Exception("Obstructed path "+id+" at "+p+": "+string.Join(",",hits.Select(c=>c.name)));
   if(lane.halfWidth<1)continue;for(int k=0;k<8;k++){var q=p+new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));if(!floor.Raycast(new Ray(q+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("No surface");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("Outside pavement "+id+" "+q);}
  }
  for(int i=0;i<paths.Count;i++){var path=paths[i];if(path.reverse<0||path.reverse>=paths.Count)throw new Exception("Invalid reverse");foreach(int next in path.next)if(next<0||next>=paths.Count||Vector3.Distance(path.points.Last(),paths[next].points.First())>.1f)throw new Exception("Disconnected edge "+i+"->"+next);}
  var so=new SerializedObject(net);var colors=so.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();net.Configure((DeliverySession)so.FindProperty("session").objectReferenceValue,paths.ToArray(),(TrafficPedestrian)so.FindProperty("prefab").objectReferenceValue,coats);EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  string result="B rear + east: 2m clear sidewalk, two narrow alley crossings separate; "+samples+" footprint probes; "+paths.Count+" paths; all declared edges continuous";File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage05_paths.txt",result);return result;
 }
}
