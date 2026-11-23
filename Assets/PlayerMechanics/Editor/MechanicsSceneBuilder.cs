using System;
using System.IO;
using System.Linq;
using Eindwerk.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Eindwerk.PlayerMechanics.Editor
{
    /// <summary>Bouwt de oefenscenes en spelerprefabs alleen op aanvraag.</summary>
    public static class MechanicsSceneBuilder
    {
        public const string OfflineScene = "Assets/Scenes/Player Mechanica.unity";
        public const string MultiplayerScene = "Assets/Scenes/Map.unity";
        public const string PlayerPrefab = "Assets/PlayerMechanics/Prefabs/CapsulePlayer.prefab";
        public const string NetworkPrefab = "Assets/Multiplayer/Prefabs/NetworkCapsulePlayer.prefab";
        private static Material floor, grid, obstacle, accent, capsule;

        [MenuItem("Tools/Player Mechanics/Bouw testscenes opnieuw")]
        public static void RebuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        /// <summary>Bouwt en bewaart beide oefenscenes en de spelerprefabs opnieuw.</summary>
        public static void Build()
        {
            Directory.CreateDirectory("Assets/PlayerMechanics/Prefabs");
            Directory.CreateDirectory("Assets/Multiplayer/Prefabs");
            Directory.CreateDirectory("Assets/PlayerMechanics/Materials");
            AssetDatabase.Refresh();
            floor = MaterialAsset("Floor", new Color(0.15f, 0.19f, 0.24f));
            grid = MaterialAsset("Grid", new Color(0.24f, 0.30f, 0.36f));
            obstacle = MaterialAsset("Obstacles", new Color(0.30f, 0.49f, 0.55f));
            accent = MaterialAsset("Accent", new Color(0.92f, 0.56f, 0.23f));
            capsule = MaterialAsset("Capsule", new Color(0.20f, 0.82f, 0.72f));
            GameObject player = BuildPlayerPrefab();
            GameObject networkPlayer = BuildNetworkPrefab(player);
            BuildOffline(player);
            BuildMap(networkPlayer);
            // Zet de twee oefenscenes vooraan en behoud de overige buildscenes.
            var preserved = EditorBuildSettings.scenes.Where(s => s.path != OfflineScene && s.path != MultiplayerScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(OfflineScene, true),
                new EditorBuildSettingsScene(MultiplayerScene, true) }.Concat(preserved).ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(OfflineScene);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/MechanicsSceneBuild.txt", "Scenes and prefabs generated successfully.\n" +
                OfflineScene + "\n" + MultiplayerScene + "\n" + PlayerPrefab + "\n" + NetworkPrefab);
            Debug.Log("MECHANICS_SCENES_BUILT");
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = "Assets/PlayerMechanics/Materials/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!result)
            {
                result = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(result, path);
            }
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(result);
            return result;
        }

        private static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("Capsule Player");
            var motor = root.AddComponent<CharacterController>();
            motor.height = 2f; motor.radius = 0.4f; motor.center = Vector3.up;
            motor.stepOffset = 0.3f; motor.slopeLimit = 45f; motor.skinWidth = 0.035f;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Capsule Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.up;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<Renderer>().sharedMaterial = capsule;
            var pivot = new GameObject("Camera Pivot").transform;
            pivot.SetParent(root.transform, false); pivot.localPosition = Vector3.up * 1.6f;
            var cameraObject = new GameObject("Player Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pivot, false);
            cameraObject.transform.localPosition = new Vector3(0, 0, -4);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 300f; camera.fieldOfView = 70f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<HDAdditionalCameraData>();
            var controller = root.AddComponent<CapsulePlayerController>();
            controller.Configure(visual.transform, pivot, camera);
            controller.SetLocalControl(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject BuildNetworkPrefab(GameObject playerPrefab)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            root.name = "Network Capsule Player";
            root.AddComponent<NetworkObject>();
            var sync = root.AddComponent<OwnerNetworkTransform>();
            sync.Interpolate = true;
            sync.SyncScaleX = false; sync.SyncScaleY = false; sync.SyncScaleZ = false;
            sync.SyncRotAngleX = false; sync.SyncRotAngleZ = false;
            root.AddComponent<NetworkPlayer>().Configure(root.transform.Find("Capsule Visual"));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, NetworkPrefab);
            UnityEngine.Object.DestroyImmediate(root);
            // NGO berekent het prefab-ID in OnValidate. Sla de prefab daarom eerst op.
            var savedNetworkObject = prefab.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?.Invoke(savedNetworkObject, null);
            var serialized = new SerializedObject(savedNetworkObject);
            if (serialized.FindProperty("GlobalObjectIdHash").longValue == 0)
                throw new InvalidOperationException("Netwerkprefab heeft geen geldig prefab ID.");
            EditorUtility.SetDirty(savedNetworkObject);
            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
        }

        private static void BuildOffline(GameObject prefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateEnvironment(false);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = new Vector3(0, 0.08f, -9);
            var controller = player.GetComponent<CapsulePlayerController>();
            controller.SetSpawnPoint(player.transform.position, Quaternion.identity);
            controller.SetLocalControl(true);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            EditorSceneManager.SaveScene(scene, OfflineScene);
        }

        private static void BuildMap(GameObject prefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateEnvironment(true);
            var cameraObject = new GameObject("Lobby Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(18, 19, -23), Quaternion.Euler(33, -34, 0));
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<HDAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();
            var networkRoot = new GameObject("Multiplayer Capabilities");
            var transport = networkRoot.AddComponent<UnityTransport>();
            var manager = networkRoot.AddComponent<NetworkManager>();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = prefab;
            manager.NetworkConfig.TickRate = 30;
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.EnableSceneManagement = false;
            const string listPath = "Assets/Multiplayer/Prefabs/MechanicsNetworkPrefabs.asset";
            var prefabList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(listPath);
            if (!prefabList)
            {
                prefabList = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(prefabList, listPath);
            }
            foreach (var entry in prefabList.PrefabList.ToArray()) prefabList.Remove(entry);
            prefabList.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(prefabList);
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
            var spawnsRoot = new GameObject("Player Spawn Points").transform;
            var spawns = new Transform[8];
            for (int i = 0; i < spawns.Length; i++)
            {
                spawns[i] = new GameObject("Spawn " + (i + 1)).transform;
                spawns[i].SetParent(spawnsRoot, false);
                spawns[i].position = new Vector3((i % 4 - 1.5f) * 2.5f, 0.1f, -8 - (i / 4) * 3);
            }
            networkRoot.AddComponent<MultiplayerSession>().Configure(manager, transport, prefab, camera, spawns);
            EditorSceneManager.SaveScene(scene, MultiplayerScene);
        }

        private static void CreateEnvironment(bool multiplayer)
        {
            var sun = new GameObject("Sun");
            sun.transform.rotation = Quaternion.Euler(48, -32, 0);
            var light = sun.AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 100000; light.shadows = LightShadows.Soft;
            sun.AddComponent<HDAdditionalLightData>();
            var volume = new GameObject("Sky and Exposure").AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var sky = profile.Add<VisualEnvironment>(true); sky.skyType.Override(1);
            // Een kleurverloop geeft de lucht kleur zonder extra afbeeldingen.
            sky.skyType.Override(3);
            var gradient = profile.Add<GradientSky>(true);
            gradient.top.Override(new Color(0.15f, 0.28f, 0.48f));
            gradient.middle.Override(new Color(0.55f, 0.65f, 0.75f));
            gradient.bottom.Override(new Color(0.30f, 0.33f, 0.38f));
            var exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed); exposure.fixedExposure.Override(11.5f);
            string profilePath = "Assets/PlayerMechanics/Materials/SandboxVolume.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (!existing) { AssetDatabase.CreateAsset(profile, profilePath); foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile); }
            else { UnityEngine.Object.DestroyImmediate(profile); profile = existing; }
            volume.sharedProfile = profile;
            var geometry = new GameObject("Mechanics Test Geometry").transform;
            Cube("Ground 40 by 40", new Vector3(0, -0.25f, 0), new Vector3(40, 0.5f, 40), floor, geometry);
            for (int i = -18; i <= 18; i += 2)
            {
                Cube("Grid X " + i, new Vector3(i, 0.003f, 0), new Vector3(0.025f, 0.005f, 40), grid, geometry, false);
                Cube("Grid Z " + i, new Vector3(0, 0.003f, i), new Vector3(40, 0.005f, 0.025f), grid, geometry, false);
            }
            for (int i = 0; i < 5; i++)
                Cube("Step " + (i + 1), new Vector3(-7, (i + 1) * 0.125f, -2 + i * 0.8f), new Vector3(3, (i + 1) * 0.25f, 0.8f), obstacle, geometry);
            var ramp = Cube("Slope 25 degrees", new Vector3(7, 0.93f, 2), new Vector3(3, 0.4f, 5), obstacle, geometry);
            ramp.transform.rotation = Quaternion.Euler(-25, 0, 0);
            Cube("Jump block 50 cm", new Vector3(-2, 0.25f, 5), new Vector3(2, 0.5f, 2), accent, geometry);
            Cube("Jump block 100 cm", new Vector3(1, 0.5f, 7), new Vector3(2, 1, 2), accent, geometry);
            Cube("Low passage roof 135 cm clearance", new Vector3(8, 1.6f, -6), new Vector3(4, 0.5f, 4), accent, geometry);
            Cube("Low passage left support", new Vector3(5.75f, 0.95f, -6), new Vector3(0.5f, 1.9f, 4), obstacle, geometry);
            Cube("Low passage right support", new Vector3(10.25f, 0.95f, -6), new Vector3(0.5f, 1.9f, 4), obstacle, geometry);
            new GameObject("Mechanics Controls").AddComponent<MechanicsHud>().Configure(multiplayer);
        }

        private static GameObject Cube(string name, Vector3 position, Vector3 scale, Material material, Transform parent, bool collision = true)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.SetParent(parent, false);
            cube.transform.position = position; cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }
    }
}
