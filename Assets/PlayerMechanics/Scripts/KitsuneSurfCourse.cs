using System;
using Eindwerk.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Eindwerk.PlayerMechanics
{
    /// <summary>Local surf practice and checkpoints, shared by solo and LAN players.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class KitsuneSurfCourse : MonoBehaviour
    {
        [SerializeField] private Transform[] stageStarts = Array.Empty<Transform>();
        [SerializeField] private Vector3[] goalCenters = Array.Empty<Vector3>();
        [SerializeField] private Vector3[] goalSizes = Array.Empty<Vector3>();
        [SerializeField] private float[] killHeights = Array.Empty<float>();
        [SerializeField] private GameObject soloPlayerPrefab;

        private CapsulePlayerController soloPlayer;
        private CapsulePlayerController player;
        private NetworkManager networkManager;
        private MultiplayerSession session;
        private Camera lobbyCamera;
        private Vector3 previousPosition;
        private int stage;
        private int retries;
        private float elapsed;
        private float nextPlayerSearch;
        private bool timerRunning;
        private bool finished;
        private GUIStyle titleStyle, captionStyle, speedStyle, smallStyle;

        public int StageCount => stageStarts.Length;
        public int ActiveStage => stage;
        public bool Finished => finished;
        public float RunTime => elapsed;
        public bool HasSoloPlayer => soloPlayer != null && soloPlayer.gameObject.activeInHierarchy && soloPlayer.HasLocalControl;

        public void ConfigureCourse(Transform[] starts, Vector3[] centers, Vector3[] sizes, float[] floors)
        {
            stageStarts = starts;
            goalCenters = centers;
            goalSizes = sizes;
            killHeights = floors;
        }

        public void ConfigureSoloPrefab(GameObject prefab) => soloPlayerPrefab = prefab;

        private void Start()
        {
            if (!ValidCourse())
            {
                Debug.LogError("Surf course needs one start, goal and fall height per stage.", this);
                enabled = false;
                return;
            }
            networkManager = FindAnyObjectByType<NetworkManager>();
            session = FindAnyObjectByType<MultiplayerSession>();
            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (camera.name == "Lobby Camera") lobbyCamera = camera;
            DiscoverPlayer();
        }

        private bool ValidCourse() => stageStarts.Length > 0 && goalCenters.Length == StageCount
            && goalSizes.Length == StageCount && killHeights.Length == StageCount;

        private bool NetworkActive => networkManager != null
            && (networkManager.IsListening || networkManager.ShutdownInProgress);

        private void DiscoverPlayer()
        {
            bool networkActive = NetworkActive;
            if (soloPlayer != null && soloPlayer.gameObject.activeSelf == networkActive)
            {
                soloPlayer.SetLocalControl(!networkActive);
                soloPlayer.gameObject.SetActive(!networkActive);
            }
            if (!networkActive && !Application.isBatchMode && soloPlayer == null && soloPlayerPrefab != null)
            {
                if (lobbyCamera != null)
                {
                    lobbyCamera.enabled = false;
                    AudioListener listener = lobbyCamera.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = false;
                }
                GameObject instance = Instantiate(soloPlayerPrefab, stageStarts[0].position, stageStarts[0].rotation);
                instance.name = "Solo Surf Player";
                soloPlayer = instance.GetComponent<CapsulePlayerController>();
                soloPlayer.ConfigureSurf();
                if (session != null) soloPlayer.PointerBlocked = session.IsPointerOverMenu;
                soloPlayer.SetLocalControl(true);
            }

            CapsulePlayerController candidate = null;
            if (!networkActive && soloPlayer != null) candidate = soloPlayer;
            else
                foreach (CapsulePlayerController motor in FindObjectsByType<CapsulePlayerController>())
                    if (motor.HasLocalControl && motor != soloPlayer) { candidate = motor; break; }
            if (candidate == player) return;
            player = candidate;
            if (player == null) return;
            player.ConfigureSurf();
            RestartRun();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextPlayerSearch || (player != null && !player.gameObject.activeInHierarchy))
            {
                nextPlayerSearch = Time.unscaledTime + 0.15f;
                DiscoverPlayer();
            }
            if (player == null || !player.HasLocalControl || !player.gameObject.activeInHierarchy) return;

            if (player.CursorCaptured && Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
                RestartRun();
            if (player.CursorCaptured && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RetryStage();
                return;
            }

            Vector3 position = player.transform.position;
            if (!finished && !timerRunning && (position - stageStarts[stage].position).sqrMagnitude > 64f)
                timerRunning = true;
            if (timerRunning && !finished) elapsed += Time.deltaTime;

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
                if (stage + 1 < StageCount)
                {
                    stage++;
                    WarpToStage();
                    return;
                }
                finished = true;
                timerRunning = false;
            }
            previousPosition = position;
        }

        private void LateUpdate()
        {
            // The LAN lobby keeps an overview camera until a network owner spawns.
            // Solo practice owns its own camera during that same period.
            if (soloPlayer != null && soloPlayer.gameObject.activeInHierarchy && lobbyCamera != null)
            {
                lobbyCamera.enabled = false;
                AudioListener listener = lobbyCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }

        public void RestartRun()
        {
            stage = 0;
            retries = 0;
            elapsed = 0f;
            timerRunning = false;
            finished = false;
            WarpToStage();
        }

        public void RetryStage()
        {
            retries++;
            WarpToStage();
        }

        private void WarpToStage()
        {
            if (player == null || !ValidCourse()) return;
            Transform start = stageStarts[stage];
            player.SetSpawnPoint(start.position, start.rotation);
            player.Warp(start.position, start.rotation);
            previousPosition = start.position;
        }

        private void OnGUI()
        {
            if (Application.isBatchMode || !ValidCourse()) return;
            if (titleStyle == null)
            {
                titleStyle = Style(22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
                captionStyle = Style(13, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.7f, 0.76f, 0.84f));
                speedStyle = Style(52, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.52f, 0.15f));
                smallStyle = Style(13, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.8f, 0.84f, 0.9f));
            }
            float center = Screen.width * 0.5f;
            bool menuOpen = player != null && !player.CursorCaptured && session != null;
            if (!menuOpen)
            {
            Panel(new Rect(center - 240, 8, 480, 83));
            GUI.Label(new Rect(center - 220, 14, 440, 32), "K I T S U N E   /   S U R F", titleStyle);
            GUI.Label(new Rect(center - 210, 48, 420, 24),
                finished ? "COURSE COMPLETE   /   " + FormatTime(elapsed)
                    : $"STAGE {stage + 1:00} / {StageCount:00}    ·    {FormatTime(elapsed)}", captionStyle);
            float totalWidth = StageCount * 24f - 4f;
            Color original = GUI.color;
            for (int i = 0; i < StageCount; i++)
            {
                GUI.color = i <= stage ? new Color(1f, 0.46f, 0.12f) : new Color(0.24f, 0.29f, 0.36f);
                GUI.DrawTexture(new Rect(center - totalWidth * 0.5f + i * 24, 78, 20, 3), Texture2D.whiteTexture);
            }
            GUI.color = original;
            }
            if (player == null) return;
            Panel(new Rect(center - 165, Screen.height - 139, 330, 96));
            Panel(new Rect(10, Screen.height - 119, Mathf.Min(405, Screen.width * 0.27f + 16), 110));
            Panel(new Rect(Screen.width - 250, Screen.height - 64, 240, 34));
            GUI.Label(new Rect(center - 140, Screen.height - 134, 280, 66),
                Mathf.RoundToInt(player.SourceUnitsSpeed).ToString(), speedStyle);
            GUI.Label(new Rect(center - 180, Screen.height - 72, 360, 22),
                (player.IsSurfing ? "SURFING" : player.IsGrounded ? "ON GROUND" : "IN AIR") + "   /   UNITS PER SECOND", captionStyle);
            GUI.Label(new Rect(18, Screen.height - 111, Mathf.Min(390, Screen.width * 0.27f), 100),
                "A / D + mouse: surf\nRelease W · Hold Space: bunnyhop\nCtrl: crouch · R: retry · T: restart\nEsc: LAN menu / free cursor", smallStyle);
            GUI.Label(new Rect(Screen.width - 250, Screen.height - 64, 230, 34),
                $"{retries} retries   ·   {(NetworkActive ? "LAN SESSION" : "SOLO PRACTICE")}", captionStyle);
            GUI.DrawTexture(new Rect(center - 3, Screen.height * 0.5f, 6, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center, Screen.height * 0.5f - 3, 1, 6), Texture2D.whiteTexture);
        }

        private static GUIStyle Style(int size, FontStyle weight, TextAnchor alignment, Color color)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight, alignment = alignment, wordWrap = true };
            style.normal.textColor = color;
            return style;
        }

        private static string FormatTime(float time) => $"{(int)(time / 60):00}:{time % 60:00.00}";

        private static void Panel(Rect rectangle)
        {
            Color color = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.045f, 0.85f);
            GUI.DrawTexture(rectangle, Texture2D.whiteTexture);
            GUI.color = color;
        }

        private void OnDestroy()
        {
            if (soloPlayer != null) Destroy(soloPlayer.gameObject);
        }
    }
}
