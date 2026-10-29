using System;
using System.Collections.Generic;
using System.Linq;
using DeliveryRider;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MyeongjiSignals
{
    public static string Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying || EditorApplication.isCompiling || scene.isDirty || scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity") throw new Exception("Saved Myeongji scene in edit mode required.");
        var roots=scene.GetRootGameObjects();
        if(roots.Any(g=>g.name=="MyeongjiSignals"))throw new Exception("Signals already installed.");
        var traffic=roots.Single(g=>g.name=="Traffic").GetComponent<TrafficNetwork>();
        var session=roots.Single(g=>g.name=="DeliverySession").GetComponent<DeliverySession>();
        var renderers=roots.Single(g=>g.name=="MyeongjiMap").GetComponentsInChildren<Renderer>(true);
        float[] centers={8f,36f,101f};
        var reds=new GameObject[6];var greens=new GameObject[6];var heads=new Renderer[6];
        for(int i=0;i<6;i++) {
            string id=(i+1).ToString("00");
            reds[i]=renderers.Single(r=>r.name.StartsWith("PED_"+id+"_Red_Stop")).gameObject;
            greens[i]=renderers.Single(r=>r.name.StartsWith("PED_"+id+"_Green_Walk")).gameObject;
            heads[i]=renderers.Single(r=>r.name=="TrafficSignal_"+id+".001");
        }
        var stops=new List<TrafficSignalStop>();var counts=new List<int>();
        var parent=new GameObject("MyeongjiSignals");
        for(int n=0;n<3;n++) {
            var go=new GameObject("Crossing_"+(n+1).ToString("00"));go.transform.SetParent(parent.transform);go.transform.position=new Vector3(centers[n],0,0);
            var signal=go.AddComponent<PedestrianCrossingSignal>();
            var so=new SerializedObject(signal);so.FindProperty("session").objectReferenceValue=session;so.FindProperty("traffic").objectReferenceValue=traffic;
            SetArray(so,"pedestrianRed",new UnityEngine.Object[]{reds[2*n],reds[2*n+1]});
            SetArray(so,"pedestrianGreen",new UnityEngine.Object[]{greens[2*n],greens[2*n+1]});
            SetArray(so,"vehicleHeads",new UnityEngine.Object[]{heads[2*n],heads[2*n+1]});so.ApplyModifiedPropertiesWithoutUndo();
            int before=stops.Count;
            for(int p=0;p<traffic.PathCount;p++) {
                var path=traffic.Path(p);
                for(int k=1;k<path.points.Length;k++) {
                    var a=path.points[k-1];var b=path.points[k];float dx=b.x-a.x;if(Mathf.Abs(dx)<.0001f)continue;
                    float line=centers[n]-Mathf.Sign(dx)*2.35f;
                    float t=(line-a.x)/dx;if(t<0f||t>1f)continue;
                    var hit=Vector3.Lerp(a,b,t);if(Mathf.Abs(hit.z)>4.05f)continue;
                    float along=Mathf.Lerp(path.cumulative[k-1],path.cumulative[k],t);
                    if(stops.Any(s=>s.path==p&&s.signal==signal&&Mathf.Abs(s.distance-along)<.1f))continue;
                    stops.Add(new TrafficSignalStop{path=p,distance=along,signal=signal});
                }
            }
            counts.Add(stops.Count-before);
        }
        if(counts.Any(c=>c<2)) {UnityEngine.Object.DestroyImmediate(parent);throw new Exception("Missing crossing approaches.");}
        var net=new SerializedObject(traffic);var array=net.FindProperty("signalStops");array.arraySize=stops.Count;
        for(int i=0;i<stops.Count;i++) {var item=array.GetArrayElementAtIndex(i);item.FindPropertyRelative("path").intValue=stops[i].path;item.FindPropertyRelative("distance").floatValue=stops[i].distance;item.FindPropertyRelative("signal").objectReferenceValue=stops[i].signal;}
        net.ApplyModifiedPropertiesWithoutUndo();
        foreach(var mat in heads.SelectMany(h=>h.sharedMaterials).Concat(renderers.Where(r=>r.name.StartsWith("PED_")).SelectMany(r=>r.sharedMaterials)).Distinct()) {
            if(!mat.name.StartsWith("ST_SignalRed")&&!mat.name.StartsWith("ST_SignalAmber")&&!mat.name.StartsWith("ST_SignalGreen")&&!mat.name.StartsWith("PED_Red_On")&&!mat.name.StartsWith("PED_Green_On"))continue;
            if(!AssetDatabase.GetAssetPath(mat).StartsWith("Assets/_Game/Art/Myeongji/Materials/"))throw new Exception("Unexpected shared material: "+mat.name);
            mat.EnableKeyword("_EMISSION");
            bool red=mat.name.StartsWith("ST_SignalRed")||mat.name.StartsWith("PED_Red_On");bool green=mat.name.StartsWith("PED_Green_On");
            Color color=red?new Color(1f,.04f,.015f):green?new Color(.02f,1f,.16f):new Color(.035f,.045f,.045f);
            mat.SetColor("_BaseColor",color);mat.SetColor("_EmissionColor",red||green?color*2f:Color.black);EditorUtility.SetDirty(mat);
        }
        for(int i=0;i<6;i++){reds[i].SetActive(true);greens[i].SetActive(false);}
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Saved 3 crossings / 6 pedestrian signal pairs. Stop approaches: "+string.Join(",",counts)+". "+Review();
    }
    private static void SetArray(SerializedObject so,string name,UnityEngine.Object[] objects){var a=so.FindProperty(name);a.arraySize=objects.Length;for(int i=0;i<objects.Length;i++)a.GetArrayElementAtIndex(i).objectReferenceValue=objects[i];}
    public static string Review(){
        var scene=SceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();int missing=0;
        foreach(var root in roots)foreach(var t in root.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        var traffic=roots.Single(g=>g.name=="Traffic").GetComponent<TrafficNetwork>();
        var net=new SerializedObject(traffic);var stops=net.FindProperty("signalStops");
        for(int i=0;i<stops.arraySize;i++){var s=stops.GetArrayElementAtIndex(i);int path=s.FindPropertyRelative("path").intValue;float d=s.FindPropertyRelative("distance").floatValue;if(path<0||path>=traffic.PathCount||d<0||d>traffic.Path(path).length||s.FindPropertyRelative("signal").objectReferenceValue==null)throw new Exception("Invalid stop line");}
        if(missing!=0)throw new Exception("Missing scripts: "+missing);
        return "Traffic="+traffic.gameObject.activeSelf+", Pedestrians="+roots.Single(g=>g.name=="Pedestrians").activeSelf+", missing scripts=0, scene saved="+!scene.isDirty;
    }
}
