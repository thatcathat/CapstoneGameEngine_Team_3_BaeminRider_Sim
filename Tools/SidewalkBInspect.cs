using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeliveryRider;
using Newtonsoft.Json;
public static class SidewalkBInspect {
 public static string Read(){var s=SceneManager.GetActiveScene();var roots=s.GetRootGameObjects();var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();var map=roots.Single(g=>g.name=="MyeongjiMap");return JsonConvert.SerializeObject(new{scene=s.path,dirty=s.isDirty,ground=map.transform.Find("Ground").GetComponentInChildren<MeshFilter>().sharedMesh.name,buildings=map.GetComponentsInChildren<Renderer>().Where(r=>Enumerable.Range(1,5).Any(i=>r.name=="AB01_B"+i.ToString("00")+".001")).Select(r=>new{name=r.name,min=r.bounds.min.ToString(),max=r.bounds.max.ToString()}),paths=Enumerable.Range(0,net.PathCount).Where(i=>net.Path(i).points.Any(p=>p.x>=25&&p.x<=110&&p.z<=-4&&p.z>=-8)).Select(i=>new{id=i,connector=net.Path(i).connector,next=net.Path(i).next,reverse=net.Path(i).reverse,length=net.Path(i).length,start=net.Path(i).points.First().ToString(),end=net.Path(i).points.Last().ToString()})});}
}
