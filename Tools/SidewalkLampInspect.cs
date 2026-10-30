using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
public static class SidewalkLampInspect {public static string Read(){var root=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="MyeongjiMap");return JsonConvert.SerializeObject(root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("StreetLamp_17")||t.name.Contains("StreetLamp_18")).Select(t=>new{name=t.name,parent=t.parent.name,pos=t.position.ToString(),bounds=t.GetComponent<Renderer>()!=null?t.GetComponent<Renderer>().bounds.ToString():"none"}));}}

