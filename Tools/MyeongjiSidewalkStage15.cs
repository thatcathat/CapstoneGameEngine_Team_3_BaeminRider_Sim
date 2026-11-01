using System;using System.Linq;using System.Collections.Generic;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.SceneManagement;using UnityEditor;using UnityEditor.SceneManagement;
public static class MyeongjiSidewalkStage15 {
 public static string Apply(){
 var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
 var floor=scene.GetRootGameObjects().Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();const string path="Assets/_Game/Art/Myeongji/Geometry/Ground_SidewalkStage15.asset";
 if(floor.sharedMesh.name!="Ground_SidewalkStage14"||AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)throw new Exception("Expected Stage14 ground");
 var old=floor.sharedMesh;var mesh=old;try{
 foreach(var rect in new[]{Rect.MinMaxRect(-10,4,125,7.3f),Rect.MinMaxRect(-6.3f,-100,-3,-4),Rect.MinMaxRect(8.5f,-109.3f,122.3f,-106),Rect.MinMaxRect(119,-109.3f,122.3f,-46)}){var next=ExpandPavement(mesh,floor.transform,floor.GetComponent<Renderer>(),rect);if(mesh!=old)UnityEngine.Object.DestroyImmediate(mesh);mesh=next;}
 AssetDatabase.CreateAsset(mesh,path);floor.sharedMesh=mesh;floor.GetComponent<MeshFilter>().sharedMesh=mesh;PrefabUtility.RecordPrefabInstancePropertyModifications(floor);PrefabUtility.RecordPrefabInstancePropertyModifications(floor.GetComponent<MeshFilter>());AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "Outer north/west/south/east pavement expanded without changing carriageways or buildings";
 }catch{if(mesh!=old&&!AssetDatabase.Contains(mesh))UnityEngine.Object.DestroyImmediate(mesh);throw;}}
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
  var m=new Mesh{name="Ground_SidewalkStage15",indexFormat=IndexFormat.UInt32};m.SetVertices(output);m.subMeshCount=source.subMeshCount;for(int i=0;i<triangles.Length;i++)m.SetTriangles(triangles[i],i);m.RecalculateNormals();m.RecalculateBounds();return m;
 }
 static void Split(List<Vector3> poly,Vector3 plane,out List<Vector3> inside,out List<Vector3> outside){
  inside=new List<Vector3>();outside=new List<Vector3>();if(poly.Count==0)return;
  float D(Vector3 p)=>plane.x*p.x+plane.y*p.z-plane.z;
  var a=poly[poly.Count-1];float da=D(a);
  foreach(var b in poly){float db=D(b);if((da>=0)!=(db>=0)){var v=Vector3.Lerp(a,b,da/(da-db));inside.Add(v);outside.Add(v);}if(db>=0)inside.Add(b);else outside.Add(b);a=b;da=db;}
 }
}
