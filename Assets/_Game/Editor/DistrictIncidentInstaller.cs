using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class DistrictIncidentInstaller
    {
        [MenuItem("Delivery Rider/Install District Incident Course")]
        public static void Install()
        {
            if(EditorApplication.isPlaying || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            for(int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var scene=EditorSceneManager.OpenScene(FourDistrictInstaller.ScenePath);
            var roots=scene.GetRootGameObjects();
            if(roots.Any(g=>g.name=="RepresentativeIncidents")) { Debug.Log("Existing incident course preserved."); return; }
            var components=roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).ToArray();
            var session=components.OfType<DeliverySession>().Single();
            var sourceDoor=components.OfType<SwingDoorHazard>().Single();
            var sourceTire=components.OfType<RollingTireHazard>().Single(t=>t.name=="DoorChainTire");
            var sourceTheft=components.OfType<IncidentTrigger>().Single(t=>t.name=="WestTheftZone");
            var d=FourDistrictInstaller.ReadLayout();
            var lane=d.roads.Single(r=>r.id=="L8");
            var flat=d.roads.Single(r=>r.id=="L7");
            var parent=new GameObject("RepresentativeIncidents").transform;
            Vector3 forward=(lane.points[1]-lane.points[0]);forward.y=0;forward.Normalize();
            Vector3 manhole=Ground(d,lane.points[0]+forward*20);
            CreateGround(d,session,GroundHazardKind.Manhole,manhole,forward,parent);
            CreateGround(d,session,GroundHazardKind.Melon,Ground(d,manhole+forward*6),forward,parent);
            // The former pilot remains inactive; clone only the required authored objects.
            Vector3 along=flat.points[1]-flat.points[0];along.y=0;along.Normalize();
            Vector3 right=Vector3.Cross(Vector3.up,along);
            Vector3 doorPoint=Ground(d,flat.points[0]+along*35-right*2.25f);
            var door=UnityEngine.Object.Instantiate(sourceDoor,parent);
            door.name="DistrictDoor";door.gameObject.SetActive(true);
            door.transform.SetPositionAndRotation(doorPoint+Vector3.up*.03f,Quaternion.LookRotation(along));
            var tire=UnityEngine.Object.Instantiate(sourceTire,parent);
            tire.name="DistrictDoorTire";tire.gameObject.SetActive(true);
            tire.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var ts=new SerializedObject(tire);
            ts.FindProperty("linkedDoor").objectReferenceValue=door;
            ts.FindProperty("session").objectReferenceValue=session;
            ts.FindProperty("proximityActivation").boolValue=false;
            var path=ts.FindProperty("localPath");path.arraySize=17;
            var positions=new Vector3[17];
            for(int i=0;i<positions.Length;i++)
            {
                positions[i]=Ground(d,doorPoint+right*(8-i*.5f)-along*2)+Vector3.up*.65f;
                path.GetArrayElementAtIndex(i).vector3Value=positions[i];
            }
            ts.FindProperty("approachCenter").vector3Value=positions[0];
            ts.ApplyModifiedPropertiesWithoutUndo();
            var wheel=(Transform)ts.FindProperty("wheel").objectReferenceValue;
            wheel.position=positions[0];wheel.rotation=Quaternion.LookRotation(positions[1]-positions[0]);
            var lamp=(Renderer)ts.FindProperty("warningLamp").objectReferenceValue;
            lamp.transform.position=positions[0]+Vector3.up;lamp.enabled=false;

            var theft=UnityEngine.Object.Instantiate(sourceTheft,parent);
            theft.name="DistrictTheft";theft.gameObject.SetActive(true);
            theft.transform.SetPositionAndRotation(Ground(d,lane.points[0]+forward*9)+Vector3.up*1.5f,Quaternion.LookRotation(forward));
            theft.transform.localScale=Vector3.one;
            var box=theft.GetComponent<BoxCollider>();box.center=Vector3.zero;box.size=new Vector3(3.8f,3,3);box.isTrigger=true;
            var settings=new SerializedObject(theft);
            settings.FindProperty("preserveActiveIncident").boolValue=true;
            settings.FindProperty("deferWhileMapOpen").boolValue=true;
            settings.FindProperty("allowTimed").boolValue=false;
            settings.FindProperty("actorSpawn").objectReferenceValue=theft.transform;
            settings.FindProperty("impactDirection").objectReferenceValue=theft.transform;
            var spawn=new GameObject("ThiefSpawn").transform;spawn.SetParent(parent,false);
            spawn.position=Ground(d,lane.points[0]+forward*14)+Vector3.up*1.08f;
            settings.FindProperty("thiefSpawn").objectReferenceValue=spawn;
            var routeRoot=new GameObject("ThiefRoute").transform;routeRoot.SetParent(parent,false);
            var route=new List<Transform>();float traversed=0;
            for(int i=1;i<lane.points.Length;i++)
            {
                Vector3 a=lane.points[i-1],b=lane.points[i];a.y=b.y=0;
                float length=Vector3.Distance(a,b);int steps=Mathf.CeilToInt(length/2);
                for(int j=1;j<=steps;j++)
                {
                    float distance=length*j/steps;if(traversed+distance<=15)continue;
                    var p=new GameObject("Point_"+route.Count).transform;p.SetParent(routeRoot,false);
                    p.position=Ground(d,Vector3.Lerp(a,b,(float)j/steps))+Vector3.up*1.08f;route.Add(p);
                }
                traversed+=length;
            }
            var escape=settings.FindProperty("escapeRoute");escape.arraySize=route.Count;
            for(int i=0;i<route.Count;i++)escape.GetArrayElementAtIndex(i).objectReferenceValue=route[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
            Shape("TheftMarker",PrimitiveType.Cylinder,Ground(d,lane.points[0]+forward*9+Vector3.Cross(Vector3.up,forward)*2.7f)+Vector3.up*.7f,
                new Vector3(.25f,.7f,.25f),Material("TheftMarker",new Color(.6f,.25f,.85f)),parent);
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save incident course.");
            Selection.activeGameObject=parent.gameObject;
            if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.Frame(new Bounds(manhole,new Vector3(70,14,70)),false);
            Debug.Log("District incidents saved: L7 door/tire, L8 theft/manhole/melon. No Play Mode verification.");
        }

        private static Vector3 Ground(FourDistrictInstaller.Layout d,Vector3 p)
        {p.y=FourDistrictInstaller.Height(d,p.x,p.z);return p;}
        internal static GameObject CreateGround(FourDistrictInstaller.Layout d,DeliverySession session,GroundHazardKind kind,Vector3 position,Vector3 forward,Transform parent)
        {
            var obj=new GameObject(kind.ToString());obj.transform.SetParent(parent,false);obj.transform.position=position;
            float dx=FourDistrictInstaller.Height(d,position.x+.5f,position.z)-FourDistrictInstaller.Height(d,position.x-.5f,position.z);
            float dz=FourDistrictInstaller.Height(d,position.x,position.z+.5f)-FourDistrictInstaller.Height(d,position.x,position.z-.5f);
            Vector3 normal=new Vector3(-dx,1,-dz).normalized;
            obj.transform.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,normal),normal);
            var volume=obj.AddComponent<BoxCollider>();volume.isTrigger=true;
            bool manhole=kind==GroundHazardKind.Manhole;
            volume.center=new Vector3(0,manhole ? .65f : .16f,0);
            volume.size=manhole?new Vector3(1.8f,1.5f,1.8f):new Vector3(2.1f,.4f,4.5f);
            var hazard=obj.AddComponent<GroundContactHazard>();
            var material=Material(manhole?"ManholeIron":"MelonPulp",manhole?new Color(.18f,.22f,.25f):new Color(.9f,.2f,.25f));
            var plate=Shape(manhole?"Lid":"Pulp",manhole?PrimitiveType.Cylinder:PrimitiveType.Cube,Vector3.zero,
                manhole?new Vector3(1.7f,.035f,1.7f):new Vector3(2.1f,.045f,4.5f),material,obj.transform);
            plate.transform.localPosition=new Vector3(0,.035f,0);plate.transform.localRotation=Quaternion.identity;
            if(!manhole)for(int i=0;i<7;i++)
            {
                var piece=Shape("Rind",PrimitiveType.Sphere,Vector3.zero,new Vector3(.35f,.18f,.45f),Material("MelonRind",new Color(.15f,.5f,.2f)),obj.transform);
                piece.transform.localPosition=new Vector3((i%2==0?-.6f:.6f),.1f,-1.5f+i*.5f);
            }
            var lamp=Shape("Warning",PrimitiveType.Cylinder,Vector3.zero,new Vector3(1.9f,.02f,1.9f),Material("HazardWarning",new Color(1,.65f,.08f)),obj.transform);
            lamp.transform.localPosition=new Vector3(0,.09f,0);lamp.transform.localRotation=Quaternion.identity;
            var renderer=lamp.GetComponent<Renderer>();renderer.enabled=false;
            var steam=Shape("Burst",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.65f,.75f,.65f),Material("Steam",new Color(.85f,.95f,1)),obj.transform);
            steam.transform.localPosition=new Vector3(0,.8f,0);steam.transform.localRotation=Quaternion.identity;steam.SetActive(false);
            var so=new SerializedObject(hazard);so.FindProperty("session").objectReferenceValue=session;
            so.FindProperty("kind").enumValueIndex=(int)kind;so.FindProperty("warningLamp").objectReferenceValue=renderer;
            so.FindProperty("effect").objectReferenceValue=steam;so.ApplyModifiedPropertiesWithoutUndo();
            return obj;
        }
        internal static GameObject Shape(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material,Transform parent)
        {var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.position=position;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;}
        internal static Material Material(string name,Color color)
        {
            string path="Assets/_Game/Art/FourDistrict/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};mat.SetFloat("_Smoothness",.1f);AssetDatabase.CreateAsset(mat,path);return mat;
        }
    }
}
