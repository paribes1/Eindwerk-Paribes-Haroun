using System;
using System.IO;
using Eindwerk.Multiplayer;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eindwerk.PlayerMechanics.Editor
{
    /// <summary>Controleert de LAN-sessie en surfcheckpoints in de speelmodus.</summary>
    [InitializeOnLoad]
    public static class SurfPlayValidation
    {
        private const string Key = "Eindwerk.SurfPlayValidation.";
        private const string Report = "Logs/SurfPlayValidation.txt";
        private const string Passed = "SURF_PLAY_VALIDATION_PASSED";
        private const string Failed = "SURF_PLAY_VALIDATION_FAILED";

        static SurfPlayValidation()
        {
            EditorApplication.update += Poll;
            Application.logMessageReceived += OnLog;
        }

        /// <summary>Start de speeltest. Sla gewijzigde scenes eerst op.</summary>
        [MenuItem("Tools/Surf/Validate Play Mode Integration")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || SessionState.GetBool(Key + "active", false))
                throw new InvalidOperationException("Start surf validation from stable Edit mode.");
            for (int index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty)
                    throw new InvalidOperationException("Save modified scenes before surf validation.");
            EditorSceneManager.OpenScene(MechanicsSceneBuilder.MultiplayerScene);
            SessionState.SetString(Key + "results", DateTime.UtcNow.ToString("O") + "\n");
            SessionState.SetString(Key + "error", string.Empty);
            SessionState.SetString(Key + "deadline", DateTime.UtcNow.AddSeconds(30).Ticks.ToString());
            SessionState.SetInt(Key + "phase", 0);
            SessionState.SetInt(Key + "goal", 0);
            SessionState.SetFloat(Key + "next", 0f);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, "SURF_PLAY_VALIDATION_RUNNING\n");
            SessionState.SetBool(Key + "active", true);
            Debug.Log("SURF_PLAY_VALIDATION_STARTED");
            EditorApplication.isPlaying = true;
        }

        private static void Poll()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                string loggedError = SessionState.GetString(Key + "error", string.Empty);
                Check(string.IsNullOrEmpty(loggedError), "Runtime error: " + loggedError);
                Check(long.TryParse(SessionState.GetString(Key + "deadline", "0"), out long deadline)
                    && DateTime.UtcNow.Ticks <= deadline, "Surf validation exceeded its 30 second deadline.");
                int phase = SessionState.GetInt(Key + "phase", 0);
                if (phase == 0)
                {
                    if (EditorApplication.isPlaying) WaitForPhase(1, 1f);
                    return;
                }
                if (!EditorApplication.isPlaying)
                {
                    Check(EditorApplication.isPlayingOrWillChangePlaymode, "Play mode ended before validation completed.");
                    return;
                }
                if (EditorApplication.isPaused || EditorApplication.isCompiling
                    || EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "next", 0f)) return;

                var course = UnityEngine.Object.FindAnyObjectByType<KitsuneSurfCourse>();
                var session = UnityEngine.Object.FindAnyObjectByType<MultiplayerSession>();
                var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
                Check(course != null && course.enabled && course.StageCount == 9, "Expected the nine-stage Map course.");
                Check(session != null && manager != null, "Expected the original LAN session and manager.");
                CapsulePlayerController player = FindOwnedMotor();

                switch (phase)
                {
                    case 1:
                        Check(!session.IsSessionActive && Motors().Length == 0,
                            "Map must start in the original lobby without any local or solo capsule.");
                        CheckLobby();
                        Pass("Map starts in its original lobby without a player or solo practice");
                        session.SetEndpoint("127.0.0.1", 18777);
                        Check(session.StartHost(), "LAN host could not start on validation port 18777.");
                        WaitForPhase(2, 1f);
                        break;
                    case 2:
                        if (!manager.IsHost || player == null || !player.HasLocalControl) return;
                        Check(player.SurfEnabled && player.IsFirstPerson && player.GetComponent<CharacterController>().enabled,
                            "LAN owner needs an active first-person surf motor.");
                        Check(Motors().Length == 1, "Hosting must spawn exactly one network capsule.");
                        CheckOneCameraAndListener();
                        player.ReleaseCursor();
                        Pass("Host spawns one owned first-person surf capsule with one camera and listener");
                        WarpToGoal(course, player, 0);
                        WaitForPhase(3, 0.25f);
                        break;
                    case 3:
                        Check(player != null && course.ActiveStage == 1 && !course.Finished,
                            "The first goal must advance to the second stage.");
                        CheckNearStart(course, player, 1);
                        Pass("First goal teleports the LAN player to checkpoint two");
                        player.Warp(StartPosition(course, 1) + new Vector3(3f, 1f, 0f), Quaternion.identity);
                        course.RetryStage();
                        Check(course.ActiveStage == 1 && !course.Finished, "Retry must retain the active checkpoint.");
                        CheckNearStart(course, player, 1);
                        Check(player.Velocity.sqrMagnitude < 0.0001f, "Retry must clear movement momentum.");
                        Pass("Checkpoint retry preserves the stage and clears velocity");
                        Vector3 start = StartPosition(course, 1);
                        float kill = new SerializedObject(course).FindProperty("killHeights")
                            .GetArrayElementAtIndex(1).floatValue;
                        player.Warp(new Vector3(start.x, kill - 3f, start.z), Quaternion.identity);
                        WaitForPhase(4, 1f);
                        break;
                    case 4:
                        Check(player != null && course.ActiveStage == 1 && !course.Finished,
                            "Falling must retain the current checkpoint.");
                        CheckNearStart(course, player, 1);
                        Pass("Falling below stage two returns to checkpoint two");
                        SessionState.SetInt(Key + "goal", 1);
                        WarpToGoal(course, player, 1);
                        WaitForPhase(5, 0.25f);
                        break;
                    case 5:
                        int goal = SessionState.GetInt(Key + "goal", 1);
                        bool last = goal == course.StageCount - 1;
                        Check(player != null && course.ActiveStage == (last ? goal : goal + 1)
                            && course.Finished == last, "Goal " + (goal + 1) + " must advance exactly once.");
                        if (!last) CheckNearStart(course, player, goal + 1);
                        Pass("Goal " + (goal + 1) + (last ? " completes the course" : " advances to checkpoint " + (goal + 2)));
                        if (!last)
                        {
                            SessionState.SetInt(Key + "goal", goal + 1);
                            WarpToGoal(course, player, goal + 1);
                            WaitForPhase(5, 0.25f);
                        }
                        else
                        {
                            CheckOneCameraAndListener();
                            session.StopSession();
                            WaitForPhase(6, 1f);
                        }
                        break;
                    case 6:
                        if (manager.IsListening || manager.ShutdownInProgress || Motors().Length > 0) return;
                        Check(FindOwnedMotor() == null, "The network owner must despawn after session stop.");
                        CheckLobby();
                        Pass("Stopping the session restores the original lobby without a leftover capsule");
                        Complete(null);
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected validation phase " + phase);
                }
            }
            catch (Exception error) { Complete(error); }
        }

        private static void WaitForPhase(int phase, float seconds)
        {
            SessionState.SetInt(Key + "phase", phase);
            SessionState.SetFloat(Key + "next", (float)EditorApplication.timeSinceStartup + seconds);
        }

        private static CapsulePlayerController[] Motors() =>
            UnityEngine.Object.FindObjectsByType<CapsulePlayerController>(FindObjectsInactive.Include);

        private static CapsulePlayerController FindOwnedMotor()
        {
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<NetworkPlayer>())
                if (candidate.IsSpawned && candidate.IsOwner) return candidate.GetComponent<CapsulePlayerController>();
            return null;
        }

        private static void CheckLobby()
        {
            var lobby = GameObject.Find("Lobby Camera");
            Check(lobby != null && lobby.GetComponent<Camera>().isActiveAndEnabled
                && lobby.GetComponent<AudioListener>().isActiveAndEnabled, "The original lobby camera must be enabled.");
            CheckOneCameraAndListener();
        }

        private static void CheckOneCameraAndListener()
        {
            int cameras = 0, listeners = 0;
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>())
                if (camera.isActiveAndEnabled) cameras++;
            foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>())
                if (listener.isActiveAndEnabled) listeners++;
            Check(cameras == 1 && listeners == 1,
                "Expected one camera and listener; found " + cameras + " and " + listeners + ".");
        }

        private static Vector3 StartPosition(KitsuneSurfCourse course, int stage)
        {
            var start = new SerializedObject(course).FindProperty("stageStarts")
                .GetArrayElementAtIndex(stage).objectReferenceValue as Transform;
            Check(start != null, "Checkpoint " + (stage + 1) + " is missing.");
            return start.position;
        }

        private static void CheckNearStart(KitsuneSurfCourse course, CapsulePlayerController player, int stage) =>
            Check(Vector3.Distance(player.transform.position, StartPosition(course, stage)) < 0.4f,
                "Player did not return to checkpoint " + (stage + 1) + ".");

        private static void WarpToGoal(KitsuneSurfCourse course, CapsulePlayerController player, int stage) =>
            player.Warp(new SerializedObject(course).FindProperty("goalCenters")
                .GetArrayElementAtIndex(stage).vector3Value, Quaternion.identity);

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Pass(string message) => SessionState.SetString(Key + "results",
            SessionState.GetString(Key + "results", string.Empty) + "PASS " + message + "\n");

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (SessionState.GetBool(Key + "active", false)
                && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                && string.IsNullOrEmpty(SessionState.GetString(Key + "error", string.Empty)))
                SessionState.SetString(Key + "error", condition + "\n" + stackTrace);
        }

        private static void Complete(Exception error)
        {
            SessionState.SetBool(Key + "active", false);
            string result = SessionState.GetString(Key + "results", string.Empty)
                + (error == null ? Passed : "FAIL " + error + "\n" + Failed) + "\n";
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, result);
            SessionState.EraseString(Key + "results");
            SessionState.EraseString(Key + "error");
            SessionState.EraseString(Key + "deadline");
            SessionState.EraseInt(Key + "phase");
            SessionState.EraseInt(Key + "goal");
            SessionState.EraseFloat(Key + "next");
            SessionState.EraseBool(Key + "active");
            try
            {
                var session = UnityEngine.Object.FindAnyObjectByType<MultiplayerSession>();
                if (session != null && session.IsSessionActive) session.StopSession();
            }
            finally { EditorApplication.isPlaying = false; }
            if (error == null) Debug.Log(Passed + "\n" + result);
            else Debug.LogError(Failed + "\n" + result);
        }
    }
}
