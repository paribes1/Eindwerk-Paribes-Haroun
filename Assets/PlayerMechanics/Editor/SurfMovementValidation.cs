using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eindwerk.PlayerMechanics.Editor
{
    /// <summary>Real capsule/surface checks, isolated from the player's authored scenes.</summary>
    public static class SurfMovementValidation
    {
        private const float Step = 1f / 120f;
        private static readonly StringBuilder Results = new StringBuilder();

        [MenuItem("Tools/Surf/Validate movement")]
        public static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before validating surf movement.");
            Scene original = SceneManager.GetActiveScene();
            Scene checks = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(checks);
            Results.Clear();
            try
            {
                Vector3 origin = new Vector3(10000f, 500f, 0f);
                GameObject floor = Box("Validation floor", origin + Vector3.down * 0.5f, new Vector3(40f, 1f, 200f));
                GameObject wall = Box("Validation wall", origin + new Vector3(0f, 20f, 30f), new Vector3(20f, 40f, 1f));
                GameObject player = new GameObject("Validation surf capsule");
                CharacterController capsule = player.AddComponent<CharacterController>();
                capsule.height = 2f;
                capsule.center = Vector3.up;
                capsule.radius = 0.4f;
                capsule.skinWidth = 0.035f;
                var controller = player.AddComponent<CapsulePlayerController>();
                // Edit-mode validation has no automatic Update, but keep the behaviour enabled
                // so CharacterController collision messages reach its contact-plane collector.
                controller.enabled = true;
                controller.SetLocalControl(true);
                controller.ConfigureSurf();
                controller.Warp(origin + Vector3.up * 0.08f, Quaternion.identity);
                Advance(controller, Vector2.zero, 30);
                Check(controller.SurfEnabled && controller.IsFirstPerson && controller.IsGrounded,
                    "Surf configuration, first-person view and flat ground classification");

                Advance(controller, Vector2.up, 120);
                Check(controller.CurrentSpeed > 7.8f && controller.CurrentSpeed < 8.1f,
                    "Ground directional acceleration reaches configured movement speed");
                controller.SimulateMovement(Vector2.up, false, false, true, Step);
                float takeoffSpeed = controller.CurrentSpeed;
                Advance(controller, Vector2.zero, 18);
                Check(!controller.IsGrounded && controller.Velocity.y > 0f
                    && Mathf.Abs(controller.CurrentSpeed - takeoffSpeed) < 0.03f,
                    "Jump preserves horizontal momentum; no air braking without input");
                controller.SimulateMovement(Vector2.right, false, false, false, Step);
                Check(controller.Velocity.x > 0.7f && controller.Velocity.x < 0.8f
                    && controller.CurrentSpeed > takeoffSpeed,
                    "Air strafing adds directional speed without clamping total momentum");

                controller.Warp(origin + Vector3.up * 0.08f, Quaternion.identity);
                Advance(controller, Vector2.zero, 30);
                int takeoffs = 0;
                bool wasRising = false;
                for (int i = 0; i < 400; i++)
                {
                    controller.SimulateMovement(Vector2.up, false, false, true, Step);
                    bool rising = controller.Velocity.y > 4f;
                    if (rising && !wasRising) takeoffs++;
                    wasRising = rising;
                }
                Check(takeoffs >= 4, "Holding jump automatically bunnyhops on flat landing");

                controller.Warp(origin + new Vector3(0f, 0.08f, 24f), Quaternion.identity);
                Advance(controller, Vector2.up, 180);
                Check(controller.transform.position.z < 29.3f && Mathf.Abs(controller.Velocity.z) < 0.05f,
                    $"Wall contact removes inward velocity instead of storing blocked momentum " +
                    $"(position={controller.transform.position:F4}, velocity={controller.Velocity:F4}, " +
                    $"actual={capsule.velocity:F4}, collisions={capsule.collisionFlags})");

                floor.SetActive(false);
                wall.SetActive(false);
                GameObject ramp = Box("Validation 55 degree surf face", origin, new Vector3(22f, 0.5f, 120f));
                ramp.transform.rotation = Quaternion.Euler(0f, 0f, 55f);
                Vector3 rampStart = origin + new Vector3(5f, 5f * Mathf.Tan(55f * Mathf.Deg2Rad) + 2f, 0f);
                controller.Warp(rampStart, Quaternion.identity);
                Physics.SyncTransforms();
                bool touchedRamp = false;
                float fastest = 0f;
                for (int i = 0; i < 180; i++)
                {
                    controller.SimulateMovement(Vector2.zero, false, false, false, Step);
                    touchedRamp |= controller.IsSurfing;
                    fastest = Mathf.Max(fastest, controller.CurrentSpeed);
                    Check(!controller.IsGrounded, "Steep ramp is not walkable ground", false);
                }
                Check(touchedRamp && fastest > 5f && controller.transform.position.y < rampStart.y - 4f,
                    $"Gravity projects along steep face and accelerates a real capsule downhill " +
                    $"(touched={touchedRamp}, fastest={fastest:F4}, position={controller.transform.position:F4}, " +
                    $"start={rampStart:F4}, velocity={controller.Velocity:F4})");
                controller.Warp(rampStart, Quaternion.identity);
                Check(!controller.IsSurfing && controller.Velocity.sqrMagnitude < 0.0001f,
                    "Checkpoint warp clears velocity and previous surface contacts");
                controller.SetLocalControl(false);
                Vector3 remote = controller.transform.position;
                Advance(controller, Vector2.up, 20);
                Check(controller.transform.position == remote && !capsule.enabled,
                    "Remote player ownership prevents local surf simulation");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/SurfMovementValidation.txt", Results.ToString());
                Debug.Log("SURF_MOVEMENT_VALIDATION_PASSED\n" + Results);
            }
            finally
            {
                if (original.IsValid() && original.isLoaded)
                    SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(checks, true);
            }
        }

        private static GameObject Box(string name, Vector3 position, Vector3 scale)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetPositionAndRotation(position, Quaternion.identity);
            box.transform.localScale = scale;
            return box;
        }

        private static void Advance(CapsulePlayerController controller, Vector2 movement, int count)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < count; i++)
                controller.SimulateMovement(movement, false, false, false, Step);
        }

        private static void Check(bool condition, string message, bool log = true)
        {
            if (!condition)
                throw new InvalidOperationException("Surf validation failed: " + message);
            if (log)
                Results.AppendLine("PASS " + message);
        }
    }
}
