using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeliveryRider;
using Newtonsoft.Json;
public static class RemainingDistrictInspect {public static string Read(){var s=SceneManager.GetActiveScene();var roots=s.GetRootGameObjects();var map=roots.Single(g=>g.name=="MyeongjiMap");var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();var data=new{scene=s.path,dirty=s.isDirty,buildings=map.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("AB01_")&&!r.name.StartsWith("AB01_A")&&!r.name.StartsWith("AB01_B")).Select(r=>new{name=r.name,min=new[]{r.bounds.min.x,r.bounds.min.z},max=new[]{r.bounds.max.x,r.bounds.max.z}}),paths=Enumerable.Range(0,net.PathCount).Where(i=>net.Path(i).points.Any(p=>p.z<-53)).Select(i=>new{id=i,connector=net.Path(i).connector,next=net.Path(i).next,reverse=net.Path(i).reverse,start=new[]{net.Path(i).points.First().x,net.Path(i).points.First().z},end=new[]{net.Path(i).points.Last().x,net.Path(i).points.Last().z}})};string json=JsonConvert.SerializeObject(data);File.WriteAllText("Docs/MapLayout/Myeongji/remaining_inspection.json",json);return json;}}
