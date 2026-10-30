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
public static class MyeongjiSidewalkStage04 {
 const string ScenePath="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity";
 const string MeshPath="Assets/_Game/Art/Myeongji/Geometry/Ground_SidewalkStage04.asset";
 static GameObject Root(string n)=>SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name==n);
 static MeshCollider Floor=>Root("MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();
 static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
 static Vector3 Feet(Vector3 p){if(!Floor.Raycast(new Ray(new Vector3(p.x,30,p.z),Vector3.down),out var h,60))throw new Exception("No floor");p.y=h.point.y;return p;}
 static bool Solid(Collider c)=>c!=Floor&&c.attachedRigidbody==null&&!c.isTrigger;
 static bool Clear(Vector3 p,float radius)=>!Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,radius,~0,QueryTriggerInteraction.Ignore).Any(Solid);
 static void Guard(){var s=SceneManager.GetActiveScene();if(s.path!=ScenePath||s.isDirty||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("Saved Myeongji scene in edit mode required");}
 static float Remap(float p,float a,float b,float c,float d)=>p<a?c+p-a:p>b?d+p-b:Mathf.Lerp(c,d,(p-a)/(b-a));
 static Vector3 Map(Vector3 p,Bounds a,Bounds b)=>new Vector3(Remap(p.x,a.min.x,a.max.x,b.min.x,b.max.x),p.y,Remap(p.z,a.min.z,a.max.z,b.min.z,b.max.z));
 public static string Apply(){
  Guard();if(AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath)!=null)throw new Exception("Stage04 already applied");
  var renderers=Root("MyeongjiMap").GetComponentsInChildren<Renderer>();
  var selected=new[]{11,15,16}.Select(i=>renderers.Single(r=>r.name=="AB01_B"+i.ToString("00")+".001")).ToArray();
  var before=selected.Select(r=>r.bounds).ToArray();var positions=selected.Select(r=>r.transform.position).ToArray();var scales=selected.Select(r=>r.transform.localScale).ToArray();
  var ground=Floor;var old=ground.sharedMesh;Mesh mesh=old;
  try{
   // Keep each interior-facing wall in place; reclaim only the road-facing footprint.
   for(int i=0;i<selected.Length;i++){
    var a=before[i];float xmin=Mathf.Max(32.8f,a.min.x),xmax=a.max.x,zmin=Mathf.Max(-45.2f,a.min.z),zmax=a.max.z;
    var r=selected[i];float sx=(xmax-xmin)/a.size.x,sz=(zmax-zmin)/a.size.z;
    r.transform.localScale=new Vector3(scales[i].x*sx,scales[i].y,scales[i].z*sz);
    var shift=new Vector3((xmin+xmax)*.5f-r.bounds.center.x,0,(zmin+zmax)*.5f-r.bounds.center.z);r.transform.position+=shift;
   }
   Physics.SyncTransforms();
   foreach(var r in selected){var box=r.bounds;box.Expand(-.03f);var other=renderers.FirstOrDefault(q=>q!=r&&q.name.StartsWith("AB01_")&&box.Intersects(q.bounds));if(other!=null)throw new Exception("Building overlaps: "+r.name+"/"+other.name);}
   foreach(var rect in new[]{Rect.MinMaxRect(29.5f,-48.5f,32.8f,-4f),Rect.MinMaxRect(29.5f,-48.5f,55f,-45.2f)}){
    var next=ExpandPavement(mesh,ground.transform,ground.GetComponent<Renderer>(),rect);if(mesh!=old)UnityEngine.Object.DestroyImmediate(mesh);mesh=next;
   }
   ground.GetComponent<MeshFilter>().sharedMesh=mesh;ground.sharedMesh=mesh;Physics.SyncTransforms();
   ReviewGeometry();
  }catch{for(int i=0;i<selected.Length;i++){selected[i].transform.position=positions[i];selected[i].transform.localScale=scales[i];}ground.GetComponent<MeshFilter>().sharedMesh=old;ground.sharedMesh=old;if(mesh!=old)UnityEngine.Object.DestroyImmediate(mesh);Physics.SyncTransforms();throw;}
  AssetDatabase.CreateAsset(mesh,MeshPath);
  PrefabUtility.RecordPrefabInstancePropertyModifications(ground);PrefabUtility.RecordPrefabInstancePropertyModifications(ground.GetComponent<MeshFilter>());
  var anchors=JArray.Parse(File.ReadAllText("Docs/MapLayout/Myeongji/anchors.json"));var layout=JObject.Parse(File.ReadAllText("Docs/MapLayout/Myeongji/layout.json"));int anchorsMoved=0;
  for(int i=0;i<selected.Length;i++){
   var r=selected[i];var a=before[i];var b=r.bounds;string id=r.name.Substring(5,3);PrefabUtility.RecordPrefabInstancePropertyModifications(r.transform);
   foreach(var row in anchors.Where(v=>(string)v["building"]==id)){
    var t=Root("MyeongjiLocations").transform.Find((string)row["name"]);t.position=Feet(Map(t.position,a,b));row["position"]=new JArray(V(t.position));
    var road=layout["roads"].Single(v=>(string)v["id"]==(string)row["road"]);Vector3 pa=new Vector3((float)road["a"][0],0,(float)road["a"][1]),pb=new Vector3((float)road["b"][0],0,(float)road["b"][1]);var d=pb-pa;row["roadPoint"]=new JArray(V(Feet(pa+d*Mathf.Clamp01(Vector3.Dot(t.position-pa,d)/d.sqrMagnitude))));anchorsMoved++;
   }
   var lot=layout["lots"].Single(v=>(string)v[0]==id);var center=Map(new Vector3((float)lot[2],0,(float)lot[3]),a,b);lot[2]=center.x;lot[3]=center.z;
   bool swapped=Mathf.Abs((float)lot[7]%180)>1;int wi=swapped?5:4,di=swapped?4:5;lot[wi]=(float)lot[wi]*(b.size.x/a.size.x);lot[di]=(float)lot[di]*(b.size.z/a.size.z);
  }
  var candidates=AssetDatabase.LoadAssetAtPath<HazardCandidates>("Assets/_Game/Settings/MyeongjiHazardCandidates.asset");var so=new SerializedObject(candidates);var items=so.FindProperty("candidates");int movedDoors=0;
  for(int j=0;j<items.arraySize;j++){var item=items.GetArrayElementAtIndex(j);if(item.FindPropertyRelative("type").stringValue!="door")continue;var property=item.FindPropertyRelative("position");var p=property.vector3Value;int i=Enumerable.Range(0,selected.Length).OrderBy(k=>before[k].SqrDistance(p)).First();if(before[i].SqrDistance(p)>1)continue;property.vector3Value=Map(p,before[i],selected[i].bounds);movedDoors++;}so.ApplyModifiedPropertiesWithoutUndo();
  File.WriteAllText("Docs/MapLayout/Myeongji/anchors.json",anchors.ToString());File.WriteAllText("Docs/MapLayout/Myeongji/layout.json",layout.ToString());
  var report=new JObject{{"stage",4},{"bounds",new JArray(selected.Select((r,i)=>new JObject{{"id",r.name},{"oldMin",new JArray(V(before[i].min))},{"oldMax",new JArray(V(before[i].max))},{"newMin",new JArray(V(r.bounds.min))},{"newMax",new JArray(V(r.bounds.max))}}))}};File.WriteAllText("Docs/MapLayout/Myeongji/sidewalk_stage04.json",report.ToString());
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
  return "B west/southwest widened; 3 footprints adjusted, "+anchorsMoved+" anchors and "+movedDoors+" wall events aligned. "+ReviewGeometry();
 }
 public static string ReviewGeometry(){
  Physics.SyncTransforms();int probes=0;var floor=Floor;var materials=floor.GetComponent<Renderer>().sharedMaterials;var ends=new List<int>();int total=0;for(int i=0;i<floor.sharedMesh.subMeshCount;i++){total+=(int)floor.sharedMesh.GetIndexCount(i)/3;ends.Add(total);}
  var centers=new List<Vector3>();for(float z=-6.95f;z>=-25.65f;z-=.4f)centers.Add(new Vector3(31.45f,0,z));for(float z=-32.35f;z>=-45.55f;z-=.4f)centers.Add(new Vector3(31.45f,0,z));for(float x=32.45f;x<=53.05f;x+=.4f)centers.Add(new Vector3(x,0,-46.55f));foreach(var center in centers){var p=Feet(center);probes++;if(!Clear(p,1f))throw new Exception("B west/southwest blocked "+p);
   for(int n=0;n<8;n++){var q=p+new Vector3(Mathf.Cos(n*Mathf.PI/4),0,Mathf.Sin(n*Mathf.PI/4));if(!floor.Raycast(new Ray(q+Vector3.up*20,Vector3.down),out var hit,40))throw new Exception("Missing ground");int sub=ends.FindIndex(e=>hit.triangleIndex<e);if(sub<0||!materials[sub].name.Contains("Pavement"))throw new Exception("B west/southwest surface not pavement "+q);}
  }return "B west/southwest: "+probes+" probes, clear radius 1m, pavement checks passed";
 }
 static Mesh ExpandPavement(Mesh source,Transform t,Renderer renderer,Rect rect){
  var materials=renderer.sharedMaterials;int plot=Array.FindIndex(materials,m=>m.name.Contains("Plot")),pave=Array.FindIndex(materials,m=>m.name.Contains("Pavement"));
  if(plot<0||pave<0)throw new Exception("Ground materials missing");
  var vertices=source.vertices;var output=new List<Vector3>();var triangles=Enumerable.Range(0,source.subMeshCount).Select(_=>new List<int>()).ToArray();
  void Add(List<Vector3> poly,int sub){if(poly.Count<3)return;for(int k=1;k<poly.Count-1;k++){if(Vector3.Cross(poly[k]-poly[0],poly[k+1]-poly[0]).sqrMagnitude<1e-12f)continue;int b=output.Count;output.Add(t.InverseTransformPoint(poly[0]));output.Add(t.InverseTransformPoint(poly[k]));output.Add(t.InverseTransformPoint(poly[k+1]));triangles[sub].AddRange(new[]{b,b+1,b+2});}}
  // Replace plot material inside the new frontage. Split triangles at the rectangle, never overlay faces.
  var planes=new[]{new Vector3(1,0,rect.xMin),new Vector3(-1,0,-rect.xMax),new Vector3(0,1,rect.yMin),new Vector3(0,-1,-rect.yMax)};
  for(int sub=0;sub<source.subMeshCount;sub++){
   var ids=source.GetTriangles(sub);
   for(int i=0;i<ids.Length;i+=3){var poly=new List<Vector3>{t.TransformPoint(vertices[ids[i]]),t.TransformPoint(vertices[ids[i+1]]),t.TransformPoint(vertices[ids[i+2]])};
    if(sub!=plot||poly.Any(p=>Mathf.Abs(p.y)>.03f)||Vector3.Cross(poly[1]-poly[0],poly[2]-poly[0]).y<=0){Add(poly,sub);continue;}
    foreach(var plane in planes){Split(poly,plane,out var inside,out var outside);Add(outside,sub);poly=inside;if(poly.Count<3)break;}
    Add(poly,pave);
   }
  }
  var m=new Mesh{name="Ground_SidewalkStage04",indexFormat=IndexFormat.UInt32};m.SetVertices(output);m.subMeshCount=source.subMeshCount;for(int i=0;i<triangles.Length;i++)m.SetTriangles(triangles[i],i);m.RecalculateNormals();m.RecalculateBounds();return m;
 }
 static void Split(List<Vector3> poly,Vector3 plane,out List<Vector3> inside,out List<Vector3> outside){
  inside=new List<Vector3>();outside=new List<Vector3>();if(poly.Count==0)return;
  float D(Vector3 p)=>plane.x*p.x+plane.y*p.z-plane.z;
  var a=poly[poly.Count-1];float da=D(a);
  foreach(var b in poly){float db=D(b);if((da>=0)!=(db>=0)){var v=Vector3.Lerp(a,b,da/(da-db));inside.Add(v);outside.Add(v);}if(db>=0)inside.Add(b);else outside.Add(b);a=b;da=db;}
 }
}
