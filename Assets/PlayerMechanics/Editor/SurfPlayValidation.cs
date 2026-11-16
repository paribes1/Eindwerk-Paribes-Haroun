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
    /// <summary>Play-mode integration checks that survive the editor's domain reload.</summary>
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

        [MenuItem("Tools/Surf/Validate Play Mode Integration")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Start surf Play validation from stable Edit mode.");
            if (SessionState.GetBool(Key + "active", false))
                throw new InvalidOperationException("Surf Play validation is already running.");
            for (int index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty)
                    throw new InvalidOperationException("Save modified scenes before surf Play validation.");

            EditorSceneManager.OpenScene(MechanicsSceneBuilder.MultiplayerScene);
            SessionState.SetString(Key + "results", DateTime.UtcNow.ToString("O") + "\n");
            SessionState.SetString(Key + "error", string.Empty);
            SessionState.SetString(Key + "deadline", DateTime.UtcNow.AddSeconds(30).Ticks.ToString());
            SessionState.SetInt(Key + "phase", 0);
            SessionState.SetInt(Key + "goal", 0);
            SessionState.SetFloat(Key + "next", 0f);
            SessionState.SetBool(Key + "active", true);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, "SURF_PLAY_VALIDATION_RUNNING\n");
            Debug.Log("SURF_PLAY_VALIDATION_STARTED");
            EditorApplication.isPlaying = true;
        }

        private static void Poll()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                string loggedError = SessionState.GetString(Key + "error", string.Empty);
                if (!string.IsNullOrEmpty(loggedError))
                    throw new InvalidOperationException("Runtime error during Play validation: " + loggedError);
                if (!long.TryParse(SessionState.GetString(Key + "deadline", "0"), out long deadline)
                    || DateTime.UtcNow.Ticks > deadline)
                    throw new TimeoutException("Surf Play validation exceeded its 30 second deadline in phase "
                        + SessionState.GetInt(Key + "phase", 0) + ".");

                int phase = SessionState.GetInt(Key + "phase", 0);
                if (phase == 0)
                {
                    if (EditorApplication.isPlaying) WaitForPhase(1, 1f);
                    return;
                }
                if (!EditorApplication.isPlaying)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        throw new InvalidOperationException("Play mode ended before validation completed.");
                    return;
                }
                if (EditorApplication.isPaused || EditorApplication.isCompiling
                    || EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "next", 0f)) return;

                KitsuneSurfCourse course = UnityEngine.Object.FindAnyObjectByType<KitsuneSurfCourse>();
                Check(course != null && course.enabled && course.StageCount == 9,
                    "Map contains an enabled nine-stage course");
                CapsulePlayerController solo = FindSolo();
                MultiplayerSession session = UnityEngine.Object.FindAnyObjectByType<MultiplayerSession>();
                Check(session != null, "Map retains its LAN session component");

                switch (phase)
                {
                    case 1:
                        CheckSolo(solo);
                        CheckOneCameraAndListener();
                        Pass("Solo Play spawns a locally controlled first-person surf player with one camera and listener");
                        solo.ReleaseCursor();
                        Vector3 start = StartPosition(course, 0);
                        float killHeight = new SerializedObject(course).FindProperty("killHeights")
                            .GetArrayElementAtIndex(0).floatValue;
                        solo.Warp(new Vector3(start.x, killHeight - 3f, start.z), Quaternion.identity);
                        WaitForPhase(2, 1f);
                        break;
                    case 2:
                        CheckSolo(solo);
                        Check(course.ActiveStage == 0 && !course.Finished
                            && Vector3.Distance(solo.transform.position, StartPosition(course, 0)) < 0.4f,
                            "Falling below the stage boundary returns the player to its checkpoint");
                        Pass("Fall reset returns to stage one without losing local control");
                        SessionState.SetInt(Key + "goal", 0);
                        WarpToGoal(course, solo, 0);
                        WaitForPhase(3, 0.25f);
                        break;
                    case 3:
                        int goal = SessionState.GetInt(Key + "goal", 0);
                        bool last = goal == course.StageCount - 1;
                        Check(course.ActiveStage == (last ? goal : goal + 1) && course.Finished == last,
                            "Goal " + (goal + 1) + " advances the course exactly once");
                        if (!last)
                            Check(Vector3.Distance(solo.transform.position, StartPosition(course, goal + 1)) < 0.4f,
                                "Goal transition uses the next checkpoint spawn");
                        Pass("Goal " + (goal + 1) + (last ? " completes the course" : " advances to checkpoint " + (goal + 2)));
                        if (!last)
                        {
                            SessionState.SetInt(Key + "goal", goal + 1);
                            WarpToGoal(course, solo, goal + 1);
                            WaitForPhase(3, 0.25f);
                        }
                        else
                        {
                            course.RestartRun();
                            Check(course.ActiveStage == 0 && !course.Finished && course.RunTime == 0f,
                                "Restart clears stage progress, completion and run time");
                            Pass("Restart clears the completed run and returns to the first checkpoint");
                            WaitForPhase(4, 1f);
                        }
                        break;
                    case 4:
                        CheckSolo(solo);
                        Check(!session.IsSessionActive, "No LAN session is active before the host smoke check");
                        session.SetEndpoint("127.0.0.1", 18777);
                        Check(session.StartHost(), "LAN host starts on validation port 18777");
                        WaitForPhase(5, 1f);
                        break;
                    case 5:
                        NetworkManager manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
                        NetworkPlayer owner = FindOwner();
                        if (manager == null || !manager.IsHost || owner == null || solo == null
                            || solo.gameObject.activeInHierarchy) return;
                        CapsulePlayerController ownerMotor = owner.GetComponent<CapsulePlayerController>();
                        if (!ownerMotor.HasLocalControl || !ownerMotor.SurfEnabled || !ownerMotor.IsFirstPerson) return;
                        Check(!solo.HasLocalControl, "Solo control is disabled while the LAN owner is active");
                        CheckOneCameraAndListener();
                        Pass("LAN host spawns its owned surf player, suspends solo practice and keeps one camera and listener");
                        session.StopSession();
                        WaitForPhase(6, 1f);
                        break;
                    case 6:
                        NetworkManager stoppedManager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
                        if (stoppedManager != null && (stoppedManager.IsListening || stoppedManager.ShutdownInProgress)) return;
                        if (solo == null || !solo.gameObject.activeInHierarchy || !solo.HasLocalControl) return;
                        CheckSolo(solo);
                        Check(FindOwner() == null, "Network owner despawns after stopping the session");
                        CheckOneCameraAndListener();
                        Check(course.ActiveStage == 0 && !course.Finished,
                            "Restored solo practice starts a fresh course");
                        Pass("Stopping LAN restores solo practice, checkpoint state and one camera and listener");
                        Complete(null);
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected surf Play validation phase " + phase);
                }
            }
            catch (Exception error)
            {
                Complete(error);
            }
        }

        private static void WaitForPhase(int phase, float seconds)
        {
            SessionState.SetInt(Key + "phase", phase);
            SessionState.SetFloat(Key + "next", (float)EditorApplication.timeSinceStartup + seconds);
        }

        private static CapsulePlayerController FindSolo()
        {
            foreach (var controller in UnityEngine.Object.FindObjectsByType<CapsulePlayerController>(
                FindObjectsInactive.Include))
                if (controller.name == "Solo Surf Player" && controller.GetComponent<NetworkPlayer>() == null)
                    return controller;
            return null;
        }

        private static NetworkPlayer FindOwner()
        {
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<NetworkPlayer>())
                if (candidate.IsSpawned && candidate.IsOwner) return candidate;
            return null;
        }

        private static void CheckSolo(CapsulePlayerController solo)
        {
            Check(solo != null && solo.gameObject.activeInHierarchy && solo.HasLocalControl
                && solo.SurfEnabled && solo.IsFirstPerson && solo.GetComponent<CharacterController>().enabled,
                "Solo practice has an active locally controlled first-person surf capsule");
        }

        private static void CheckOneCameraAndListener()
        {
            int cameras = 0, listeners = 0;
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>())
                if (camera.isActiveAndEnabled) cameras++;
            foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>())
                if (listener.isActiveAndEnabled) listeners++;
            Check(cameras == 1 && listeners == 1,
                "Expected one enabled runtime camera and listener; found " + cameras + " cameras and " + listeners + " listeners");
        }

        private static Vector3 StartPosition(KitsuneSurfCourse course, int stage)
        {
            var starts = new SerializedObject(course).FindProperty("stageStarts");
            var start = starts.GetArrayElementAtIndex(stage).objectReferenceValue as Transform;
            Check(start != null, "Checkpoint " + (stage + 1) + " exists");
            return start.position;
        }

        private static void WarpToGoal(KitsuneSurfCourse course, CapsulePlayerController solo, int stage)
        {
            CheckSolo(solo);
            Vector3 center = new SerializedObject(course).FindProperty("goalCenters")
                .GetArrayElementAtIndex(stage).vector3Value;
            solo.Warp(center, Quaternion.identity);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Pass(string message)
        {
            SessionState.SetString(Key + "results", SessionState.GetString(Key + "results", string.Empty)
                + "PASS " + message + "\n");
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!SessionState.GetBool(Key + "active", false)
                || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                || !string.IsNullOrEmpty(SessionState.GetString(Key + "error", string.Empty))) return;
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
            finally
            {
                EditorApplication.isPlaying = false;
            }
            if (error == null) Debug.Log(Passed + "\n" + result);
            else Debug.LogError(Failed + "\n" + result);
        }
    }
}
