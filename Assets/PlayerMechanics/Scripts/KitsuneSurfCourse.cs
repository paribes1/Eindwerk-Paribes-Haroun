using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Eindwerk.PlayerMechanics
{
    /// <summary>Surf checkpoints for the existing multiplayer player and session flow.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class KitsuneSurfCourse : MonoBehaviour
    {
        [SerializeField] private Transform[] stageStarts = Array.Empty<Transform>();
        [SerializeField] private Vector3[] goalCenters = Array.Empty<Vector3>();
        [SerializeField] private Vector3[] goalSizes = Array.Empty<Vector3>();
        [SerializeField] private float[] killHeights = Array.Empty<float>();

        private CapsulePlayerController player;
        private Vector3 previousPosition;
        private float nextPlayerSearch;
        private int stage;
        private bool finished;

        public int StageCount => stageStarts.Length;
        public int ActiveStage => stage;
        public bool Finished => finished;

        public void ConfigureCourse(Transform[] starts, Vector3[] centers, Vector3[] sizes, float[] floors)
        {
            stageStarts = starts;
            goalCenters = centers;
            goalSizes = sizes;
            killHeights = floors;
        }

        private void Start()
        {
            if (StageCount == 0 || goalCenters.Length != StageCount || goalSizes.Length != StageCount
                || killHeights.Length != StageCount)
            {
                Debug.LogError("Surf checkpoints need a start, goal and fall height per stage.", this);
                enabled = false;
            }
        }

        private void FindLocalPlayer()
        {
            CapsulePlayerController candidate = null;
            foreach (CapsulePlayerController motor in FindObjectsByType<CapsulePlayerController>())
                if (motor.HasLocalControl) { candidate = motor; break; }
            if (candidate == player) return;
            player = candidate;
            if (player == null) return;

            stage = 0;
            finished = false;
            player.ConfigureSurf();
            // Keep the original session's allocated spawn until the first checkpoint.
            player.SetSpawnPoint(player.transform.position, player.transform.rotation);
            previousPosition = player.transform.position;
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + 0.15f;
                FindLocalPlayer();
            }
            if (player == null || !player.HasLocalControl || !player.gameObject.activeInHierarchy) return;

            // The original controller also handles R. Reset the sweep so a respawn
            // cannot be mistaken for movement through a checkpoint.
            if (player.CursorCaptured && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RetryStage();
                return;
            }
            Vector3 position = player.transform.position;
            if (position.y < killHeights[stage])
            {
                RetryStage();
                return;
            }

            Bounds goal = new Bounds(goalCenters[stage], goalSizes[stage]);
            Vector3 segment = position - previousPosition;
            bool crossed = goal.Contains(position);
            if (!crossed && segment.sqrMagnitude > 0.0001f)
                crossed = goal.IntersectRay(new Ray(previousPosition, segment.normalized), out float distance)
                    && distance <= segment.magnitude;
            if (!finished && crossed)
            {
                if (stage + 1 == StageCount)
                    finished = true;
                else
                {
                    stage++;
                    RetryStage();
                    return;
                }
            }
            previousPosition = position;
        }

        public void RetryStage()
        {
            if (player == null) return;
            Transform checkpoint = stageStarts[stage];
            player.SetSpawnPoint(checkpoint.position, checkpoint.rotation);
            player.Warp(checkpoint.position, checkpoint.rotation);
            previousPosition = checkpoint.position;
        }
    }
}
