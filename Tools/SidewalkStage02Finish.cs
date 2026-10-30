using System;
using System.Linq;
using System.IO;
using DeliveryRider;
using DeliveryRider.Editor;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class SidewalkStage02Finish {
 public static string Save(){EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "Scene saved after temporary preview cleanup";}
 public static string Apply(){
  var scene=SceneManager.GetActiveScene();if(scene.isDirty||EditorApplication.isPlaying||scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity")throw new Exception("Saved Myeongji edit scene required");
  var table=AssetDatabase.LoadAssetAtPath<DeliveryRouteTable>("Assets/_Game/Settings/MyeongjiRouteTable.asset");JsonUtility.FromJsonOverwrite(File.ReadAllText("Docs/MapLayout/Myeongji/route_table.json"),table);if(!table.IsValid)throw new Exception("Invalid route table");EditorUtility.SetDirty(table);
  AssetDatabase.ImportAsset("Assets/_Game/Art/Myeongji/Map/MyeongjiMap.png");
  var result=MusclemanInstaller.ApplyToScene(scene);
  var map=scene.GetRootGameObjects().Single(g=>g.name=="MyeongjiMap");
  PrefabUtility.SaveAsPrefabAsset(map,"Assets/_Game/Prefabs/Myeongji/MyeongjiMap.prefab",out bool success);if(!success)throw new Exception("Prefab save failed");
  AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return result+" Map prefab and distance table saved.";
 }
 public static string Preview(){
  var host=new GameObject("Temporary_Sidewalk_Review");var camera=host.AddComponent<Camera>();var rt=new RenderTexture(1000,750,24);Texture2D texture=null;var previous=RenderTexture.active;
  try{host.transform.position=new Vector3(49,55,3);host.transform.LookAt(new Vector3(13.5f,1,-26));camera.orthographic=true;camera.orthographicSize=31;camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=250;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture=new Texture2D(1000,750,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1000,750),0,0);texture.Apply();var file="C:/Users/PC/Documents/Codex/Myeongji_District/SidewalkStage02_preview.png";File.WriteAllBytes(file,texture.EncodeToPNG());return file;}
  finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);}
 }
}
