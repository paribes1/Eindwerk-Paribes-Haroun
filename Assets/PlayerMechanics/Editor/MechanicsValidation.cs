using System;
using System.IO;
using System.Linq;
using Eindwerk.Multiplayer;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Eindwerk.PlayerMechanics.Editor
{
    /// <summary>Controleert beweging, botsingen en netwerkopbouw zonder handmatige invoer.</summary>
    public static class MechanicsValidation
    {
        private static readonly System.Text.StringBuilder results = new System.Text.StringBuilder();
        private const float Step = 1f / 60f;

        /// <summary>Bouwt de oefenscenes opnieuw en voert de controles uit.</summary>
        public static void BuildAndValidate()
        {
            MechanicsSceneBuilder.Build();
            Validate();
        }

        [MenuItem("Tools/Player Mechanics/Controleer mechanics")]
        public static void ValidateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var originalScenes = EditorSceneManager.GetSceneManagerSetup();
            try { Validate(); }
            finally { EditorSceneManager.RestoreSceneManagerSetup(originalScenes); }
        }

        /// <summary>Controleert de spelerbeweging in de oefenscene en de netwerkopbouw in Map.</summary>
        public static void Validate()
        {
            results.Clear();
            EditorSceneManager.OpenScene(MechanicsSceneBuilder.OfflineScene);
            AssertNoMissingScripts();
            var controller = UnityEngine.Object.FindAnyObjectByType<CapsulePlayerController>();
            Check(controller != null && controller.HasLocalControl, "Offline speler heeft lokale besturing");
            var character = controller.GetComponent<CharacterController>();
            Check(controller.transform.Find("Capsule Visual").GetComponent<MeshFilter>() != null, "Capsulemodel aanwezig");
            controller.enabled = false;
            controller.SetLocalControl(true);
            Vector3 start = new Vector3(-14, 0.08f, -12);
            controller.Warp(start, Quaternion.identity);
            Advance(controller, Vector2.zero, 30);
            Check(controller.IsGrounded, "Zwaartekracht en grondcontact");
            float baseZ = controller.transform.position.z;
            Advance(controller, Vector2.up, 120);
            float walking = controller.transform.position.z - baseZ;
            Check(walking > 8f && walking < 9.2f, "Lopen met versnelling op 4.5 meter per seconde");
            controller.Warp(start, Quaternion.identity);
            Advance(controller, Vector2.zero, 20);
            baseZ = controller.transform.position.z;
            Advance(controller, Vector2.up, 120, true);
            float sprinting = controller.transform.position.z - baseZ;
            Check(sprinting > 13f && sprinting < 15.2f && sprinting > walking, "Sprint op 7.5 meter per seconde");
            controller.Warp(start, Quaternion.identity);
            Advance(controller, Vector2.zero, 20);
            Vector3 diagonalStart = controller.transform.position;
            Advance(controller, Vector2.one, 120);
            Check(Vector3.Distance(diagonalStart, controller.transform.position) < walking + 0.2f, "Diagonale invoer wordt begrensd");
            controller.Warp(start, Quaternion.identity);
            Advance(controller, Vector2.zero, 30);
            controller.SimulateMovement(Vector2.zero, false, false, true, Step);
            float apex = controller.transform.position.y;
            for (int i = 0; i < 90; i++)
            {
                controller.SimulateMovement(Vector2.zero, false, false, false, Step);
                apex = Mathf.Max(apex, controller.transform.position.y);
            }
            Check(apex > 1.05f && apex < 1.4f && controller.IsGrounded, "Springhoogte en landen");
            Advance(controller, Vector2.zero, 30, false, true);
            Check(controller.IsCrouching && Mathf.Abs(character.height - 1.15f) < 0.01f, "Hurken verkleint collider en capsule");
            controller.Warp(new Vector3(8, 0.01f, -6), Quaternion.identity);
            Physics.SyncTransforms();
            Advance(controller, Vector2.zero, 30);
            Check(controller.IsCrouching && character.height < 1.2f, "Blijft gehurkt onder lage doorgang");
            controller.Warp(start, Quaternion.identity);
            Advance(controller, Vector2.zero, 30);
            Check(!controller.IsCrouching && Mathf.Abs(character.height - 2f) < 0.01f, "Opstaan zodra er ruimte is");
            controller.SetSpawnPoint(start, Quaternion.identity);
            controller.Warp(new Vector3(0, -30, 0), Quaternion.identity);
            Advance(controller, Vector2.zero, 1);
            Check(Vector3.Distance(controller.transform.position, start) < 0.05f, "Terugzetten na val buiten omgeving");
            controller.SetLocalControl(false);
            Vector3 remoteStart = controller.transform.position;
            Advance(controller, Vector2.up, 60);
            Check(controller.transform.position == remoteStart && !character.enabled, "Geen lokale simulatie zonder eigenaarschap");
            EditorSceneManager.OpenScene(MechanicsSceneBuilder.MultiplayerScene);
            AssertNoMissingScripts();
            var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
            Check(manager != null && manager.NetworkConfig.PlayerPrefab != null, "Map heeft netwerkmanager en spelerprefab");
            Check(manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Any(list => list != null && list.Contains(manager.NetworkConfig.PlayerPrefab)),
                "Netwerkspeler staat in opgeslagen prefabregistratie");
            var networkPrefab = new SerializedObject(manager.NetworkConfig.PlayerPrefab.GetComponent<NetworkObject>());
            Check(networkPrefab.FindProperty("GlobalObjectIdHash").longValue != 0, "Netwerkprefab heeft een geldig prefab ID");
            Check(UnityEngine.Object.FindAnyObjectByType<MultiplayerSession>() != null, "Map heeft LAN-sessiemenu");
            Check(UnityEngine.Object.FindObjectsByType<NetworkPlayer>().Length == 0, "Map spawnt spelers pas bij een sessie");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/MechanicsValidation.txt", results.ToString());
            EditorSceneManager.OpenScene(MechanicsSceneBuilder.OfflineScene);
            Debug.Log("MECHANICS_VALIDATION_PASSED\n" + results);
        }

        private static void Advance(CapsulePlayerController controller, Vector2 movement, int steps, bool sprint = false, bool crouch = false)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < steps; i++) controller.SimulateMovement(movement, sprint, crouch, false, Step);
        }

        private static void AssertNoMissingScripts()
        {
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) == 0, "Referenties " + go.name, false);
            results.AppendLine("PASS Geen ontbrekende scriptreferenties");
        }

        private static void Check(bool condition, string label, bool log = true)
        {
            if (!condition) throw new InvalidOperationException("Mechanics validation failed: " + label);
            if (log) results.AppendLine("PASS " + label);
        }

        /// <summary>Maakt een Windows-testbuild met alleen Map.</summary>
        [MenuItem("Tools/Player Mechanics/Maak multiplayer testbuild")]
        public static void BuildSmokePlayer()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MechanicsSceneBuilder.MultiplayerScene },
                locationPathName = "Builds/MechanicsSmoke/MechanicsSmoke.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Smoke build failed: " + report.summary.result);
            Debug.Log("MECHANICS_SMOKE_BUILD_PASSED");
        }

        /// <summary>Bouwt de scenes, controleert ze en maakt daarna een testbuild.</summary>
        public static void BuildValidateAndSmokePlayer()
        {
            BuildAndValidate();
            BuildSmokePlayer();
        }
    }
}
