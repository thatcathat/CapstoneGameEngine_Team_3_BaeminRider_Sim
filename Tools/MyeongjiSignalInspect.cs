using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class MyeongjiSignalInspect
{
    public static string Refresh(){AssetDatabase.Refresh();return "Refresh requested";}
    public static string Read(){
        var roots=SceneManager.GetActiveScene().GetRootGameObjects();
        var map=roots.Single(g=>g.name=="MyeongjiMap");
        return string.Join("\n",map.GetComponentsInChildren<Transform>(true).Where(t=>(t.name.StartsWith("PED_")||t.name.StartsWith("TrafficSignal_")) && t.GetComponent<Renderer>() != null).Select(t=>t.name+" | "+t.position+" | "+string.Join(",",t.GetComponent<Renderer>()?.sharedMaterials.Select(m=>m.name)??new string[0])));
    }
}

