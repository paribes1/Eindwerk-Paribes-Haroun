#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Eindwerk.Multiplayer;
using Eindwerk.PlayerMechanics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eindwerk.Tests
{
    /// <summary>
    /// Test multiplayer met twee aparte processen, alleen als de test expliciet wordt gestart.
    /// Start de testbuild met -mechanicsSmoke host/client en een eigen -smokeOutput per proces.
    /// </summary>
    public sealed class MultiplayerSmokeRunner : MonoBehaviour
    {
        private const float TimeoutSeconds = 40f;
        private static readonly Vector3 ClientTestPosition = new Vector3(3f, 0.1f, -7f);

        [Serializable]
        private sealed class Report
        {
            public string mode;
            public bool passed;
            public string message;
            public string sessionStatus;
            public bool ownershipChecked;
            public bool remoteMovementObserved;
            public bool remoteCrouchObserved;
            public bool reconnectObserved;
            public int maximumSpawnedPlayers;
            public int remoteConnections;
            public int remoteDisconnects;
            public float durationSeconds;
        }

        private readonly Report report = new Report();
        private MultiplayerSession session;
        private NetworkManager manager;
        private string outputPath;
        private float startedAt;
        private bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateWhenRequested()
        {
            string mode = GetArgument("-mechanicsSmoke");
            if (mode != "host" && mode != "client")
                return;
            var runner = new GameObject("Explicit Multiplayer Smoke Test").AddComponent<MultiplayerSmokeRunner>();
            DontDestroyOnLoad(runner.gameObject);
            runner.report.mode = mode;
            runner.outputPath = GetArgument("-smokeOutput");
            runner.startedAt = Time.realtimeSinceStartup;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
        }

        private IEnumerator Start()
        {
            // Wacht tot de startscene geladen is voordat de test Map opent.
            yield return null;
            if (SceneManager.GetActiveScene().name != "Map")
                yield return SceneManager.LoadSceneAsync("Map", LoadSceneMode.Single);
            yield return null;

            session = FindAnyObjectByType<MultiplayerSession>();
            manager = FindAnyObjectByType<NetworkManager>();
            if (!Require(session != null && manager != null, "Map does not contain the multiplayer session and manager."))
                yield break;

            if (report.mode == "host")
            {
                manager.OnClientConnectedCallback += OnHostClientConnected;
                manager.OnClientDisconnectCallback += OnHostClientDisconnected;
            }

            if (!session.IsSessionActive)
            {
                string hostAddress = GetArgument("-networkAddress") ?? "127.0.0.1";
                ushort udpPort = ushort.TryParse(GetArgument("-networkPort"), out ushort parsed) ? parsed : (ushort)7777;
                session.SetEndpoint(hostAddress, udpPort);
                bool started = report.mode == "host" ? session.StartHost() : session.StartClient();
                if (!Require(started, "Network session did not start: " + session.Status))
                    yield break;
            }

            if (report.mode == "host")
                yield return RunHost();
            else
                yield return RunClient();
        }

        private void Update()
        {
            if (finished)
                return;
            if (startedAt > 0f && Time.realtimeSinceStartup - startedAt > TimeoutSeconds)
                Finish(false, "Global smoke-test timeout at the last stage. " + (session != null ? session.Status : "Loading Map"));
            NetworkPlayer[] players = SpawnedPlayers();
            report.maximumSpawnedPlayers = Mathf.Max(report.maximumSpawnedPlayers, players.Length);
        }

        /// <summary>Controleert of de host beweging, hurken en opnieuw verbinden van de client ziet.</summary>
        private IEnumerator RunHost()
        {
            Debug.Log("SMOKE_STAGE host waiting for first remote player");
            yield return WaitFor(() => SpawnedPlayers().Length == 2, "Host did not receive the first client.");
            if (finished || !CheckOwnership()) yield break;
            // Tel ook een client mee die al voor het aanmelden van de netwerkreacties verbonden was.
            report.remoteConnections = Mathf.Max(1, report.remoteConnections);

            Debug.Log("SMOKE_STAGE host checking replicated movement and crouch");
            yield return WaitFor(() =>
            {
                NetworkPlayer remote = SpawnedPlayers().FirstOrDefault(player => !player.IsOwner);
                if (remote == null) return false;
                Vector3 delta = remote.transform.position - ClientTestPosition;
                if (new Vector2(delta.x, delta.z).magnitude < 0.25f)
                    report.remoteMovementObserved = true;
                if (remote.GetComponent<CapsulePlayerController>().IsCrouching)
                    report.remoteCrouchObserved = true;
                return report.remoteMovementObserved && report.remoteCrouchObserved;
            }, "Host did not observe the client's synchronized position and crouch.");
            if (finished) yield break;

            Debug.Log("SMOKE_STAGE host checking disconnect then reconnect");
            yield return WaitFor(() => report.remoteDisconnects >= 1, "Host did not observe the first client disconnect.");
            if (finished) yield break;
            yield return WaitFor(() => report.remoteConnections >= 2 && SpawnedPlayers().Length == 2,
                "Host did not observe the client reconnect with a newly spawned player.");
            if (finished || !CheckOwnership()) yield break;
            report.reconnectObserved = true;
            yield return WaitFor(() => report.remoteDisconnects >= 2 && SpawnedPlayers().Length == 1,
                "Host did not remove the reconnected client after its final disconnect.");
            if (finished) yield break;
            session.StopSession();
            Finish(true, "Host observed two peers, remote movement, remote crouch, disconnect, reconnect and final cleanup.");
        }

        /// <summary>Laat de client bewegen, hurken, verbreken en opnieuw verbinden.</summary>
        private IEnumerator RunClient()
        {
            Debug.Log("SMOKE_STAGE client waiting for two players");
            yield return WaitFor(() => manager.IsConnectedClient && SpawnedPlayers().Length == 2,
                "Client did not connect and spawn both players.");
            if (finished || !CheckOwnership()) yield break;

            ulong firstClientId = manager.LocalClientId;
            CapsulePlayerController controller = manager.LocalClient.PlayerObject.GetComponent<CapsulePlayerController>();
            // De test stuurt zelf de beweging; schakel toetsenbordinvoer uit zodat hurken actief blijft.
            controller.enabled = false;
            controller.Warp(ClientTestPosition, Quaternion.Euler(0f, 90f, 0f));
            Debug.Log("SMOKE_STAGE client holding moved and crouched pose");
            float holdUntil = Time.realtimeSinceStartup + 3f;
            while (!finished && Time.realtimeSinceStartup < holdUntil)
            {
                controller.SimulateMovement(Vector2.zero, false, true, false, Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f));
                yield return null;
            }
            if (finished || !Require(controller.IsCrouching, "Client simulation did not enter crouch."))
                yield break;

            session.StopSession();
            yield return WaitFor(() => !manager.IsListening && !manager.ShutdownInProgress && SpawnedPlayers().Length == 0,
                "Client did not shut down and remove its old player objects.");
            if (finished) yield break;
            yield return new WaitForSecondsRealtime(0.75f);
            if (!Require(session.StartClient(), "Client could not restart its connection."))
                yield break;

            Debug.Log("SMOKE_STAGE client reconnecting");
            yield return WaitFor(() => manager.IsConnectedClient && SpawnedPlayers().Length == 2,
                "Client could not reconnect and spawn both players.");
            if (finished || !CheckOwnership()) yield break;
            if (!Require(manager.LocalClientId != firstClientId, "Reconnect did not assign a fresh client identity."))
                yield break;
            report.reconnectObserved = true;
            yield return new WaitForSecondsRealtime(1f);
            session.StopSession();
            yield return WaitFor(() => !manager.IsListening && !manager.ShutdownInProgress,
                "Reconnected client did not shut down cleanly.");
            if (finished) yield break;
            yield return new WaitForSecondsRealtime(0.5f);
            Finish(true, "Client verified ownership, sent movement and crouch, removed old avatars, reconnected and shut down cleanly.");
        }

        /// <summary>Controleert dat alleen de eigen speler besturing, een camera en geluid heeft.</summary>
        private bool CheckOwnership()
        {
            NetworkPlayer[] players = SpawnedPlayers();
            if (!Require(players.Length == 2, "Expected exactly two spawned network players.")) return false;
            if (!Require(players.Count(player => player.IsOwner) == 1, "Expected exactly one locally owned player.")) return false;
            foreach (NetworkPlayer player in players)
            {
                CapsulePlayerController controller = player.GetComponent<CapsulePlayerController>();
                CharacterController collider = player.GetComponent<CharacterController>();
                Camera camera = player.GetComponentInChildren<Camera>(true);
                AudioListener listener = player.GetComponentInChildren<AudioListener>(true);
                if (!Require(controller != null && collider != null && camera != null && listener != null,
                    "Network player lacks the controller, camera or listener.")) return false;
                if (!Require(controller.HasLocalControl == player.IsOwner, "Input ownership is incorrect.")) return false;
                if (!Require(collider.enabled == player.IsOwner, "Remote CharacterController is active or local controller is inactive.")) return false;
                if (!Require(camera.enabled == player.IsOwner && listener.enabled == player.IsOwner,
                    "Camera or audio listener ownership is incorrect.")) return false;
            }
            report.ownershipChecked = true;
            return true;
        }

        private IEnumerator WaitFor(Func<bool> predicate, string failure)
        {
            while (!finished)
            {
                bool ready;
                try { ready = predicate(); }
                catch (Exception exception)
                {
                    Finish(false, failure + " " + exception);
                    yield break;
                }
                if (ready) yield break;
                if (Time.realtimeSinceStartup - startedAt > TimeoutSeconds)
                {
                    Finish(false, failure);
                    yield break;
                }
                yield return null;
            }
        }

        private void OnHostClientConnected(ulong clientId)
        {
            if (clientId != manager.LocalClientId)
                report.remoteConnections++;
        }

        private void OnHostClientDisconnected(ulong clientId)
        {
            if (clientId != manager.LocalClientId)
                report.remoteDisconnects++;
        }

        private bool Require(bool condition, string failure)
        {
            if (!condition) Finish(false, failure);
            return condition;
        }

        private void Finish(bool passed, string message)
        {
            if (finished) return;
            finished = true;
            report.passed = passed;
            report.message = message;
            report.sessionStatus = session != null ? session.Status : "Map not loaded";
            report.durationSeconds = Time.realtimeSinceStartup - startedAt;
            string json = JsonUtility.ToJson(report, true);
            if (!string.IsNullOrEmpty(outputPath))
            {
                try
                {
                    string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(outputPath, json);
                }
                catch (Exception exception)
                {
                    passed = false;
                    Debug.LogError("Could not write smoke proof: " + exception);
                }
            }
            if (passed) Debug.Log("SMOKE_PASS " + json);
            else Debug.LogError("SMOKE_FAIL " + json);
            if (session != null && session.IsSessionActive) session.StopSession();
            Application.Quit(passed ? 0 : 1);
        }

        private static NetworkPlayer[] SpawnedPlayers() =>
            FindObjectsByType<NetworkPlayer>().Where(player => player.IsSpawned).ToArray();

        private static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 1; i + 1 < arguments.Length; i++)
                if (arguments[i] == name) return arguments[i + 1];
            return null;
        }
    }
}
#endif
