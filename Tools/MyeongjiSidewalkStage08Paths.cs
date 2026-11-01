using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage08Paths {
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
  var roots=scene.GetRootGameObjects();var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=169||floor.sharedMesh.name!="Ground_SidewalkStage08"||Mathf.Abs(net.Path(30).points.First().x-32.81f)>.1f)throw new Exception("Expected Stage07 routes and Stage08 ground");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToList();var checkedIds=new HashSet<int>();
  Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var hit,60))throw new Exception("No floor");return hit.point+Vector3.up*.02f;}
  Vector3[] Line(Vector3 a,Vector3 b){int n=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);return Enumerable.Range(0,n+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)n))).ToArray();}
  PedestrianPath Make(Vector3[] points,float width,int reverse,bool connector,int[] next){var sum=new float[points.Length];for(int i=1;i<points.Length;i++)sum[i]=sum[i-1]+Vector3.Distance(points[i-1],points[i]);return new PedestrianPath{points=points,cumulative=sum,length=sum.Last(),halfWidth=width,reverse=reverse,connector=connector,next=next};}
  void Set(int id,Vector3 a,Vector3 b,float width,int reverse,int[] next){paths[id]=Make(Line(a,b),width,reverse,false,next);checkedIds.Add(id);}
  void Connection(int id,int from,int to,Vector3? corner,float width){var a=paths[from].points.Last();var b=paths[to].points.First();var pts=corner.HasValue?Enumerable.Range(0,9).Select(i=>{float t=i/8f;return Feet((1-t)*(1-t)*a+2*t*(1-t)*corner.Value+t*t*b);}).ToArray():Line(a,b);pts[0]=a;pts[pts.Length-1]=b;var p=Make(pts,width,from,true,new[]{to});if(id==paths.Count)paths.Add(p);else paths[id]=p;paths[from].next=paths[from].next.Concat(new[]{id}).Distinct().ToArray();checkedIds.Add(id);}
  int Pair(Vector3 a,Vector3 b){int id=paths.Count;var pts=Line(a,b);paths.Add(Make(pts,1,id+1,false,Array.Empty<int>()));paths.Add(Make(pts.Reverse().ToArray(),1,id,false,Array.Empty<int>()));checkedIds.Add(id);checkedIds.Add(id+1);return id;}
  float offset=5.45f*Mathf.Sqrt(1+32f*32f/(52f*52f));Vector3 East(float z)=>P(116+32f/52f*z-offset,z);var ec=East(-32.95f);
  Set(30,P(32.45f,-32.95f),P(52.05f,-32.95f),1,31,Array.Empty<int>());Set(31,P(52.05f,-32.95f),P(32.45f,-32.95f),1,30,Array.Empty<int>());
  Set(32,paths[32].points.First(),P(53.05f,-33.95f),1,33,Array.Empty<int>());Set(33,P(53.05f,-33.95f),paths[33].points.Last(),1,32,paths[33].next);
  Set(34,paths[34].points.First(),P(60.95f,-33.95f),1,35,Array.Empty<int>());Set(35,P(60.95f,-33.95f),paths[35].points.Last(),1,34,paths[35].next);
  Set(145,P(31.45f,-33.95f),paths[145].points.Last(),1,146,paths[145].next);Set(146,paths[146].points.First(),P(31.45f,-33.95f),1,145,paths[146].next);
  Set(26,East(-34.3f),paths[26].points.Last(),1,27,paths[26].next);Set(27,paths[27].points.First(),East(-34.3f),1,26,paths[27].next);
  int east=Pair(P(61.95f,-32.95f),ec-Vector3.right*1.6f);
  Connection(82,30,33,P(53.05f,-32.95f),1);Connection(84,32,31,P(53.05f,-32.95f),1);
  Connection(83,30,east,null,.4f);Connection(85,east+1,31,null,.4f);
  Connection(149,12,145,null,.4f);Connection(150,146,13,null,.4f);Connection(157,24,26,null,.4f);Connection(158,27,25,null,.4f);
  Connection(paths.Count,east+1,35,P(60.95f,-32.95f),1);Connection(paths.Count,34,east,P(60.95f,-32.95f),1);
  Connection(paths.Count,east,26,ec,1);Connection(paths.Count,27,east+1,ec,1);
  Connection(paths.Count,146,30,P(31.45f,-32.95f),1);Connection(paths.Count,31,145,P(31.45f,-32.95f),1);
  Physics.SyncTransforms();var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int count=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){count+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(count);}
  int samples=0;foreach(int id in checkedIds)foreach(var p in paths[id].points){samples++;var lane=paths[id];var hits=Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,lane.halfWidth,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=floor&&c.attachedRigidbody==null).ToArray();if(hits.Length>0)throw new Exception("Obstructed path "+id+" at "+p+": "+string.Join(",",hits.Select(c=>c.name)));
   if(lane.halfWidth<1)continue;for(int k=0;k<8;k++){var q=p+new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));if(!floor.Raycast(new Ray(q+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("No surface");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("Outside pavement "+id+" "+q);}
  }
  for(int i=0;i<paths.Count;i++){var path=paths[i];if(path.reverse<0||path.reverse>=paths.Count)throw new Exception("Invalid reverse");foreach(int next in path.next)if(next<0||next>=paths.Count||Vector3.Distance(path.points.Last(),paths[next].points.First())>.1f)throw new Exception("Disconnected edge "+i+"->"+next);}
  var so=new SerializedObject(net);var colors=so.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();net.Configure((DeliverySession)so.FindProperty("session").objectReferenceValue,paths.ToArray(),(TrafficPedestrian)so.FindProperty("prefab").objectReferenceValue,coats);EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  string result="B interior south: 2m clear sidewalks and corners; alley crossing .4m; "+samples+" footprint probes; "+paths.Count+" paths; all declared edges continuous";File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage08_paths.txt",result);return result;
 }
}
