using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage04Paths {
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
  var roots=scene.GetRootGameObjects();var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=145||floor.sharedMesh.name!="Ground_SidewalkStage04"||Mathf.Abs(net.Path(4).points.First().x-31.5f)>.1f)throw new Exception("Expected Stage03 routes and Stage04 ground");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToList();var checkedIds=new HashSet<int>();
  Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var hit,60))throw new Exception("No floor");return hit.point+Vector3.up*.02f;}
  Vector3[] Line(Vector3 a,Vector3 b){int n=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);return Enumerable.Range(0,n+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)n))).ToArray();}
  PedestrianPath Make(Vector3[] points,float width,int reverse,bool connector,int[] next){var sum=new float[points.Length];for(int i=1;i<points.Length;i++)sum[i]=sum[i-1]+Vector3.Distance(points[i-1],points[i]);return new PedestrianPath{points=points,cumulative=sum,length=sum.Last(),halfWidth=width,reverse=reverse,connector=connector,next=next};}
  void Set(int id,Vector3 a,Vector3 b,float width,int reverse,int[] next){paths[id]=Make(Line(a,b),width,reverse,false,next);checkedIds.Add(id);}
  int Pair(Vector3 a,Vector3 b){int id=paths.Count;var pts=Line(a,b);paths.Add(Make(pts,1,id+1,false,Array.Empty<int>()));paths.Add(Make(pts.Reverse().ToArray(),1,id,false,Array.Empty<int>()));checkedIds.Add(id);checkedIds.Add(id+1);return id;}
  void Connection(int id,int from,int to,Vector3? corner,float width){var a=paths[from].points.Last();var b=paths[to].points.First();var pts=corner.HasValue?Enumerable.Range(0,9).Select(i=>{float t=i/8f;return Feet((1-t)*(1-t)*a+2*t*(1-t)*corner.Value+t*t*b);}).ToArray():Line(a,b);pts[0]=a;pts[pts.Length-1]=b;var p=Make(pts,width,from,true,new[]{to});if(id==paths.Count)paths.Add(p);else paths[id]=p;paths[from].next=paths[from].next.Concat(new[]{id}).Distinct().ToArray();checkedIds.Add(id);}
  var front=P(32.45f,-5.95f);var north=P(31.45f,-6.95f);var southJoin=P(32.45f,-46.55f);
  var northANext=paths[3].next;var southANext=paths[17].next;
  Set(2,paths[2].points.First(),front,.4f,3,new[]{4});Set(3,front,paths[3].points.Last(),.4f,2,northANext);
  Set(4,front,paths[4].points.Last(),1,5,paths[4].next);Set(5,paths[5].points.First(),front,1,4,new[]{3});
  Set(12,north,P(31.45f,-25.65f),1,13,Array.Empty<int>());Set(13,P(31.45f,-25.65f),north,1,12,Array.Empty<int>());
  Set(16,paths[16].points.First(),southJoin,.4f,17,Array.Empty<int>());Set(17,southJoin,paths[17].points.Last(),.4f,16,southANext);
  int lower=Pair(P(31.45f,-32.35f),P(31.45f,-45.55f));int rear=Pair(southJoin,P(53.05f,-46.55f));
  Connection(74,lower,rear,P(31.45f,-46.55f),1);Connection(75,rear+1,lower+1,P(31.45f,-46.55f),1);
  paths[74].next=new[]{rear,17};paths[rear+1].next=new[]{75,17};paths[16].next=new[]{75,rear};
  Connection(paths.Count,12,lower,null,.4f);Connection(paths.Count,lower+1,13,null,.4f);
  int northTurn=paths.Count;Connection(northTurn,5,12,P(31.45f,-5.95f),1);Connection(paths.Count,13,4,P(31.45f,-5.95f),1);
  paths[2].next=new[]{4,northTurn};paths.Last().next=new[]{4,3};
  Physics.SyncTransforms();var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int count=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){count+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(count);}
  int samples=0;foreach(int id in checkedIds)foreach(var p in paths[id].points){samples++;var lane=paths[id];var hits=Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,lane.halfWidth,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=floor&&c.attachedRigidbody==null).ToArray();if(hits.Length>0)throw new Exception("Obstructed path "+id+" at "+p+": "+string.Join(",",hits.Select(c=>c.name)));
   if(lane.halfWidth<1)continue;for(int k=0;k<8;k++){var q=p+new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));if(!floor.Raycast(new Ray(q+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("No surface");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("Outside pavement "+id+" "+q);}
  }
  for(int i=0;i<paths.Count;i++){var path=paths[i];if(path.reverse<0||path.reverse>=paths.Count)throw new Exception("Invalid reverse");foreach(int next in path.next)if(next<0||next>=paths.Count||Vector3.Distance(path.points.Last(),paths[next].points.First())>.1f)throw new Exception("Disconnected edge "+i+"->"+next);}
  var so=new SerializedObject(net);var colors=so.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();net.Configure((DeliverySession)so.FindProperty("session").objectReferenceValue,paths.ToArray(),(TrafficPedestrian)so.FindProperty("prefab").objectReferenceValue,coats);EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  string result="B west + southwest: 2m clear sidewalk, narrow alley crossing separate; "+samples+" footprint probes; "+paths.Count+" paths; all declared edges continuous";File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage04_paths.txt",result);return result;
 }
}
