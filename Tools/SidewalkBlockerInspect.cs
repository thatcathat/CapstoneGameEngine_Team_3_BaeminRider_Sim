using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
public static class SidewalkBlockerInspect {public static string Read(){var s=SceneManager.GetActiveScene();Physics.SyncTransforms();var p=new Vector3(96.19f,0,-21.8f);return JsonConvert.SerializeObject(new{dirty=s.isDirty,hits=Physics.OverlapCapsule(p+Vector3.up*.8f,p+Vector3.up*1.5f,1,~0,QueryTriggerInteraction.Ignore).Select(c=>new{name=c.name,parent=c.transform.parent.name,min=c.bounds.min.ToString(),max=c.bounds.max.ToString(),position=c.transform.position.ToString()})});}}
