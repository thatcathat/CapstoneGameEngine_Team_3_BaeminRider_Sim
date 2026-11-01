using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using DeliveryRider;
public static class EastCurrentInspect {public static string Read(){var s=SceneManager.GetActiveScene();var roots=s.GetRootGameObjects();var map=roots.Single(g=>g.name=="MyeongjiMap");var net=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();return JsonConvert.SerializeObject(new{dirty=s.isDirty,ground=map.transform.Find("Ground").GetComponentInChildren<MeshFilter>().sharedMesh.name,count=net.PathCount,lamps=map.GetComponentsInChildren<Transform>().Where(t=>t.name.EndsWith("PostCollider")&&t.position.x>85&&t.position.z>-46).Select(t=>new{name=t.name,x=t.position.x,z=t.position.z}),paths=Enumerable.Range(0,net.PathCount).Where(i=>i==6||i==7||i==20||i==21||i==38||i==39||i==58||i==59||net.Path(i).connector&&net.Path(i).points.First().x>85&&net.Path(i).points.First().z>-56).Select(i=>new{id=i,next=net.Path(i).next,reverse=net.Path(i).reverse,start=net.Path(i).points.First().ToString(),end=net.Path(i).points.Last().ToString()})});}}
