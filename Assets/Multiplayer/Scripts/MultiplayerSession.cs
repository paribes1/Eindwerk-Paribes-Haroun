using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Eindwerk.Multiplayer
{
    /// <summary>
    /// Direct-IP LAN session menu, connection approval and spawn allocation.
    /// No account, lobby service, internet relay or game rules are required.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerSession : MonoBehaviour
    {
        private const string ProtocolToken = "EindwerkPlayerMechanics-v1";

        [Header("Scene references")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport transport;
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Camera spectatorCamera;
        [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();

        [Header("LAN settings")]
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;
        [SerializeField, Range(1, 32)] private int maximumPlayers = 8;
        [SerializeField, Min(3f)] private float connectTimeout = 12f;

        private readonly Dictionary<ulong, int> spawnSlots = new Dictionary<ulong, int>();
        private string portText = "7777";
        private string status = "Kies Host om een LAN-sessie te starten of Client om te verbinden.";
        private bool callbacksBound;
        private bool connecting;
        private bool requestedStop;
        private float connectStartedAt;
        private string localAddresses;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;

        public string Status => status;
        public bool IsSessionActive => networkManager != null && networkManager.IsListening;
        public int MaximumPlayers => maximumPlayers;

        public void SetEndpoint(string hostAddress, ushort udpPort)
        {
            if (IsSessionActive || connecting)
                throw new InvalidOperationException("Stop the session before changing its endpoint.");
            address = hostAddress;
            port = udpPort;
            portText = udpPort.ToString();
        }

        public bool IsPointerOverMenu(Vector2 screenPosition)
        {
            if (Application.isBatchMode || Cursor.lockState == CursorLockMode.Locked)
                return false;
            return GetPanelRect().Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        }

        public void Configure(NetworkManager manager, UnityTransport unityTransport,
            GameObject capsulePrefab, Camera overviewCamera, Transform[] points)
        {
            networkManager = manager;
            transport = unityTransport;
            playerPrefab = capsulePrefab;
            spectatorCamera = overviewCamera;
            spawnPoints = points ?? Array.Empty<Transform>();
        }

        private void Start()
        {
            portText = port.ToString();
            localAddresses = GetLocalAddresses();
            if (!BindCallbacks())
                status = "Configuratie ontbreekt: NetworkManager, UnityTransport of spelerprefab.";
            SetSpectatorCamera(true);
            StartFromCommandLine();
        }

        private bool BindCallbacks()
        {
            if (networkManager == null || transport == null || playerPrefab == null)
                return false;
            if (callbacksBound)
                return true;

            networkManager.ConnectionApprovalCallback += ApproveConnection;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            networkManager.OnTransportFailure += OnTransportFailure;
            networkManager.OnClientStopped += OnSessionStopped;
            networkManager.OnServerStopped += OnSessionStopped;
            callbacksBound = true;
            return true;
        }

        public bool StartHost() => StartSession(SessionMode.Host);
        public bool StartClient() => StartSession(SessionMode.Client);
        public bool StartServer() => StartSession(SessionMode.Server);

        private enum SessionMode { Host, Client, Server }

        private bool StartSession(SessionMode mode)
        {
            if (!BindCallbacks())
            {
                status = "Kan niet starten: de netwerkreferenties zijn niet ingesteld.";
                return false;
            }
            if (networkManager.IsListening || networkManager.ShutdownInProgress || connecting)
                return false;
            if (!ushort.TryParse(portText, out ushort parsedPort) || parsedPort == 0)
            {
                status = "Vul een poort tussen 1 en 65535 in.";
                return false;
            }
            if (mode == SessionMode.Client &&
                (!IPAddress.TryParse(address.Trim(), out IPAddress ip) || ip.AddressFamily != AddressFamily.InterNetwork))
            {
                status = "Vul het IPv4-adres van de host in, bijvoorbeeld 192.168.1.10.";
                return false;
            }

            port = parsedPort;
            address = address.Trim();
            requestedStop = false;
            connecting = mode == SessionMode.Client;
            connectStartedAt = Time.unscaledTime;
            spawnSlots.Clear();
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(ProtocolToken);
            networkManager.NetworkConfig.ProtocolVersion = 1;
            // Every instance explicitly opens Map before connecting; no cross-scene game flow.
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.NetworkConfig.TickRate = 30;
            transport.SetConnectionData(mode == SessionMode.Client ? address : "127.0.0.1", port, "0.0.0.0");

            try
            {
                bool started = mode == SessionMode.Host ? networkManager.StartHost()
                    : mode == SessionMode.Server ? networkManager.StartServer()
                    : networkManager.StartClient();
                if (!started)
                {
                    connecting = false;
                    status = "Starten mislukt. Controleer of de poort al in gebruik is.";
                    networkManager.Shutdown();
                    return false;
                }

                status = mode == SessionMode.Host ? $"Host actief op UDP-poort {port}."
                    : mode == SessionMode.Server ? $"Server actief op UDP-poort {port}, zonder lokale speler."
                    : $"Verbinden met {address}:{port}...";
                return true;
            }
            catch (Exception exception)
            {
                connecting = false;
                status = $"Starten mislukt: {exception.Message}";
                networkManager.Shutdown();
                Debug.LogException(exception, this);
                return false;
            }
        }

        public void StopSession()
        {
            requestedStop = true;
            connecting = false;
            status = "Sessie gestopt. Je kunt opnieuw verbinden of hosten.";
            if (networkManager != null)
                networkManager.Shutdown();
            spawnSlots.Clear();
            SetSpectatorCamera(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (networkManager == null)
                return;
            bool hasLocalPlayer = networkManager.IsClient && networkManager.IsConnectedClient
                && networkManager.LocalClient != null && networkManager.LocalClient.PlayerObject != null
                && networkManager.LocalClient.PlayerObject.IsSpawned;
            SetSpectatorCamera(!hasLocalPlayer);

            if (connecting && Time.unscaledTime - connectStartedAt > connectTimeout)
            {
                connecting = false;
                status = $"Geen verbinding binnen {connectTimeout:0} seconden. Controleer host, IPv4-adres, poort en firewall.";
                networkManager.Shutdown();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = false;
            response.CreatePlayerObject = false;
            if (request.Payload == null || Encoding.UTF8.GetString(request.Payload) != ProtocolToken)
            {
                response.Approved = false;
                response.Reason = "Andere versie van de player mechanics. Gebruik hetzelfde project en dezelfde build.";
                return;
            }

            if (!spawnSlots.TryGetValue(request.ClientNetworkId, out int slot))
            {
                slot = FindFreeSpawnSlot();
                if (slot < 0)
                {
                    response.Approved = false;
                    response.Reason = $"De sessie is vol. Er zijn maximaal {maximumPlayers} spelers.";
                    return;
                }
                spawnSlots.Add(request.ClientNetworkId, slot);
            }

            GetSpawnPose(slot, out Vector3 position, out Quaternion rotation);
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = position;
            response.Rotation = rotation;
        }

        private int FindFreeSpawnSlot()
        {
            for (int i = 0; i < maximumPlayers; i++)
            {
                if (!spawnSlots.ContainsValue(i))
                    return i;
            }
            return -1;
        }

        private void GetSpawnPose(int slot, out Vector3 position, out Quaternion rotation)
        {
            if (slot < spawnPoints.Length && spawnPoints[slot] != null)
            {
                position = spawnPoints[slot].position;
                rotation = spawnPoints[slot].rotation;
                return;
            }
            position = new Vector3((slot % 4 - 1.5f) * 3f, 0.1f, -4f - slot / 4 * 3f);
            rotation = Quaternion.identity;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId == networkManager.LocalClientId)
            {
                connecting = false;
                status = networkManager.IsHost ? $"Host actief op UDP-poort {port}." : $"Verbonden met {address}:{port}.";
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            spawnSlots.Remove(clientId);
            if (clientId != networkManager.LocalClientId || networkManager.IsServer)
                return;
            connecting = false;
            if (!requestedStop)
            {
                string reason = networkManager.DisconnectReason;
                status = string.IsNullOrWhiteSpace(reason)
                    ? "Verbinding verbroken of host niet bereikbaar. Je kunt opnieuw verbinden."
                    : $"Verbinding geweigerd of verbroken: {reason}";
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnTransportFailure()
        {
            connecting = false;
            status = "Netwerkfout. Controleer of de UDP-poort vrij is en probeer opnieuw.";
        }

        private void OnSessionStopped(bool wasHost)
        {
            connecting = false;
            spawnSlots.Clear();
            SetSpectatorCamera(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetSpectatorCamera(bool active)
        {
            if (spectatorCamera == null)
                return;
            spectatorCamera.enabled = active;
            AudioListener listener = spectatorCamera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = active;
        }

        private void OnDestroy()
        {
            if (!callbacksBound || networkManager == null)
                return;
            networkManager.ConnectionApprovalCallback -= ApproveConnection;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            networkManager.OnTransportFailure -= OnTransportFailure;
            networkManager.OnClientStopped -= OnSessionStopped;
            networkManager.OnServerStopped -= OnSessionStopped;
        }

        private static string GetLocalAddresses()
        {
            try
            {
                var addresses = new List<string>();
                foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        addresses.Add(ip.ToString());
                }
                return addresses.Count > 0 ? string.Join(", ", addresses) : "Geen LAN-adres gevonden";
            }
            catch (SocketException)
            {
                return "LAN-adres niet beschikbaar";
            }
        }

        private void StartFromCommandLine()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string mode = null;
            for (int i = 1; i + 1 < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case "-networkMode": mode = arguments[++i].ToLowerInvariant(); break;
                    case "-networkAddress": address = arguments[++i]; break;
                    case "-networkPort": portText = arguments[++i]; break;
                }
            }
            if (mode == "host") StartHost();
            else if (mode == "client") StartClient();
            else if (mode == "server") StartServer();
        }

        private void OnGUI()
        {
            if (Application.isBatchMode)
                return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13 };
                smallStyle = new GUIStyle(bodyStyle) { fontSize = 11 };
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool active = networkManager != null && (networkManager.IsListening || connecting || networkManager.ShutdownInProgress);
            GUILayout.BeginArea(GetPanelRect(), GUI.skin.box);
            GUILayout.Label("MULTIPLAYER MECHANICS", titleStyle);
            GUILayout.Label(status, bodyStyle);
            if (locked)
            {
                GUILayout.Label("Escape: muis vrijmaken en sessiemenu openen", smallStyle);
                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(7f);
            if (!active)
            {
                GUILayout.Label("Host IPv4-adres", smallStyle);
                address = GUILayout.TextField(address, 64);
                GUILayout.Label("UDP-poort", smallStyle);
                portText = GUILayout.TextField(portText, 5);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host starten", GUILayout.Height(32))) StartHost();
                if (GUILayout.Button("Client verbinden", GUILayout.Height(32))) StartClient();
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Alleen server starten", GUILayout.Height(27))) StartServer();
                GUILayout.Label($"Maximaal {maximumPlayers} spelers. Zelfde pc: 127.0.0.1.", smallStyle);
                GUILayout.Label($"Deze pc op het LAN: {localAddresses}", smallStyle);
                GUILayout.Label("Open Map in elke instantie. Gebruik bij een tweede pc het IPv4-adres van de host en dezelfde poort.", smallStyle);
            }
            else
            {
                int count = networkManager != null && networkManager.IsServer
                    ? networkManager.ConnectedClientsIds.Count
                    : FindObjectsByType<NetworkPlayer>().Length;
                GUILayout.Label($"Spelers: {count} / {maximumPlayers}   |   UDP {port}", bodyStyle);
                GUILayout.Label("Klik buiten dit paneel om de speler weer te besturen. Escape geeft de muis vrij.", smallStyle);
                if (GUILayout.Button(connecting ? "Verbinden annuleren" : "Sessie stoppen", GUILayout.Height(32)))
                    StopSession();
            }
            GUILayout.EndArea();
        }

        private Rect GetPanelRect()
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool active = networkManager != null &&
                (networkManager.IsListening || connecting || networkManager.ShutdownInProgress);
            return new Rect(12f, 12f, Mathf.Max(100f, Mathf.Min(440f, Screen.width - 24f)),
                locked ? 125f : active ? 260f : 390f);
        }
    }
}
