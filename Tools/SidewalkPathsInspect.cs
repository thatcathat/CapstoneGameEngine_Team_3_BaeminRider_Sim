using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeliveryRider;
using Newtonsoft.Json;
public static class SidewalkPathsInspect {
 public static string Read(){var net=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();return JsonConvert.SerializeObject(Enumerable.Range(0,net.PathCount).Where(i=>net.Path(i).points.Any(p=>p.x>=3&&p.x<=24.5&&p.z>=-48.5&&p.z<=-4)).Select(i=>new{id=i,connector=net.Path(i).connector,next=net.Path(i).next,reverse=net.Path(i).reverse,length=net.Path(i).length,start=net.Path(i).points.First().ToString(),end=net.Path(i).points.Last().ToString()}));}
}

