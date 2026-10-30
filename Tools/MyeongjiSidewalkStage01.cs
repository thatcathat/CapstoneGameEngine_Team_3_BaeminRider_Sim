using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DeliveryRider;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
public static class MyeongjiSidewalkStage01
{
 const string ScenePath="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity";
 const string MeshPath="Assets/_Game/Art/Myeongji/Geometry/Ground_SidewalkStage01.asset";
 static readonly Vector3 Delta=new Vector3(.5f,0,-2.7f);
 static GameObject Root(string n)=>SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name==n);
 static MeshCollider Floor=>Root("MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();
 static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
 static Vector3 Feet(Vector3 p){if(!Floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var h,60))throw new Exception("No floor");p.y=h.point.y;return p;}
 static bool IsFloor(Collider c)=>c==Floor;
 static bool Solid(Collider c)=>!IsFloor(c)&&c.attachedRigidbody==null&&!c.isTrigger;
 static bool Clear(Vector3 p,float radius)=>!Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,radius,~0,QueryTriggerInteraction.Ignore).Any(Solid);
 static void Guard(){var s=SceneManager.GetActiveScene();if(s.path!=ScenePath||s.isDirty||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("Saved Myeongji scene in edit mode required");}
 public static string ApplyGeometry(){
  Guard();if(AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath)!=null)throw new Exception("Stage already applied");
  var target=Root("MyeongjiMap").GetComponentsInChildren<Renderer>().Single(r=>r.name=="AB01_A01.001");
  if(target.transform.position.sqrMagnitude>.0001f)throw new Exception("Building changed since audit");
  var originalBounds=target.bounds;
  var ground=Floor;var mf=ground.GetComponent<MeshFilter>();var old=mf.sharedMesh;
  var mesh=ExpandPavement(old,ground.transform,ground.GetComponent<Renderer>());
  // Reject unsafe placement before saving any scene modifications.
  target.transform.position+=Delta;Physics.SyncTransforms();
  var newBounds=target.bounds;var inset=newBounds;inset.Expand(-.02f);
  var blockers=Root("MyeongjiMap").GetComponentsInChildren<Renderer>().Where(r=>r!=target&&r.name.StartsWith("AB01_")&&inset.Intersects(r.bounds)).Select(r=>r.name).ToArray();
  if(blockers.Length>0){target.transform.position-=Delta;UnityEngine.Object.DestroyImmediate(mesh);throw new Exception("Building overlap: "+string.Join(",",blockers));}
  if(!AssetDatabase.IsValidFolder("Assets/_Game/Art/Myeongji/Geometry"))AssetDatabase.CreateFolder("Assets/_Game/Art/Myeongji","Geometry");
  AssetDatabase.CreateAsset(mesh,MeshPath);mf.sharedMesh=mesh;ground.sharedMesh=mesh;
  PrefabUtility.RecordPrefabInstancePropertyModifications(mf);PrefabUtility.RecordPrefabInstancePropertyModifications(ground);PrefabUtility.RecordPrefabInstancePropertyModifications(target.transform);
  Physics.SyncTransforms();
  var anchors=JArray.Parse(File.ReadAllText("Docs/MapLayout/Myeongji/anchors.json"));
  foreach(var a in anchors.Where(a=>(string)a["building"]=="A01")){
   var t=Root("MyeongjiLocations").transform.Find((string)a["name"]);if(t==null)throw new Exception("Missing anchor");
   t.position=Feet(t.position+Delta);if(!Clear(t.position,.5f))throw new Exception("Anchor obstruction: "+t.name);
   a["position"]=new JArray(V(t.position));var rp=a["roadPoint"];rp[0]=(float)rp[0]+Delta.x;
  }
  // Door hazards attached to the moved building follow its wall; all other event positions stay in place.
  var candidates=AssetDatabase.LoadAssetAtPath<HazardCandidates>("Assets/_Game/Settings/MyeongjiHazardCandidates.asset");
  var so=new SerializedObject(candidates);var items=so.FindProperty("candidates");int doors=0;
  for(int i=0;i<items.arraySize;i++){var item=items.GetArrayElementAtIndex(i);if(item.FindPropertyRelative("type").stringValue!="door")continue;
   var pos=item.FindPropertyRelative("position");var p=pos.vector3Value;
   if(p.x>=originalBounds.min.x-1f&&p.x<=originalBounds.max.x+1f&&p.z>=originalBounds.min.z-1f&&p.z<=originalBounds.max.z+1f){pos.vector3Value=p+Delta;doors++;}
  }
  so.ApplyModifiedPropertiesWithoutUndo();
  File.WriteAllText("Docs/MapLayout/Myeongji/anchors.json",anchors.ToString());
  var layout=JObject.Parse(File.ReadAllText("Docs/MapLayout/Myeongji/layout.json"));var lot=layout["lots"].Single(l=>(string)l[0]=="A01");lot[2]=(float)lot[2]+Delta.x;lot[3]=(float)lot[3]+Delta.z;
  File.WriteAllText("Docs/MapLayout/Myeongji/layout.json",layout.ToString());
  EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
  return "Stage01 saved: single ground mesh edited, A01 shifted "+Delta+", attached door candidates moved="+doors+". "+Review();
 }
 static Mesh ExpandPavement(Mesh source,Transform t,Renderer renderer){
  var materials=renderer.sharedMaterials;int plot=Array.FindIndex(materials,m=>m.name.Contains("Plot")),pave=Array.FindIndex(materials,m=>m.name.Contains("Pavement"));
  if(plot<0||pave<0)throw new Exception("Ground materials missing");
  var vertices=source.vertices;var output=new List<Vector3>();var triangles=Enumerable.Range(0,source.subMeshCount).Select(_=>new List<int>()).ToArray();
  void Add(List<Vector3> poly,int sub){if(poly.Count<3)return;for(int k=1;k<poly.Count-1;k++){if(Vector3.Cross(poly[k]-poly[0],poly[k+1]-poly[0]).sqrMagnitude<1e-12f)continue;int b=output.Count;output.Add(t.InverseTransformPoint(poly[0]));output.Add(t.InverseTransformPoint(poly[k]));output.Add(t.InverseTransformPoint(poly[k+1]));triangles[sub].AddRange(new[]{b,b+1,b+2});}}
  // Replace plot material inside the new frontage. Split triangles at the rectangle, never overlay faces.
  var planes=new[]{new Vector3(1,0,3.2f),new Vector3(-1,0,-24.3f),new Vector3(0,1,-7.4f),new Vector3(0,-1,4.05f)};
  for(int sub=0;sub<source.subMeshCount;sub++){
   var ids=source.GetTriangles(sub);
   for(int i=0;i<ids.Length;i+=3){var poly=new List<Vector3>{t.TransformPoint(vertices[ids[i]]),t.TransformPoint(vertices[ids[i+1]]),t.TransformPoint(vertices[ids[i+2]])};
    if(sub!=plot||poly.Any(p=>Mathf.Abs(p.y)>.03f)||Vector3.Cross(poly[1]-poly[0],poly[2]-poly[0]).y<=0){Add(poly,sub);continue;}
    foreach(var plane in planes){Split(poly,plane,out var inside,out var outside);Add(outside,sub);poly=inside;if(poly.Count<3)break;}
    Add(poly,pave);
   }
  }
  var m=new Mesh{name="Ground_SidewalkStage01",indexFormat=IndexFormat.UInt32};m.SetVertices(output);m.subMeshCount=source.subMeshCount;for(int i=0;i<triangles.Length;i++)m.SetTriangles(triangles[i],i);m.RecalculateNormals();m.RecalculateBounds();return m;
 }
 static void Split(List<Vector3> poly,Vector3 plane,out List<Vector3> inside,out List<Vector3> outside){
  inside=new List<Vector3>();outside=new List<Vector3>();if(poly.Count==0)return;
  float D(Vector3 p)=>plane.x*p.x+plane.y*p.z-plane.z;
  var a=poly[poly.Count-1];float da=D(a);
  foreach(var b in poly){float db=D(b);if((da>=0)!=(db>=0)){var v=Vector3.Lerp(a,b,da/(da-db));inside.Add(v);outside.Add(v);}if(db>=0)inside.Add(b);else outside.Add(b);a=b;da=db;}
 }
 public static string Review(){
  Physics.SyncTransforms();var errors=new List<string>();int probes=0;var floor=Floor;var renderer=floor.GetComponent<Renderer>();var ends=new List<int>();int sum=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){sum+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(sum);}
  // Continuous two-metre-wide clear corridor along the widened frontage, excluding corner transitions.
  for(float x=5.5f;x<=22.5f;x+=.5f){var p=Feet(new Vector3(x,0,-6.05f));probes++;if(!Clear(p,1f))errors.Add("Blocked at "+p);for(float z=-5.05f;z>=-7.05f;z-=.5f){floor.Raycast(new Ray(new Vector3(x,20,z),Vector3.down),out var h,40);int sub=ends.FindIndex(e=>h.triangleIndex<e);if(sub<0||!renderer.sharedMaterials[sub].name.Contains("Pavement"))errors.Add("Non pavement at "+x+","+z);}}
  var net=Root("Traffic").GetComponent<TrafficNetwork>();int trafficHits=0;
  for(int i=0;i<net.PathCount;i++){var path=net.Path(i);for(int j=1;j<path.points.Length;j++){var a=path.points[j-1];var b=path.points[j];if((a+b).x*.5f>30||(a+b).z*.5f<-20)continue;trafficHits+=Physics.OverlapBox((a+b)*.5f+Vector3.up*1.2f,new Vector3(1.02f,.85f,2.5f),Quaternion.LookRotation(b-a),~0,QueryTriggerInteraction.Ignore).Count(Solid);}}
  if(trafficHits>0)errors.Add("Traffic overlaps="+trafficHits);
  var result="Frontage probes="+probes+", two-metre corridor / pavement checks; traffic overlaps="+trafficHits+", issues="+string.Join(";",errors.Distinct().Take(12));
  if(errors.Count>0)throw new Exception(result);return result;
 }
}
