using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
public static class SidewalkEastSurface {public static string Read(){var floor=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();var mesh=floor.sharedMesh;var mats=floor.GetComponent<Renderer>().sharedMaterials;var ends=Enumerable.Range(0,mesh.subMeshCount).Select(i=>Enumerable.Range(0,i+1).Sum(j=>(int)mesh.GetIndexCount(j)/3)).ToArray();return JsonConvert.SerializeObject(Enumerable.Range(0,78).Select(i=>{float z=-7-i*.5f,x=116+32f/52f*z-5.45f*Mathf.Sqrt(1+32f*32f/(52f*52f));floor.Raycast(new Ray(new Vector3(x,20,z),Vector3.down),out var h,40);return new{z,mat=mats[System.Array.FindIndex(ends,e=>h.triangleIndex<e)].name,y=h.point.y};}).Where(p=>p.mat.Contains("Asphalt")||p.mat.Contains("Curb")));}}
