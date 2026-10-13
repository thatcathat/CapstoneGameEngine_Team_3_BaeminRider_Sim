using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // Consumes baked planning data. All serialized Unity assets are made by Editor APIs.
    public static class FourDistrictInstaller
    {
        public const string ScenePath = "Assets/_Game/Scenes/FourDistrictPrototype.unity";
        private const string Art = "Assets/_Game/Art/FourDistrict";
        [Serializable] public class Road { public string id; public float width; public Vector3[] points; }
        [Serializable] public class Destination { public float x,y,z,outX,outZ; public string zone; public int node; public Vector3 Position => new Vector3(x,y,z); public Vector3 Outward => new Vector3(outX,0,outZ).normalized; }
        [Serializable] public class Flat { public float x,y,z,radius; }
        [Serializable] public class Layout
        {
            public int nx,nz;
            public float minX,minZ,maximumGradePercent,maximumHeight,heightScale;
            public float[] heights;
            public Road[] roads;
            public Destination[] stores,doors;
            public Destination home;
            public Flat[] flats;
        }
        public static Layout ReadLayout() => JsonUtility.FromJson<Layout>(File.ReadAllText("Docs/MapLayout/district_geometry.json"));

        [MenuItem("Delivery Rider/Create Four District Prototype")]
        public static void Create()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            { EditorSceneManager.OpenScene(ScenePath); Debug.Log("Existing four-district scene opened; no content overwritten."); return; }
            var layout=ReadLayout();
            if (layout.roads.Length!=22 || layout.heights.Length!=layout.nx*layout.nz || layout.maximumGradePercent>7)
                throw new InvalidOperationException("Invalid district geometry data.");
            Directory.CreateDirectory(Art);
            AssetDatabase.Refresh();
            var source=EditorSceneManager.OpenScene(TrafficPilotInstaller.ScenePath);
            if (!EditorSceneManager.SaveScene(source,ScenePath,true)) throw new InvalidOperationException("Could not copy source scene.");
            var scene=EditorSceneManager.OpenScene(ScenePath);
            var roots=scene.GetRootGameObjects();
            var components=roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).ToArray();
            foreach(var root in roots)
                if (new[]{"TrafficPilotWorld","DoorHazardPilot","TireHazardPilot","Incident_trigger"}.Contains(root.name)) root.SetActive(false);
            foreach(var loader in components.OfType<MapLoader>()) loader.gameObject.SetActive(false);
            foreach(var trigger in components.OfType<IncidentTrigger>()) trigger.gameObject.SetActive(false);
            foreach(var scheduler in components.OfType<TimedIncidentScheduler>()) scheduler.enabled=false;
            var world=new GameObject("FourDistrictWorld").transform;
            var texture=ImportSurface();
            var ground=Material("Ground",Color.white);
            ground.SetTexture("_BaseMap",texture);
            for(int start=0;start<layout.nz-1;start+=64) BuildGround(layout,start,Mathf.Min(64,layout.nz-1-start),ground,world);
            var markers=new GameObject("RoadCenterlines").transform; markers.SetParent(world,false);
            foreach(var road in layout.roads)
            {
                var line=new GameObject(road.id+(road.id.StartsWith("L")?"_Pedestrian":"_MainRoad")).transform;
                line.SetParent(markers,false);
                for(int i=0;i<road.points.Length;i++)
                { var p=new GameObject("Point_"+i).transform; p.SetParent(line,false); p.position=Grounded(layout,road.points[i],0); }
            }
            var flats=new GameObject("FlatAreas").transform;flats.SetParent(world,false);
            for(int i=0;i<layout.flats.Length;i++)
            { var f=layout.flats[i]; var p=new GameObject("Flat_"+i+"_R"+f.radius.ToString("F1")).transform;p.SetParent(flats,false);p.position=new Vector3(f.x,f.y,f.z); }
            var anchors=new GameObject("DeliveryLocations").transform; anchors.SetParent(world,false);
            for(int i=0;i<layout.stores.Length;i++) Landmark(layout,layout.stores[i],"Shop_"+(i+1).ToString("00"),true,anchors);
            for(int i=0;i<layout.doors.Length;i++) Landmark(layout,layout.doors[i],"Door_"+(i+1).ToString("00"),false,anchors);
            Landmark(layout,layout.home,"Home",true,anchors);
            ConnectDelivery(layout,components,roots);
            UpdateMap(layout,components.OfType<DeliveryMapView>().Single(),texture);
            var session=components.OfType<DeliverySession>().Single();
            AddTraffic(layout,session,world);
            var tag=components.OfType<TMPro.TMP_Text>().FirstOrDefault(t=>t.name=="PrototypeTag");
            if(tag!=null) tag.text="FOUR DISTRICTS / PROTOTYPE";
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save district scene.");
            Selection.activeGameObject=world.gameObject;
            if(SceneView.lastActiveSceneView!=null) SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(layout.minX+(layout.nx-1)*0.5f,0,layout.minZ+(layout.nz-1)*0.5f),new Vector3(layout.nx,20,layout.nz)),false);
            Debug.Log("FourDistrictPrototype saved: 22 roads, 12 shop/32 door anchors, shared ground, existing 6 orders, 3 cars. Play Mode not started.");
        }

        public static float Height(Layout d,float x,float z)
        {
            float fx=Mathf.Clamp(x-d.minX,0,d.nx-1.0001f),fz=Mathf.Clamp(z-d.minZ,0,d.nz-1.0001f);
            int ix=(int)fx,iz=(int)fz;float u=fx-ix,v=fz-iz;
            float a=d.heights[iz*d.nx+ix],b=d.heights[iz*d.nx+ix+1],c=d.heights[(iz+1)*d.nx+ix],e=d.heights[(iz+1)*d.nx+ix+1];
            return u+v<=1?a+(b-a)*u+(c-a)*v:e+(c-e)*(1-u)+(b-e)*(1-v);
        }
        private static Vector3 Grounded(Layout d,Vector3 p,float offset) => new Vector3(p.x,Height(d,p.x,p.z)+offset,p.z);

        private static void BuildGround(Layout d,int start,int rows,Material material,Transform parent)
        {
            var vertices=new Vector3[d.nx*(rows+1)];var uv=new Vector2[vertices.Length];var normals=new Vector3[vertices.Length];
            var triangles=new int[(d.nx-1)*rows*6];
            for(int z=0;z<=rows;z++) for(int x=0;x<d.nx;x++)
            {
                int i=z*d.nx+x,rz=start+z;
                vertices[i]=new Vector3(d.minX+x,d.heights[rz*d.nx+x],d.minZ+rz);
                uv[i]=new Vector2((float)x/(d.nx-1),(float)rz/(d.nz-1));
                float dx=d.heights[rz*d.nx+Mathf.Min(x+1,d.nx-1)]-d.heights[rz*d.nx+Mathf.Max(x-1,0)];
                float dz=d.heights[Mathf.Min(rz+1,d.nz-1)*d.nx+x]-d.heights[Mathf.Max(rz-1,0)*d.nx+x];
                normals[i]=new Vector3(-dx,2,-dz).normalized;
                if(x==d.nx-1||z==rows) continue;
                int t=(z*(d.nx-1)+x)*6;
                triangles[t]=i;triangles[t+1]=i+d.nx;triangles[t+2]=i+1;
                triangles[t+3]=i+1;triangles[t+4]=i+d.nx;triangles[t+5]=i+d.nx+1;
            }
            var mesh=new Mesh {name="DistrictGround_"+start,vertices=vertices,triangles=triangles,uv=uv,normals=normals};
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(Art+"/"+mesh.name+".asset"));
            var obj=new GameObject(mesh.name);obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial=material;
            obj.AddComponent<MeshCollider>().sharedMesh=mesh;
        }

        private static Texture2D ImportSurface()
        {
            string path=Art+"/DistrictSurface.png";
            File.Copy("Docs/MapLayout/district_surface.png",path,true);
            AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.maxTextureSize=2048;
            importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Landmark(Layout d,Destination p,string name,bool shop,Transform parent)
        {
            var root=new GameObject(p.zone+"_"+name).transform;root.SetParent(parent,false);root.position=p.Position;
            Box("EntranceMarker",p.Position+Vector3.up*.045f,new Vector3(1.3f,.07f,1.3f),Material(shop?"ShopMarker":"DoorMarker",shop?new Color(.9f,.6f,.18f):new Color(.28f,.65f,.62f)),root,false);
            Vector3 center=p.Position+p.Outward*4.5f;
            // Skip blocks at close intersections; keep the entrance and its walking apron clear.
            bool clear=d.roads.All(r=>Enumerable.Range(1,r.points.Length-1).All(i=>Distance(center,r.points[i-1],r.points[i])>r.width*.5f+(r.id.StartsWith("L")?0:3)+3));
            if(clear)
            {
                float h=shop?5:6.5f;
                Box("Building",Grounded(d,center,h*.5f-.2f),new Vector3(4,h,4),Material("Building_"+p.zone,ZoneColor(p.zone)),root,true);
            }
        }
        private static float Distance(Vector3 p,Vector3 a,Vector3 b)
        { p.y=a.y=b.y=0;var ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,ab)/Mathf.Max(.0001f,ab.sqrMagnitude));return Vector3.Distance(p,a+ab*t); }
        private static Color ZoneColor(string zone)
        { switch(zone){case "A":return new Color(.84f,.7f,.43f);case "B":return new Color(.49f,.66f,.78f);case "C":return new Color(.5f,.7f,.51f);default:return new Color(.72f,.57f,.69f);} }

        private static void ConnectDelivery(Layout d,Component[] components,GameObject[] roots)
        {
            var rider=components.OfType<RiderMotor>().Single();
            rider.transform.position=Grounded(d,d.home.Position,1.08f);
            Set(rider,"followSlopes",p=>p.boolValue=true);
            var camera=components.OfType<Camera>().Single(c=>c.CompareTag("MainCamera"));
            camera.transform.position=rider.transform.position+new Vector3(0,5,-7);
            camera.transform.LookAt(rider.transform.position+Vector3.up);
            camera.farClipPlane=Mathf.Max(camera.farClipPlane,800);
            var pick=roots.Single(g=>g.name=="Pick_Up_Spot").transform;
            int[] shops={0,2,7};
            for(int i=0;i<shops.Length;i++)
            {
                var destination=d.stores[shops[i]];var group=pick.Find("Spot_"+(i+1));
                group.position=Grounded(d,destination.Position,1.018f);
                foreach(Transform child in group)
                    if(child.name.StartsWith("ShopCounter")) child.position=Grounded(d,destination.Position+destination.Outward*2.8f,.62f);
            }
            int residentIndex=0;
            foreach(var resident in components.OfType<DeliveryResident>())
            {
                Vector3 p;
                switch(resident.name)
                {
                    case "Customer":p=d.doors[24].Position;break;
                    case "Customer_South":p=d.doors[16].Position;break;
                    case "Customer_North":p=d.doors[0].Position;break;
                    default:p=d.home.Position+new Vector3(6+residentIndex++*3,0,5);break;
                }
                resident.transform.position=Grounded(d,p,1.08f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(resident.transform);
            }
        }

        private static void UpdateMap(Layout d,DeliveryMapView map,Texture2D texture)
        {
            var so=new SerializedObject(map);
            var area=(RectTransform)so.FindProperty("mapArea").objectReferenceValue;
            var background=area.Find("MapGeometryBackground");
            if(background==null||background.GetComponent<UnityEngine.UI.RawImage>()==null) throw new InvalidOperationException("Expected existing RawImage map background.");
            background.GetComponent<UnityEngine.UI.RawImage>().texture=texture;
            so.FindProperty("worldMinimum").vector2Value=new Vector2(d.minX,d.minZ);
            so.FindProperty("worldSize").vector2Value=new Vector2(d.nx-1,d.nz-1);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddTraffic(Layout d,DeliverySession session,Transform parent)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/TrafficVan.prefab");
            foreach(string roadId in new[]{"B1","B6"})
            {
                var r=d.roads.Single(x=>x.id==roadId);int segment=roadId=="B1"?0:r.points.Length-2;
                Vector3 a=r.points[segment],b=r.points[segment+1];a.y=b.y=0;
                Vector3 forward=(b-a).normalized,side=Vector3.Cross(Vector3.up,forward);
                a+=forward*18;b-=forward*18;float length=Vector3.Distance(a,b);const float radius=1.65f;
                var points=new List<Vector3>();
                for(float t=0;t<length;t+=1) points.Add(Grounded(d,a+forward*t+side*radius,.85f));
                for(int i=0;i<24;i++){float angle=i*Mathf.PI/24;points.Add(Grounded(d,b+side*(radius*Mathf.Cos(angle))+forward*(radius*Mathf.Sin(angle)),.85f));}
                for(float t=0;t<length;t+=1) points.Add(Grounded(d,b-forward*t-side*radius,.85f));
                for(int i=0;i<24;i++){float angle=i*Mathf.PI/24;points.Add(Grounded(d,a-side*(radius*Mathf.Cos(angle))-forward*(radius*Mathf.Sin(angle)),.85f));}
                var lane=new GameObject("TrafficLane_"+roadId).AddComponent<TrafficRoute>();lane.transform.SetParent(parent,false);lane.Configure(points.ToArray());
                int cars=roadId=="B1"?2:1;
                for(int i=0;i<cars;i++)
                {
                    var obj=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);obj.name="Traffic_"+roadId+"_"+i;
                    float start=10+i*lane.Length*.5f;lane.Sample(start,out var p,out var f);obj.transform.SetPositionAndRotation(p,Quaternion.LookRotation(f));
                    var car=obj.GetComponent<TrafficVehicle>();
                    Set(car,"session",v=>v.objectReferenceValue=session);Set(car,"route",v=>v.objectReferenceValue=lane);
                    Set(car,"junction",v=>v.objectReferenceValue=null);Set(car,"startDistance",v=>v.floatValue=start);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(obj.transform);
                }
            }
        }
        private static Material Material(string name,Color color)
        {
            string path=Art+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color,enableInstancing=true};mat.SetFloat("_Smoothness",.1f);AssetDatabase.CreateAsset(mat,path);return mat;
        }
        private static GameObject Box(string name,Vector3 p,Vector3 size,Material material,Transform parent,bool solid)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.position=p;obj.transform.localScale=size;
            obj.GetComponent<Renderer>().sharedMaterial=material;if(!solid)UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;
        }
        private static void Set(UnityEngine.Object target,string property,Action<SerializedProperty> write)
        {var so=new SerializedObject(target);write(so.FindProperty(property));so.ApplyModifiedPropertiesWithoutUndo();}
    }
}
