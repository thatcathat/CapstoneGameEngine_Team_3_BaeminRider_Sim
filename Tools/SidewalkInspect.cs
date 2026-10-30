using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
public static class SidewalkInspect {
 static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
 public static string Read(){var s=SceneManager.GetActiveScene();var roots=s.GetRootGameObjects();var map=roots.Single(g=>g.name=="MyeongjiMap");return JsonConvert.SerializeObject(new{scene=s.path,dirty=s.isDirty, objects=map.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.Contains("A01")||r.name.Contains("A02")||r.name.Contains("A03")||r.name.Contains("Ground")||r.name.Contains("Signal_01")||r.name.Contains("Lamp")).Select(r=>new{name=r.name,center=V(r.bounds.center),size=V(r.bounds.size),position=V(r.transform.position)}),anchors=roots.Single(g=>g.name=="MyeongjiLocations").GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("A01")).Select(t=>new{name=t.name,p=V(t.position)}),colliders=map.GetComponentsInChildren<Collider>().Where(c=>c.bounds.center.x>0&&c.bounds.center.x<27&&c.bounds.center.z>-18).Select(c=>new{name=c.name,p=V(c.bounds.center),size=V(c.bounds.size)})});}
}
