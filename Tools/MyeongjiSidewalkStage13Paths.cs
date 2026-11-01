using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage13Paths {
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
  var roots=scene.GetRootGameObjects();var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=186||floor.sharedMesh.name!="Ground_SidewalkStage13"||Mathf.Abs(net.Path(38).points.First().x-88.14283f)>.1f)throw new Exception("Expected Stage12 routes and Stage13 ground");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToList();var checkedIds=new HashSet<int>();
  Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var hit,60))throw new Exception("No floor");return hit.point+Vector3.up*.02f;}
  Vector3[] Line(Vector3 a,Vector3 b){int n=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);return Enumerable.Range(0,n+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)n))).ToArray();}
  PedestrianPath Make(Vector3[] points,float width,int reverse,bool connector,int[] next){var sum=new float[points.Length];for(int i=1;i<points.Length;i++)sum[i]=sum[i-1]+Vector3.Distance(points[i-1],points[i]);return new PedestrianPath{points=points,cumulative=sum,length=sum.Last(),halfWidth=width,reverse=reverse,connector=connector,next=next};}
  void Set(int id,Vector3 a,Vector3 b,float width,int reverse,int[] next){paths[id]=Make(Line(a,b),width,reverse,false,next);checkedIds.Add(id);}
  void Connection(int id,int from,int to,Vector3? corner,float width){var a=paths[from].points.Last();var b=paths[to].points.First();var pts=corner.HasValue?Enumerable.Range(0,9).Select(i=>{float t=i/8f;return Feet((1-t)*(1-t)*a+2*t*(1-t)*corner.Value+t*t*b);}).ToArray():Line(a,b);pts[0]=a;pts[pts.Length-1]=b;var p=Make(pts,width,from,true,new[]{to});if(id==paths.Count)paths.Add(p);else paths[id]=p;paths[from].next=paths[from].next.Concat(new[]{id}).Distinct().ToArray();checkedIds.Add(id);}
  Vector3 Upper(float z)=>P(116+32f/52f*z+5.45f*Mathf.Sqrt(1+32f*32f/(52f*52f)),z);Vector3 Lower(float z)=>P(84+20f/51f*(z+52)+5.45f*Mathf.Sqrt(1+20f*20f/(51f*51f)),z);var upperDir=new Vector3(-32,0,-52).normalized;var lowerDir=new Vector3(-20,0,-51).normalized;var top=Upper(-47.95f);var bottom=Lower(-98.05f);
  var pts=Line(top+upperDir*1.6f,Upper(-51)).Concat(Line(Upper(-51),Lower(-53)).Skip(1)).Concat(Line(Lower(-53),Lower(-61)).Skip(1)).ToArray();paths[38]=Make(pts,1,39,false,Array.Empty<int>());paths[39]=Make(pts.Reverse().ToArray(),1,38,false,Array.Empty<int>());checkedIds.Add(38);checkedIds.Add(39);
  Set(40,Lower(-62),bottom-lowerDir*1.6f,1,41,Array.Empty<int>());Set(41,bottom-lowerDir*1.6f,Lower(-62),1,40,Array.Empty<int>());
  Set(60,top+Vector3.right*1.6f,P(112.05f,-47.95f),1,61,Array.Empty<int>());Set(61,P(112.05f,-47.95f),top+Vector3.right*1.6f,1,60,Array.Empty<int>());
  Set(64,P(113.05f,-48.95f),P(113.05f,-59.55f),1,65,Array.Empty<int>());Set(65,P(113.05f,-59.55f),P(113.05f,-48.95f),1,64,Array.Empty<int>());
  Set(66,bottom+Vector3.right*1.6f,P(112.05f,-98.05f),1,67,Array.Empty<int>());Set(67,P(112.05f,-98.05f),bottom+Vector3.right*1.6f,1,66,Array.Empty<int>());
  int low=paths.Count;paths.Add(Make(Line(P(113.05f,-66.45f),P(113.05f,-97.05f)),1,low+1,false,Array.Empty<int>()));paths.Add(Make(paths[low].points.Reverse().ToArray(),1,low,false,Array.Empty<int>()));checkedIds.Add(low);checkedIds.Add(low+1);
  Connection(87,38,40,null,1);Connection(90,41,39,null,1);Connection(76,20,38,null,.4f);Connection(88,39,21,null,.4f);Connection(89,39,58,null,.4f);Connection(103,59,38,null,.4f);
  Connection(104,60,62,null,.4f);Connection(108,63,61,null,.4f);Connection(105,60,64,P(113.05f,-47.95f),1);Connection(110,65,61,P(113.05f,-47.95f),1);
  Connection(107,62,67,null,.4f);Connection(111,66,63,null,.4f);Connection(109,low,67,P(113.05f,-98.05f),1);Connection(112,66,low+1,P(113.05f,-98.05f),1);
  Connection(paths.Count,64,low,null,.4f);Connection(paths.Count,low+1,65,null,.4f);
  Connection(paths.Count,39,60,top,1);Connection(paths.Count,61,38,top,1);Connection(paths.Count,40,66,bottom,1);Connection(paths.Count,67,41,bottom,1);
  Physics.SyncTransforms();var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int count=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){count+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(count);}
  int samples=0;foreach(int id in checkedIds)foreach(var p in paths[id].points){samples++;var lane=paths[id];var hits=Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,lane.halfWidth,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=floor&&c.attachedRigidbody==null).ToArray();if(hits.Length>0)throw new Exception("Obstructed path "+id+" at "+p+": "+string.Join(",",hits.Select(c=>c.name)));
   if(lane.halfWidth<1)continue;for(int k=0;k<8;k++){var q=p+new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));if(!floor.Raycast(new Ray(q+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("No surface");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("Outside pavement "+id+" "+q);}
  }
  for(int i=0;i<paths.Count;i++){var path=paths[i];if(path.reverse<0||path.reverse>=paths.Count)throw new Exception("Invalid reverse");foreach(int next in path.next)if(next<0||next>=paths.Count||Vector3.Distance(path.points.Last(),paths[next].points.First())>.1f)throw new Exception("Disconnected edge "+i+"->"+next);}
  var so=new SerializedObject(net);var colors=so.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();net.Configure((DeliverySession)so.FindProperty("session").objectReferenceValue,paths.ToArray(),(TrafficPedestrian)so.FindProperty("prefab").objectReferenceValue,coats);EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  string result="F/G lower perimeter: 2m sidewalks and corners; narrow alley/external links separate; "+samples+" footprint probes; "+paths.Count+" paths; all declared edges continuous";File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage13_paths.txt",result);return result;
 }
}
