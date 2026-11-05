using System;using System.IO;using UnityEngine;using UnityEngine.SceneManagement;using UnityEditor;using UnityEditor.SceneManagement;
public static class GroundFloorFinalPreview {public static string Render(){if(EditorApplication.isPlaying)throw new Exception("Edit mode required");var host=new GameObject("Temporary_FacadePreview");var camera=host.AddComponent<Camera>();var rt=new RenderTexture(1600,750,24);Texture2D texture=null;var previous=RenderTexture.active;
 try{host.transform.position=new Vector3(103,5.5f,-107);host.transform.LookAt(new Vector3(103,2.8f,-93));camera.fieldOfView=65;camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=250;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture=new Texture2D(1600,750,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,750),0,0);texture.Apply();var path="C:/Users/PC/Documents/Codex/Myeongji_District/GroundFloor_Final_After_20261105.png";File.WriteAllBytes(path,texture.EncodeToPNG());return path;}
 finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());}}}




