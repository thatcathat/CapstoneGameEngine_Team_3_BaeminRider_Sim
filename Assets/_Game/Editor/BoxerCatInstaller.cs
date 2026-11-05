using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace DeliveryRider.Editor
{
    public static class BoxerCatInstaller
    {
        [MenuItem("Delivery Rider/Apply Boxer Cat")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Edit mode only.");
            GameScenes.RequireActive();
            var planner = Object.FindFirstObjectByType<HazardPlanner>();
            var data = new SerializedObject(planner);
            var settings = (HazardSpawnSettings)data.FindProperty("settings").objectReferenceValue;
            var candidates = (HazardCandidates)data.FindProperty("candidates").objectReferenceValue;
            var places = candidates.Candidates.Where(c => c.type != "boxercat").ToList();
            var bases = places.Where(c => c.type == "chicken").ToArray();
            if (bases.Length == 0) throw new System.InvalidOperationException("No chicken placement candidates.");
            var root = new GameObject("BoxerCat");
            root.SetActive(false);
            try
            {
                var coat = Material("BoxerCatCoat", new Color(0.72f, 0.42f, 0.18f));
                var red = Material("BoxerCatGlove", new Color(0.8f, 0.04f, 0.03f));
                var dark = Material("BoxerCatEye", new Color(0.03f, 0.025f, 0.02f));
                var torso = Part(root.transform,"Torso",new Vector3(0,.45f,0),new Vector3(.5f,.7f,.4f),coat);
                var head = Part(root.transform,"Head",new Vector3(0,.6f,.45f),Vector3.one*.5f,coat);
                for (int side = -1; side <= 1; side += 2)
                {
                    Part(head,"Ear",new Vector3(side*.32f,.44f,0),new Vector3(.3f,.5f,.25f),coat,PrimitiveType.Cube);
                    Part(head,"Eye",new Vector3(side*.22f,.1f,.44f),Vector3.one*.14f,dark);
                    Part(root.transform,"RearPaw",new Vector3(side*.2f,.15f,-.25f),new Vector3(.2f,.3f,.25f),coat);
                }
                Part(root.transform,"Tail",new Vector3(0,.55f,-.5f),new Vector3(.13f,.13f,.65f),coat);
                var left = Part(root.transform,"LeftPaw",new Vector3(-.25f,.2f,.35f),Vector3.one*.22f,coat);
                var right = Part(root.transform,"RightPaw",new Vector3(.25f,.2f,.35f),Vector3.one*.22f,coat);
                var lg = Part(left,"Glove",Vector3.zero,Vector3.one*1.5f,red).gameObject;
                var rg = Part(right,"Glove",Vector3.zero,Vector3.one*1.5f,red).gameObject;
                lg.SetActive(false); rg.SetActive(false);
                var collider = root.AddComponent<CapsuleCollider>(); collider.center = Vector3.up*.7f; collider.height=1.4f; collider.radius=.35f; collider.isTrigger=false; var body=root.AddComponent<Rigidbody>(); body.isKinematic=true; body.useGravity=false;
                var hazard = root.AddComponent<BoxerCatHazard>();
                var serialized = new SerializedObject(hazard);
                serialized.FindProperty("torso").objectReferenceValue=torso;
                serialized.FindProperty("head").objectReferenceValue=head;
                serialized.FindProperty("leftHand").objectReferenceValue=left;
                serialized.FindProperty("rightHand").objectReferenceValue=right;
                var gloves=serialized.FindProperty("gloves"); gloves.arraySize=2;
                gloves.GetArrayElementAtIndex(0).objectReferenceValue=lg; gloves.GetArrayElementAtIndex(1).objectReferenceValue=rg;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/_Game/Prefabs/Hazards/BoxerCat.prefab");
                foreach(var spot in bases) { var copy=spot; copy.type="boxercat"; places.Add(copy); }
                candidates.Configure(candidates.Spots.ToArray(),places.ToArray()); EditorUtility.SetDirty(candidates);
                var kinds=settings.Kinds.ToList(); var kind=kinds.FirstOrDefault(k=>k.type=="boxercat");
                if(kind==null) { kind=new HazardSpawnSettings.Kind { type="boxercat",role=HazardRole.Trigger,unlockDay=1,poolSize=3 }; kinds.Add(kind); }
                kind.prefab=prefab; settings.Configure(kinds.ToArray()); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
                Debug.Log($"Boxer cat installed: {bases.Length} candidates, {kind.poolSize} per day. No play validation.");
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Transform Part(Transform parent,string name,Vector3 position,Vector3 scale,Material material,PrimitiveType shape=PrimitiveType.Sphere)
        {
            var obj=GameObject.CreatePrimitive(shape); obj.name=name; obj.transform.SetParent(parent,false); obj.transform.localPosition=position; obj.transform.localScale=scale;
            Object.DestroyImmediate(obj.GetComponent<Collider>()); obj.GetComponent<Renderer>().sharedMaterial=material; return obj.transform;
        }
        private static Material Material(string name,Color color)
        {
            string path="Assets/_Game/Art/Materials/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null) { var source=AssetDatabase.FindAssets("t:Material",new[]{"Assets/_Game/Art/Materials"}).Select(g=>AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(m=>m.shader.name.Contains("Curved")); mat=source!=null?new Material(source):new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat,path); }
            mat.color=color; EditorUtility.SetDirty(mat); return mat;
        }
    }
}
