using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage11Paths {
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
  var roots=scene.GetRootGameObjects();var floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=185||floor.sharedMesh.name!="Ground_SidewalkStage11"||Mathf.Abs(net.Path(50).points.First().x-34.4f)>.1f)throw new Exception("Expected Stage10 routes and Stage11 ground");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToList();var checkedIds=new HashSet<int>();
  Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var hit,60))throw new Exception("No floor");return hit.point+Vector3.up*.02f;}
  Vector3[] Line(Vector3 a,Vector3 b){int n=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);return Enumerable.Range(0,n+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)n))).ToArray();}
  PedestrianPath Make(Vector3[] points,float width,int reverse,bool connector,int[] next){var sum=new float[points.Length];for(int i=1;i<points.Length;i++)sum[i]=sum[i-1]+Vector3.Distance(points[i-1],points[i]);return new PedestrianPath{points=points,cumulative=sum,length=sum.Last(),halfWidth=width,reverse=reverse,connector=connector,next=next};}
  void Set(int id,Vector3 a,Vector3 b,float width,int reverse,int[] next){paths[id]=Make(Line(a,b),width,reverse,false,next);checkedIds.Add(id);}
  void Connection(int id,int from,int to,Vector3? corner,float width){var a=paths[from].points.Last();var b=paths[to].points.First();var pts=corner.HasValue?Enumerable.Range(0,9).Select(i=>{float t=i/8f;return Feet((1-t)*(1-t)*a+2*t*(1-t)*corner.Value+t*t*b);}).ToArray():Line(a,b);pts[0]=a;pts[pts.Length-1]=b;var p=Make(pts,width,from,true,new[]{to});if(id==paths.Count)paths.Add(p);else paths[id]=p;paths[from].next=paths[from].next.Concat(new[]{id}).Distinct().ToArray();checkedIds.Add(id);}
  Set(50,P(35.45f,-58.45f),P(35.45f,-81.55f),1,51,Array.Empty<int>());Set(51,P(35.45f,-81.55f),P(35.45f,-58.45f),1,50,Array.Empty<int>());
  Set(52,P(28.55f,-58.45f),P(28.55f,-88.45f),1,53,Array.Empty<int>());Set(53,P(28.55f,-88.45f),P(28.55f,-58.45f),1,52,Array.Empty<int>());
  Set(54,P(36.45f,-82.55f),P(57.45f,-82.55f),1,55,Array.Empty<int>());Set(55,P(57.45f,-82.55f),P(36.45f,-82.55f),1,54,Array.Empty<int>());
  Set(56,P(29.55f,-89.45f),P(57.45f,-89.45f),1,57,Array.Empty<int>());Set(57,P(57.45f,-89.45f),P(29.55f,-89.45f),1,56,Array.Empty<int>());
  int cap=paths.Count;paths.Add(Make(Line(P(58.45f,-83.55f),P(58.45f,-88.45f)),1,cap+1,false,Array.Empty<int>()));paths.Add(Make(paths[cap].points.Reverse().ToArray(),1,cap,false,Array.Empty<int>()));checkedIds.Add(cap);checkedIds.Add(cap+1);
  Connection(98,50,54,P(35.45f,-82.55f),1);Connection(106,55,51,P(35.45f,-82.55f),1);
  Connection(103,52,56,P(28.55f,-89.45f),1);Connection(111,57,53,P(28.55f,-89.45f),1);
  Connection(105,54,cap,P(58.45f,-82.55f),1);Connection(109,56,cap+1,P(58.45f,-89.45f),1);
  Connection(paths.Count,cap,57,P(58.45f,-89.45f),1);Connection(paths.Count,cap+1,55,P(58.45f,-82.55f),1);
  Set(18,paths[18].points.First(),P(27.55f,-57.45f),1,19,paths[18].next);Set(19,P(27.55f,-57.45f),paths[19].points.Last(),1,18,paths[19].next);
  Set(177,P(36.45f,-57.45f),paths[177].points.Last(),1,178,paths[177].next);Set(178,paths[178].points.First(),P(36.45f,-57.45f),1,177,paths[178].next);
  Connection(179,18,177,null,.4f);Connection(180,178,19,null,.4f);
  Connection(paths.Count,18,52,P(28.55f,-57.45f),1);Connection(paths.Count,53,19,P(28.55f,-57.45f),1);
  Connection(paths.Count,178,50,P(35.45f,-57.45f),1);Connection(paths.Count,51,177,P(35.45f,-57.45f),1);
  Physics.SyncTransforms();var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int count=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){count+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(count);}
  int samples=0;foreach(int id in checkedIds)foreach(var p in paths[id].points){samples++;var lane=paths[id];var hits=Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,lane.halfWidth,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=floor&&c.attachedRigidbody==null).ToArray();if(hits.Length>0)throw new Exception("Obstructed path "+id+" at "+p+": "+string.Join(",",hits.Select(c=>c.name)));
   if(lane.halfWidth<1)continue;for(int k=0;k<8;k++){var q=p+new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));if(!floor.Raycast(new Ray(q+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("No surface");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("Outside pavement "+id+" "+q);}
  }
  var remove=new HashSet<int>{97,99,100,101,102,104,107,108,110,112};var index=new Dictionary<int,int>();for(int i=0;i<paths.Count;i++)if(!remove.Contains(i))index[i]=index.Count;paths=paths.Where((p,i)=>!remove.Contains(i)).ToList();foreach(var p in paths){p.reverse=index[p.reverse];p.next=p.next.Where(n=>!remove.Contains(n)).Select(n=>index[n]).ToArray();}
  for(int i=0;i<paths.Count;i++){var path=paths[i];if(path.reverse<0||path.reverse>=paths.Count)throw new Exception("Invalid reverse");foreach(int next in path.next)if(next<0||next>=paths.Count||Vector3.Distance(path.points.Last(),paths[next].points.First())>.1f)throw new Exception("Disconnected edge "+i+"->"+next);}
  var so=new SerializedObject(net);var colors=so.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();net.Configure((DeliverySession)so.FindProperty("session").objectReferenceValue,paths.ToArray(),(TrafficPedestrian)so.FindProperty("prefab").objectReferenceValue,coats);EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  string result="South extension internal: 2m continuous L-shaped sidewalks; redundant diagonal shortcuts removed; "+samples+" footprint probes; "+paths.Count+" paths; all declared edges continuous";File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage11_paths.txt",result);return result;
 }
}
