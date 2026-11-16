using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Eindwerk.PlayerMechanics.Editor
{
    /// <summary>Explicit file-triggered editor jobs; no scene changes on ordinary imports.</summary>
    [InitializeOnLoad]
    public static class SurfEditorTasks
    {
        private const string Request = "Temp/SurfEditorRequest.txt";
        private const string Report = "Logs/SurfEditorReport.txt";

        static SurfEditorTasks() => EditorApplication.update += Poll;

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
            string action = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            try
            {
                if (action == "build")
                {
                    if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building.");
                    EditorSceneManager.SaveOpenScenes();
                    KitsuneSurfSceneBuilder.Build();
                    SurfMovementValidation.Validate();
                    MechanicsValidation.Validate();
                    EditorSceneManager.OpenScene(MechanicsSceneBuilder.MultiplayerScene);
                    ValidateCourse();
                    Write("PASS: Course built, surf physics passed, original mechanics passed, Map scene passed.");
                }
                else if (action == "validate")
                {
                    SurfMovementValidation.Validate();
                    EditorSceneManager.OpenScene(MechanicsSceneBuilder.MultiplayerScene);
                    ValidateCourse();
                    Write("PASS: Surf physics and course validation.");
                }
                else if (action == "play")
                {
                    EditorApplication.isPlaying = true;
                    Write("PLAY_REQUESTED");
                }
                else if (action == "playcheck")
                {
                    SurfPlayValidation.Begin();
                    Write("PLAY_VALIDATION_STARTED");
                }
                else if (action == "stop")
                {
                    EditorApplication.isPlaying = false;
                    Write("STOP_REQUESTED");
                }
                else throw new InvalidOperationException("Unknown surf editor request: " + action);
            }
            catch (Exception error)
            {
                Write("FAIL: " + error);
                Debug.LogException(error);
            }
        }

        private static void ValidateCourse()
        {
            var course = UnityEngine.Object.FindAnyObjectByType<KitsuneSurfCourse>();
            if (course == null || course.StageCount != 9) throw new InvalidOperationException("Expected 9 surf stages in Map.");
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) != 0)
                    throw new InvalidOperationException("Missing script: " + go.name);
            if (UnityEngine.Object.FindObjectsByType<MeshCollider>().Length < 18)
                throw new InvalidOperationException("Course needs at least 18 surf ramp colliders.");
            var data = new SerializedObject(course);
            var starts = data.FindProperty("stageStarts");
            var centers = data.FindProperty("goalCenters");
            var sizes = data.FindProperty("goalSizes");
            for (int i = 0; i < course.StageCount; i++)
            {
                var start = (Transform)starts.GetArrayElementAtIndex(i).objectReferenceValue;
                if (start == null || !Physics.Raycast(start.position + Vector3.up, Vector3.down, 3f))
                    throw new InvalidOperationException("Start is unsupported: " + (i + 1));
                Vector3 size = sizes.GetArrayElementAtIndex(i).vector3Value;
                if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                    throw new InvalidOperationException("Invalid goal bounds: " + (i + 1));
                if (Vector3.Distance(start.position, centers.GetArrayElementAtIndex(i).vector3Value) < 30f)
                    throw new InvalidOperationException("Stage goal too near start: " + (i + 1));
            }
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                var first = (Transform)starts.GetArrayElementAtIndex(0).objectReferenceValue;
                view.LookAt(first.position + new Vector3(0, -35, 85), Quaternion.Euler(25, -28, 0), 115f);
                view.Repaint();
            }
        }

        private static void Write(string message)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, DateTime.UtcNow.ToString("O") + "\n" + message);
            Debug.Log(message);
        }
    }
}
