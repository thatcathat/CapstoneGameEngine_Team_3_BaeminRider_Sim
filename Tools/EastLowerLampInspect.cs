using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
public static class EastLowerLampInspect {public static string Read(){var root=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="MyeongjiMap");return JsonConvert.SerializeObject(root.GetComponentsInChildren<Transform>().Where(t=>t.name.EndsWith("PostCollider")&&t.position.z<-46&&t.position.x>64).Select(t=>new{name=t.name,x=t.position.x,z=t.position.z}));}}

