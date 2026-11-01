using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage14Paths {
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
  var roots=scene.GetRootGameObjects();var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=194||floor.sharedMesh.name!="Ground_SidewalkStage14"||Mathf.Abs(net.Path(70).points.First().x-103.8f)>.1f)throw new Exception("Expected Stage13 routes and Stage14 ground");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToList();var checkedIds=new HashSet<int>();
  Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var hit,60))throw new Exception("No floor");return hit.point+Vector3.up*.02f;}
  Vector3[] Line(Vector3 a,Vector3 b){int n=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);return Enumerable.Range(0,n+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)n))).ToArray();}
  PedestrianPath Make(Vector3[] points,float width,int reverse,bool connector,int[] next){var sum=new float[points.Length];for(int i=1;i<points.Length;i++)sum[i]=sum[i-1]+Vector3.Distance(points[i-1],points[i]);return new PedestrianPath{points=points,cumulative=sum,length=sum.Last(),halfWidth=width,reverse=reverse,connector=connector,next=next};}
  void Set(int id,Vector3 a,Vector3 b,float width,int reverse,int[] next){paths[id]=Make(Line(a,b),width,reverse,false,next);checkedIds.Add(id);}
  void Connection(int id,int from,int to,Vector3? corner,float width){var a=paths[from].points.Last();var b=paths[to].points.First();var pts=corner.HasValue?Enumerable.Range(0,9).Select(i=>{float t=i/8f;return Feet((1-t)*(1-t)*a+2*t*(1-t)*corner.Value+t*t*b);}).ToArray():Line(a,b);pts[0]=a;pts[pts.Length-1]=b;var p=Make(pts,width,from,true,new[]{to});if(id==paths.Count)paths.Add(p);else paths[id]=p;paths[from].next=paths[from].next.Concat(new[]{id}).Distinct().ToArray();checkedIds.Add(id);}
  Set(68,P(98.55f,-59.55f),P(112.05f,-59.55f),1,69,Array.Empty<int>());Set(69,P(112.05f,-59.55f),P(98.55f,-59.55f),1,68,Array.Empty<int>());
  Set(70,P(104.45f,-67.45f),P(104.45f,-89),1,71,Array.Empty<int>());Set(71,P(104.45f,-89),P(104.45f,-67.45f),1,70,Array.Empty<int>());
  Set(72,P(97.55f,-60.55f),P(97.55f,-89),1,73,Array.Empty<int>());Set(73,P(97.55f,-89),P(97.55f,-60.55f),1,72,Array.Empty<int>());
  paths.Add(Make(Line(P(105.45f,-66.45f),P(112.05f,-66.45f)),1,195,false,Array.Empty<int>()));paths.Add(Make(paths[194].points.Reverse().ToArray(),1,194,false,Array.Empty<int>()));checkedIds.Add(194);checkedIds.Add(195);
  Set(64,paths[64].points.First(),P(113.05f,-58.55f),1,65,Array.Empty<int>());Set(65,P(113.05f,-58.55f),paths[65].points.Last(),1,64,paths[65].next);
  Set(186,P(113.05f,-67.45f),paths[186].points.Last(),1,187,paths[186].next);Set(187,paths[187].points.First(),P(113.05f,-67.45f),1,186,Array.Empty<int>());
  Connection(188,64,186,null,.4f);Connection(189,187,65,null,.4f);
  Connection(113,69,72,P(97.55f,-59.55f),1);Connection(117,73,68,P(97.55f,-59.55f),1);
  Connection(115,71,194,P(104.45f,-66.45f),1);Connection(paths.Count,195,70,P(104.45f,-66.45f),1);
  var arc=Enumerable.Range(0,33).Select(i=>{float t=i*Mathf.PI/32;return Feet(P(101+3.45f*Mathf.Cos(t),-89-3.45f*Mathf.Sin(t)));}).ToArray();
  int cap=paths.Count;paths.Add(Make(arc,1,70,true,new[]{73}));paths[70].next=new[]{cap};checkedIds.Add(cap);paths.Add(Make(arc.Reverse().ToArray(),1,72,true,new[]{71}));paths[72].next=new[]{cap+1};checkedIds.Add(cap+1);
  Connection(paths.Count,64,69,P(113.05f,-59.55f),1);Connection(paths.Count,68,65,P(113.05f,-59.55f),1);
  Connection(paths.Count,194,186,P(113.05f,-66.45f),1);Connection(paths.Count,187,195,P(113.05f,-66.45f),1);
  Physics.SyncTransforms();var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int count=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){count+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(count);}
  int samples=0;foreach(int id in checkedIds)foreach(var p in paths[id].points){samples++;var lane=paths[id];var hits=Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,lane.halfWidth,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=floor&&c.attachedRigidbody==null).ToArray();if(hits.Length>0)throw new Exception("Obstructed path "+id+" at "+p+": "+string.Join(",",hits.Select(c=>c.name)));
   if(lane.halfWidth<1)continue;for(int k=0;k<8;k++){var q=p+new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));if(!floor.Raycast(new Ray(q+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("No surface");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("Outside pavement "+id+" "+q);}
  }
  var remove=new HashSet<int>{114,116,118};var index=new Dictionary<int,int>();for(int i=0;i<paths.Count;i++)if(!remove.Contains(i))index[i]=index.Count;paths=paths.Where((p,i)=>!remove.Contains(i)).ToList();foreach(var p in paths){p.reverse=index[p.reverse];p.next=p.next.Where(n=>!remove.Contains(n)).Select(n=>index[n]).ToArray();}
  for(int i=0;i<paths.Count;i++){var path=paths[i];if(path.reverse<0||path.reverse>=paths.Count)throw new Exception("Invalid reverse");foreach(int next in path.next)if(next<0||next>=paths.Count||Vector3.Distance(path.points.Last(),paths[next].points.First())>.1f)throw new Exception("Disconnected edge "+i+"->"+next);}
  var so=new SerializedObject(net);var colors=so.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();net.Configure((DeliverySession)so.FindProperty("session").objectReferenceValue,paths.ToArray(),(TrafficPedestrian)so.FindProperty("prefab").objectReferenceValue,coats);EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  string result="G internal: 2m continuous sidewalks, branch corners and rounded dead end; "+samples+" footprint probes; "+paths.Count+" paths; all declared edges continuous";File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage14_paths.txt",result);return result;
 }
}
