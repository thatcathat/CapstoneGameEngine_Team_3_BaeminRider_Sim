using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using DeliveryRider;
public static class MyeongjiSidewalkStage02Paths {
 static GameObject Root(string n)=>SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name==n);
 static MeshCollider floor;
 static Vector3 Feet(Vector3 p){if(!floor.Raycast(new Ray(p+Vector3.up*30,Vector3.down),out var h,60))throw new Exception("No floor");return h.point+Vector3.up*.02f;}
 static PedestrianPath Make(Vector3[] points,float width,int reverse,bool connector){var cumulative=new float[points.Length];for(int i=1;i<points.Length;i++)cumulative[i]=cumulative[i-1]+Vector3.Distance(points[i],points[i-1]);return new PedestrianPath{points=points,cumulative=cumulative,length=cumulative.Last(),halfWidth=width,reverse=reverse,connector=connector,next=Array.Empty<int>()};}
 static Vector3 P(float x,float z)=>new Vector3(x,0,z);
 static Vector3[] Line(Vector3 a,Vector3 b){int n=Mathf.CeilToInt(Vector3.Distance(a,b)/.4f);return Enumerable.Range(0,n+1).Select(i=>Feet(Vector3.Lerp(a,b,i/(float)n))).ToArray();}
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved scene edit mode required");
  floor=Root("MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var net=Root("Pedestrians").GetComponent<PedestrianNetwork>();
  if(net.PathCount!=133)throw new Exception("Run the current pedestrian baker first; expected stage02 topology");
  var paths=Enumerable.Range(0,net.PathCount).Select(i=>JsonUtility.FromJson<PedestrianPath>(JsonUtility.ToJson(net.Path(i)))).ToList();
  if(paths[8].points[0].x<4||paths[16].points.Last().x<30)throw new Exception("Unexpected source topology");
  foreach(int id in new[]{2,16}){var old=paths[id];var points=old.points.Where(p=>p.x>=25).ToArray();if(points.Length<2)throw new Exception("Missing neighboring lane");paths[id]=Make(points,old.halfWidth,old.reverse,false);paths[id].next=old.next;var reverse=paths[old.reverse];paths[old.reverse]=Make(points.Reverse().ToArray(),old.halfWidth,id,false);paths[old.reverse].next=Array.Empty<int>();}
  paths[8]=Make(Line(P(4.95f,-6.95f),P(4.95f,-45.55f)),1f,9,false);paths[9]=Make(paths[8].points.Reverse().ToArray(),1f,8,false);
  paths[14]=Make(Line(P(22.55f,-6.95f),P(22.55f,-45.55f)),1f,15,false);paths[15]=Make(paths[14].points.Reverse().ToArray(),1f,14,false);
  int north=paths.Count;paths.Add(Make(Line(P(5.95f,-5.95f),P(21.55f,-5.95f)),1f,north+1,false));paths.Add(Make(paths[north].points.Reverse().ToArray(),1f,north,false));
  int south=paths.Count;paths.Add(Make(Line(P(5.95f,-46.55f),P(21.55f,-46.55f)),1f,south+1,false));paths.Add(Make(paths[south].points.Reverse().ToArray(),1f,south,false));
  var verified=new List<int>{8,9,14,15,north,north+1,south,south+1};
  void Connect(int from,int to,Vector3? corner,float width){
   var a=paths[from].points.Last();var b=paths[to].points.First();Vector3[] points;
   if(corner.HasValue){var c=corner.Value;points=Enumerable.Range(0,9).Select(i=>{float t=i/8f;return Feet((1-t)*(1-t)*a+2*(1-t)*t*c+t*t*b);}).ToArray();}else points=Line(a,b);
   points[0]=a;points[points.Length-1]=b;int id=paths.Count;var route=Make(points,width,from,true);route.next=new[]{to};paths.Add(route);paths[from].next=paths[from].next.Concat(new[]{id}).ToArray();if(width==1f)verified.Add(id);
  }
  Connect(north,14,P(22.55f,-5.95f),1);Connect(15,north+1,P(22.55f,-5.95f),1);
  Connect(14,south+1,P(22.55f,-46.55f),1);Connect(south,15,P(22.55f,-46.55f),1);
  Connect(south+1,9,P(4.95f,-46.55f),1);Connect(8,south,P(4.95f,-46.55f),1);
  Connect(9,north,P(4.95f,-5.95f),1);Connect(north+1,8,P(4.95f,-5.95f),1);
  // Retain pedestrian-only alley links to B, with their narrower checked width.
  Connect(north,2,null,.4f);Connect(3,north+1,null,.4f);Connect(south,16,null,.4f);Connect(17,south+1,null,.4f);
  var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int sum=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){sum+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(sum);}
  int samples=0;Physics.SyncTransforms();
  foreach(int id in verified.Concat(Enumerable.Range(paths.Count-4,4))){var lane=paths[id];foreach(var q in lane.points){samples++;var hits=Physics.OverlapCapsule(q+Vector3.up*.8f,q+Vector3.up*1.5f,lane.halfWidth,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=floor&&c.attachedRigidbody==null).ToArray();if(hits.Length>0)throw new Exception("Path footprint blocked "+id+" at "+q+": "+string.Join(",",hits.Select(h=>h.name)));
   if(lane.halfWidth<1)continue;for(int k=0;k<8;k++){var p=q+new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));if(!floor.Raycast(new Ray(p+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("No surface");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("Path footprint leaves pavement: "+id+" "+p);}
  }}
  var remove=new HashSet<int>{74,75,76,79};var index=new Dictionary<int,int>();for(int i=0;i<paths.Count;i++)if(!remove.Contains(i))index[i]=index.Count;
  var kept=paths.Where((p,i)=>!remove.Contains(i)).ToArray();foreach(var path in kept){path.reverse=index[path.reverse];path.next=path.next.Where(n=>!remove.Contains(n)).Select(n=>index[n]).ToArray();foreach(int n in path.next)if(Vector3.Distance(path.points.Last(),kept[n].points.First())>.1f)throw new Exception("Broken graph endpoint");}
  var data=new SerializedObject(net);var session=(DeliverySession)data.FindProperty("session").objectReferenceValue;var prefab=(TrafficPedestrian)data.FindProperty("prefab").objectReferenceValue;var colors=data.FindProperty("coats");var coats=Enumerable.Range(0,colors.arraySize).Select(i=>(Material)colors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
  net.Configure(session,kept,prefab,coats);EditorUtility.SetDirty(net);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  return "A block: 8 directional lanes + 8 rounded corners, checked half-width=1m; 4 narrow alley links retained. "+samples+" footprint samples passed; total paths="+kept.Length;
 }
}
