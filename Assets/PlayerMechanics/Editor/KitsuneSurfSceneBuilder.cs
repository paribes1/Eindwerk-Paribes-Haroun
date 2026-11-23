using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Eindwerk.PlayerMechanics.Editor
{
    /// <summary>
    /// Bouwt een surfparcours met negen etappes in Map, geïnspireerd op Kitsune.
    /// Behoudt het LAN-netwerk en de aparte oefenscene.
    /// Slaat de hellingen en hun botsingsvlakken op in het project.
    /// </summary>
    public static class KitsuneSurfSceneBuilder
    {
        private const string AssetFolder = "Assets/PlayerMechanics/Surf";
        private const string RootName = "Kitsune Surf Course";
        private const float FaceAngle = 55f;
        private static Material rampMaterial, darkMaterial, paleMaterial, orangeMaterial, blueMaterial;

        [MenuItem("Tools/Surf/Build Kitsune Course")]
        public static void BuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        /// <summary>Bouwt en bewaart het parcours en controleert de hellingen.</summary>
        public static void Build()
        {
            Directory.CreateDirectory(AssetFolder);
            AssetDatabase.Refresh();
            BuildMaterials();
            var scene = EditorSceneManager.OpenScene(MechanicsSceneBuilder.MultiplayerScene);
            foreach (var item in scene.GetRootGameObjects())
                if (item.name == "Mechanics Test Geometry" || item.name == RootName)
                    UnityEngine.Object.DestroyImmediate(item);

            var root = new GameObject(RootName);
            var starts = new Transform[9];
            var goals = new Vector3[9];
            var sizes = new Vector3[9];
            var killHeights = new float[9];
            for (int stage = 0; stage < starts.Length; stage++)
            {
                var stageRoot = new GameObject($"Stage {stage + 1:00}").transform;
                stageRoot.SetParent(root.transform, false);
                // De negen etappes staan in een raster van drie bij drie.
                // Checkpoints verplaatsen de speler naar de volgende etappe.
                stageRoot.position = new Vector3((stage % 3 - 1) * 180f, 0f, (stage / 3) * 300f);
                BuildStage(stage, stageRoot, out starts[stage], out goals[stage], out sizes[stage], out killHeights[stage]);
            }

            var course = root.AddComponent<KitsuneSurfCourse>();
            course.ConfigureCourse(starts, goals, sizes, killHeights);
            if (UnityEngine.Object.FindAnyObjectByType<MechanicsHud>() == null)
                new GameObject("Mechanics Controls").AddComponent<MechanicsHud>().Configure(true);
            ConfigureExistingSpawns(starts[0]);
            ConfigureLighting();
            var lobby = GameObject.Find("Lobby Camera");
            if (lobby != null)
            {
                lobby.transform.SetPositionAndRotation(starts[0].position + new Vector3(38f, 22f, -29f),
                    Quaternion.Euler(24f, -27f, 0f));
                var camera = lobby.GetComponent<Camera>();
                if (camera != null) { camera.farClipPlane = 1400f; camera.fieldOfView = 78f; }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(starts[0].position + new Vector3(0, -35f, 85f),
                    Quaternion.Euler(25f, -28f, 0f), 165f);
            Selection.activeGameObject = root;
            Validate();
            Debug.Log("KITSUNE_SURF_COURSE_BUILT: Map now contains nine staged surf lanes and preserved LAN capabilities.");
        }

        private static void BuildStage(int stage, Transform parent, out Transform spawn,
            out Vector3 goal, out Vector3 goalSize, out float killHeight)
        {
            const float startHeight = 120f;
            int rampCount = stage < 3 ? 2 : 3;
            float length = 54f + (stage % 3) * 4f;
            float spacing = length + 10f;
            float firstZ = 20f;
            Platform("Start deck", parent, new Vector3(0f, startHeight - 1f, 0f), new Vector3(28f, 2f, 22f), true);
            spawn = new GameObject("Checkpoint spawn").transform;
            spawn.SetParent(parent, false);
            spawn.localPosition = new Vector3(0f, startHeight + 0.12f, -4f);
            spawn.localRotation = Quaternion.identity;
            // De voorkant blijft open om op de helling te springen.
            // Houd A of D naar de nok en draai rustig mee met de helling.
            Beam("Back rail", parent, new Vector3(-14f, startHeight + 2f, -11f),
                new Vector3(14f, startHeight + 2f, -11f), 0.16f, orangeMaterial);
            for (int side = -1; side <= 1; side += 2)
                Beam("Start side rail", parent, new Vector3(side * 14f, startHeight + 2f, -11f),
                    new Vector3(side * 14f, startHeight + 2f, 6f), 0.16f, darkMaterial);
            StageNumber(stage + 1, parent, new Vector3(-2.2f, startHeight + 3.5f, -10.8f));
            Arrow(parent, new Vector3(0f, startHeight + 0.025f, 5f), 3f, orangeMaterial);

            float lastZ = firstZ;
            float lastRidge = 0f;
            for (int ramp = 0; ramp < rampCount; ramp++)
            {
                float sign = (ramp % 2 == 0 ? 1f : -1f) * (stage % 2 == 0 ? 1f : -1f);
                float offset = sign * (stage < 3 ? 7f : 8f + (stage % 3));
                float zStart = firstZ + ramp * spacing;
                float ridge = 103f - ramp * 32f;
                float turn = stage < 2 ? 0f : sign * (stage % 3 + 1) * 2f;
                var rampRoot = new GameObject($"Surf ramp {ramp + 1} - {FaceAngle:0} degrees").transform;
                rampRoot.SetParent(parent, false);
                rampRoot.localPosition = new Vector3(offset, ridge, zStart + length * 0.5f);
                rampRoot.localRotation = Quaternion.Euler(0f, turn, 0f);
                float halfWidth = stage == 8 ? 12f : 10f;
                Ramp(rampRoot, stage, ramp, length, halfWidth, 8f);
                lastZ = zStart + length;
                lastRidge = ridge - 8f;
            }

            // Het brede eindplatform ligt onder de laatste helling.
            // De volgende etappe begint zodra de speler het doel bereikt.
            float goalY = lastRidge - 25f;
            Vector3 deck = new Vector3(0f, goalY - 1f, lastZ + 24f);
            Platform("Checkpoint catch deck", parent, deck, new Vector3(64f, 2f, 44f), false);
            Portal(parent, new Vector3(0f, goalY, lastZ + 32f), stage == 8 ? blueMaterial : orangeMaterial);
            Arrow(parent, new Vector3(0f, goalY + 0.025f, lastZ + 16f), 5f, orangeMaterial);
            goal = parent.TransformPoint(new Vector3(0f, goalY + 2.5f, lastZ + 24f));
            goalSize = new Vector3(63f, 6f, 43f);
            killHeight = goalY - 24f;

            // De sierbalken staan buiten de route en omlijnen elke etappe.
            float roomDepth = lastZ + 48f;
            for (int side = -1; side <= 1; side += 2)
            {
                Beam("Room outline", parent, new Vector3(side * 57f, 0f, -20f),
                    new Vector3(side * 57f, 0f, roomDepth), 0.28f, blueMaterial);
                for (int rib = 0; rib < 3; rib++)
                {
                    float z = 10f + rib * (roomDepth - 20f) / 2f;
                    Beam("Room upright", parent, new Vector3(side * 57f, -15f, z),
                        new Vector3(side * 57f, 138f, z), 0.8f, darkMaterial);
                    Beam("Upright light", parent, new Vector3(side * 56.5f, 10f, z),
                        new Vector3(side * 56.5f, 106f, z), 0.15f, orangeMaterial);
                }
            }
        }

        private static void Ramp(Transform root, int stage, int index, float length, float width, float drop)
        {
            float height = width * Mathf.Tan(FaceAngle * Mathf.Deg2Rad);
            Vector3 a0 = new Vector3(0, 0, -length * 0.5f);
            Vector3 l0 = new Vector3(-width, -height, a0.z);
            Vector3 r0 = new Vector3(width, -height, a0.z);
            Vector3 a1 = new Vector3(0, -drop, length * 0.5f);
            Vector3 l1 = new Vector3(-width, -height - drop, a1.z);
            Vector3 r1 = new Vector3(width, -height - drop, a1.z);
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var faces = new List<int>();
            var caps = new List<int>();
            float faceWidth = Mathf.Sqrt(width * width + height * height);
            Quad(vertices, uv, faces, l0, l1, a1, a0, length / 4f, faceWidth / 4f);
            Quad(vertices, uv, faces, a0, a1, r1, r0, length / 4f, faceWidth / 4f);
            Quad(vertices, uv, caps, r0, r1, l1, l0, 1f, 1f);
            Triangle(vertices, uv, caps, a0, r0, l0);
            Triangle(vertices, uv, caps, a1, l1, r1);
            var generated = new Mesh { name = $"Stage{stage + 1:00}_Ramp{index + 1:00}" };
            generated.SetVertices(vertices);
            generated.SetUVs(0, uv);
            generated.subMeshCount = 2;
            generated.SetTriangles(faces, 0);
            generated.SetTriangles(caps, 1);
            generated.RecalculateNormals();
            generated.RecalculateTangents();
            generated.RecalculateBounds();
            string path = $"{AssetFolder}/{generated.name}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = generated; AssetDatabase.CreateAsset(mesh, path); }
            else { EditorUtility.CopySerialized(generated, mesh); UnityEngine.Object.DestroyImmediate(generated); EditorUtility.SetDirty(mesh); }
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { rampMaterial, darkMaterial };
            root.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            Beam("Orange ridge", root, a0 + Vector3.up * 0.045f, a1 + Vector3.up * 0.045f, 0.14f, orangeMaterial);
            Beam("Left edge", root, l0 + Vector3.up * 0.045f, l1 + Vector3.up * 0.045f, 0.18f, orangeMaterial);
            Beam("Right edge", root, r0 + Vector3.up * 0.045f, r1 + Vector3.up * 0.045f, 0.18f, orangeMaterial);
            Beam("Start profile left", root, l0 + Vector3.up * 0.035f, a0 + Vector3.up * 0.035f, 0.12f, orangeMaterial);
            Beam("Start profile right", root, a0 + Vector3.up * 0.035f, r0 + Vector3.up * 0.035f, 0.12f, orangeMaterial);
            Beam("Exit profile left", root, l1 + Vector3.up * 0.035f, a1 + Vector3.up * 0.035f, 0.12f, orangeMaterial);
            Beam("Exit profile right", root, a1 + Vector3.up * 0.035f, r1 + Vector3.up * 0.035f, 0.12f, orangeMaterial);
        }

        private static void Quad(List<Vector3> vertices, List<Vector2> uv, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, float longitudinal, float transverse)
        {
            int start = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            uv.AddRange(new[] { new Vector2(0, 0), new Vector2(longitudinal, 0),
                new Vector2(longitudinal, transverse), new Vector2(0, transverse) });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private static void Triangle(List<Vector3> vertices, List<Vector2> uv, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count;
            vertices.AddRange(new[] { a, b, c });
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.up });
            triangles.AddRange(new[] { start, start + 1, start + 2 });
        }

        private static void Platform(string name, Transform parent, Vector3 center, Vector3 size, bool start)
        {
            Cube(name, parent, center, size, paleMaterial, true);
            float y = center.y + size.y * 0.5f + 0.025f;
            float x = size.x * 0.5f;
            float z = size.z * 0.5f;
            Beam("Deck left trim", parent, center + new Vector3(-x, size.y * 0.5f, -z),
                center + new Vector3(-x, size.y * 0.5f, z), 0.2f, orangeMaterial);
            Beam("Deck right trim", parent, center + new Vector3(x, size.y * 0.5f, -z),
                center + new Vector3(x, size.y * 0.5f, z), 0.2f, orangeMaterial);
            Beam("Deck front trim", parent, new Vector3(center.x - x, y, center.z + z),
                new Vector3(center.x + x, y, center.z + z), 0.2f, start ? orangeMaterial : blueMaterial);
            for (int line = 1; line < 5; line++)
            {
                float lineX = center.x - x + size.x * line / 5f;
                Cube("Deck tile seam", parent, new Vector3(lineX, y, center.z),
                    new Vector3(0.028f, 0.012f, size.z), darkMaterial, false);
            }
        }

        private static void Portal(Transform parent, Vector3 center, Material trim)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Cube("Checkpoint pillar", parent, center + new Vector3(side * 11.5f, 4f, 0),
                    new Vector3(1.1f, 8f, 1.1f), darkMaterial, false);
                Cube("Checkpoint pillar light", parent, center + new Vector3(side * 11.5f, 4f, -0.57f),
                    new Vector3(0.38f, 7.2f, 0.05f), trim, false);
            }
            Cube("Checkpoint lintel", parent, center + Vector3.up * 8.1f,
                new Vector3(28f, 1.1f, 1.1f), darkMaterial, false);
            Cube("Checkpoint header light", parent, center + new Vector3(0, 7.6f, -0.6f),
                new Vector3(26f, 0.25f, 0.08f), trim, false);
        }

        private static void Arrow(Transform parent, Vector3 center, float size, Material material)
        {
            Beam("Launch arrow left", parent, center + new Vector3(-size, 0, -size), center,
                0.20f, material);
            Beam("Launch arrow right", parent, center + new Vector3(size, 0, -size), center,
                0.20f, material);
        }

        private static void StageNumber(int value, Transform parent, Vector3 center)
        {
            Digit(value / 10, parent, center + Vector3.left * 1.6f);
            Digit(value % 10, parent, center + Vector3.right * 1.6f);
        }

        private static void Digit(int value, Transform parent, Vector3 center)
        {
            // De cijfers bestaan uit balkjes en hebben geen lettertype nodig.
            int[] masks = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
            Vector2[] a = { new Vector2(-1, 2), new Vector2(1, 2), new Vector2(1, 0), new Vector2(1, -2),
                new Vector2(-1, -2), new Vector2(-1, 0), new Vector2(-1, 0) };
            Vector2[] b = { new Vector2(1, 2), new Vector2(1, 0), new Vector2(1, -2), new Vector2(-1, -2),
                new Vector2(-1, 0), new Vector2(-1, 2), new Vector2(1, 0) };
            for (int segment = 0; segment < 7; segment++)
                if ((masks[value] & (1 << segment)) != 0)
                    Beam("Stage number", parent, center + new Vector3(a[segment].x, a[segment].y, 0),
                        center + new Vector3(b[segment].x, b[segment].y, 0), 0.28f, orangeMaterial);
        }

        private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 size,
            Material material, bool collision)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static void Beam(string name, Transform parent, Vector3 start, Vector3 end,
            float thickness, Material material)
        {
            Vector3 difference = end - start;
            var beam = Cube(name, parent, (start + end) * 0.5f,
                new Vector3(thickness, thickness, difference.magnitude), material, false);
            beam.transform.localRotation = Quaternion.LookRotation(difference.normalized,
                Mathf.Abs(Vector3.Dot(difference.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
        }

        private static void BuildMaterials()
        {
            Shader lit = Shader.Find("HDRP/Lit");
            if (lit == null) throw new InvalidOperationException("Kitsune course requires the project's HDRP/Lit shader.");
            darkMaterial = MaterialAsset("Charcoal", new Color(0.045f, 0.055f, 0.068f));
            paleMaterial = MaterialAsset("Ivory Decks", new Color(0.68f, 0.72f, 0.73f));
            orangeMaterial = MaterialAsset("Kitsune Orange", new Color(1f, 0.26f, 0.025f), true);
            blueMaterial = MaterialAsset("Checkpoint Blue", new Color(0.055f, 0.56f, 0.9f), true);
            rampMaterial = MaterialAsset("Tiled Ramp", Color.white);
            const int textureSize = 128;
            string texturePath = AssetFolder + "/RampTiles.asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, true) { name = "Dark checker tiles" };
                AssetDatabase.CreateAsset(texture, texturePath);
            }
            var pixels = new Color[textureSize * textureSize];
            for (int y = 0; y < textureSize; y++)
                for (int x = 0; x < textureSize; x++)
                {
                    bool seam = x % 64 < 2 || y % 64 < 2;
                    bool alternate = ((x / 64 + y / 64) & 1) != 0;
                    pixels[y * textureSize + x] = seam ? new Color(0.16f, 0.18f, 0.20f)
                        : alternate ? new Color(0.075f, 0.085f, 0.10f) : new Color(0.11f, 0.125f, 0.14f);
                }
            texture.SetPixels(pixels);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            texture.Apply(true, false);
            EditorUtility.SetDirty(texture);
            rampMaterial.SetTexture("_BaseColorMap", texture);
            EditorUtility.SetDirty(rampMaterial);
        }

        private static Material MaterialAsset(string name, Color color, bool emissive = false)
        {
            string path = AssetFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.25f);
            if (emissive)
            {
                material.SetColor("_EmissiveColor", color * 4500f);
                material.SetColor("_EmissiveColorLDR", color);
                material.SetFloat("_EmissiveIntensity", 4500f);
                material.SetFloat("_EmissiveExposureWeight", 1f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureExistingSpawns(Transform firstSpawn)
        {
            var spawnsRoot = GameObject.Find("Player Spawn Points");
            if (spawnsRoot == null) return;
            for (int index = 0; index < spawnsRoot.transform.childCount; index++)
            {
                Transform spawn = spawnsRoot.transform.GetChild(index);
                spawn.SetPositionAndRotation(firstSpawn.position + new Vector3((index % 4 - 1.5f) * 3f,
                    0f, -(index / 4) * 3f), firstSpawn.rotation);
            }
        }

        private static void ConfigureLighting()
        {
            var sun = GameObject.Find("Sun");
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
                var light = sun.GetComponent<Light>();
                if (light != null) { light.intensity = 45000f; light.shadows = LightShadows.Soft; }
            }
            var skyObject = GameObject.Find("Sky and Exposure");
            if (skyObject == null) skyObject = new GameObject("Sky and Exposure");
            var volume = skyObject.GetComponent<Volume>();
            if (volume == null) volume = skyObject.AddComponent<Volume>();
            volume.isGlobal = true;
            string profilePath = AssetFolder + "/KitsuneSky.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            var environment = GetOrAdd<VisualEnvironment>(profile);
            environment.skyType.Override(3);
            var gradient = GetOrAdd<GradientSky>(profile);
            gradient.top.Override(new Color(0.06f, 0.11f, 0.21f));
            gradient.middle.Override(new Color(0.27f, 0.39f, 0.50f));
            gradient.bottom.Override(new Color(0.018f, 0.035f, 0.065f));
            gradient.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
            gradient.multiplier.Override(18000f);
            var exposure = GetOrAdd<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(13f);
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(profile);
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var component)) return component;
            component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>Controleert de negen starts en de botsingsvlakken van alle hellingen.</summary>
        [MenuItem("Tools/Surf/Validate Course Geometry")]
        public static void Validate()
        {
            Physics.SyncTransforms();
            var root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Open Map and build the Kitsune course before validating.");
            if (root.transform.childCount != 9) throw new InvalidOperationException("Expected nine stages.");
            int rampCount = 0;
            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>())
            {
                rampCount++;
                if (collider.sharedMesh == null || collider.convex)
                    throw new InvalidOperationException("A surf ramp has no valid static collision mesh.");
                Vector3[] normals = collider.sharedMesh.normals;
                float expected = Mathf.Cos(FaceAngle * Mathf.Deg2Rad);
                if (normals.Length < 8 || Mathf.Abs(normals[0].y - expected) > 0.02f || normals[0].y <= 0f)
                    throw new InvalidOperationException("A ramp face is inverted or outside its intended surf angle.");
            }
            if (rampCount != 24) throw new InvalidOperationException($"Expected 24 surf ramps, got {rampCount}.");
            foreach (Transform stage in root.transform)
            {
                var spawn = stage.Find("Checkpoint spawn");
                if (spawn == null || !Physics.Raycast(spawn.position + Vector3.up, Vector3.down, 3f))
                    throw new InvalidOperationException("A checkpoint spawn has no supporting deck.");
            }
            Debug.Log("KITSUNE_SURF_GEOMETRY_VALID: nine starts, 24 collidable 55-degree ramps, 18 catch/start decks.");
        }
    }
}
