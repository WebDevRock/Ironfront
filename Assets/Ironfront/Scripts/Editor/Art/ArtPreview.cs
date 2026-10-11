using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Renders procedural models into PNGs in an unsaved temporary scene, for fast visual iteration and for the
    /// art-direction document. Menu: Ironfront > Art > Render Model Previews. Output: Temp/ArtPreview (git-ignored)
    /// unless another folder is given.
    /// </summary>
    public static class ArtPreview
    {
        [MenuItem("Ironfront/Art/Render Model Previews")]
        public static void RenderFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(RenderAll("Temp/ArtPreview"));
        }

        /// <summary>Renders every model family; returns a summary. Leaves an empty untitled scene open.</summary>
        public static string RenderAll(string outDir)
        {
            Directory.CreateDirectory(outDir);
            var a = ArtLibrary.Create();
            string s = "";
            s += Render(outDir, "vehicles", new Vector3(0f, 0f, 0f), 7.5f, 38f, -150f, root =>
            {
                Place(root, "Light", new Vector3(-2.4f, 0f, 0f), t => VehicleModels.LightTank(t, a), a, 1);
                Place(root, "Heavy", new Vector3(0.6f, 0f, 0f), t => VehicleModels.HeavyTank(t, a), a, 1);
                Place(root, "Harvester", new Vector3(3.6f, 0f, 0f), t => VehicleModels.Harvester(t, a), a, 1);
                Place(root, "LightRed", new Vector3(-2.4f, 0f, -3.6f), t => VehicleModels.LightTank(t, a), a, 2);
                Place(root, "HeavyRed", new Vector3(0.6f, 0f, -3.6f), t => VehicleModels.HeavyTank(t, a), a, 2);
                Place(root, "HarvesterRed", new Vector3(3.6f, 0f, -3.6f), t => VehicleModels.Harvester(t, a), a, 2);
            });
            s += Render(outDir, "vehicles_rts", new Vector3(0.6f, 0f, -1.8f), 30f, 55f, 0f, null);
            s += Render(outDir, "structures", new Vector3(0f, 0f, 0f), 30f, 40f, -160f, root =>
            {
                PlaceStructure(root, "HQ", new Vector3(-9f, 0f, 4f), t => StructureModels.Headquarters(t, a), a, 1, new Vector2(8f, 8f), 3f);
                PlaceStructure(root, "Refinery", new Vector3(1f, 0f, 4f), t => StructureModels.Refinery(t, a), a, 1, new Vector2(7f, 6f), 4f);
                PlaceStructure(root, "Power", new Vector3(9f, 0f, 4f), t => StructureModels.PowerPlant(t, a), a, 2, new Vector2(5f, 5f), 0f);
                PlaceStructure(root, "Factory", new Vector3(-5f, 0f, -8f), t => StructureModels.VehicleFactory(t, a), a, 2, new Vector2(8f, 7f), 5f);
                PlaceStructure(root, "Outpost", new Vector3(6f, 0f, -8f), t => StructureModels.Outpost(t, a), a, 2, new Vector2(6f, 6f), 0f);
            });
            s += Render(outDir, "structures_rts", new Vector3(0f, 0f, -2f), 46f, 55f, 0f, null);
            return s;
        }

        private static GameObject s_lastRoot;

        public static void Place(Transform parent, string name, Vector3 pos, Action<Transform> build, ArtLibrary a, int faction)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 160f, 0f);
            build(go.transform);
            var color = faction == 1 ? new Color(0.2f, 0.45f, 0.95f) : new Color(0.85f, 0.15f, 0.12f);
            var block = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial == a.Faction)
                {
                    block.SetColor("_BaseColor", color);
                    r.SetPropertyBlock(block);
                }
            }
        }

        public static void PlaceStructure(Transform parent, string name, Vector3 pos, Action<Transform> build, ArtLibrary a, int faction,
            Vector2 footprint, float apron)
        {
            Place(parent, name, pos, t =>
            {
                build(t);
                if (apron > 0f) StructureModels.Apron(t, a, footprint, apron);
            }, a, faction);
            var go = parent.Find(name);
            go.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var block = new MaterialPropertyBlock();
            var color = faction == 1 ? new Color(0.2f, 0.45f, 0.95f) : new Color(0.85f, 0.15f, 0.12f);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial == a.FactionGlow)
                {
                    block.SetColor("_EmissionColor", color * 2.5f);
                    r.SetPropertyBlock(block);
                }
        }

        /// <summary>Renders a 1600x900 view orbiting <paramref name="focus"/>. Pass a populate callback to rebuild the scene.</summary>
        public static string Render(string outDir, string name, Vector3 focus, float distance, float pitch, float yaw, Action<Transform> populate)
        {
            if (populate != null)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RtsSandboxBuilder.CreateLighting();
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.localScale = new Vector3(4f, 1f, 4f);
                ground.GetComponent<Renderer>().sharedMaterial = ArtLibrary.Current.Concrete_;
                s_lastRoot = new GameObject("Preview");
                populate(s_lastRoot.transform);
            }

            var camGo = new GameObject("PreviewCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 35f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f);
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            camGo.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);

            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string path = Path.Combine(outDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            return path + "\n";
        }
    }
}
