using Mirror;
using UnityEngine;
using EpicTransport;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameNetworkManager : NetworkManager
{
    [Header("Prefabs")]
    public GameObject lobbyPlayerPrefab;

#if UNITY_EDITOR
    [Header("Scenes")]
    public SceneAsset menuSceneAsset;
#endif
    [HideInInspector] public string menuSceneName;

    public static string LocalPlayerName { get; set; }
    public static string CurrentLobbyCode { get; set; }

    private static bool _sessionDestroyed = false;
    public static bool IsIntentionallyJoining { get; set; } = false;
    public static bool IsHostDisconnecting   { get; set; } = false;

    public override void Awake()
    {
        if (singleton != null && singleton != this) { Destroy(gameObject); return; }
        dontDestroyOnLoad = true;
        base.Awake();
        if (!string.IsNullOrEmpty(menuSceneName))
        {
            onlineScene  = menuSceneName;
            offlineScene = menuSceneName;
        }

        if (lobbyPlayerPrefab != null && !spawnPrefabs.Contains(lobbyPlayerPrefab))
            spawnPrefabs.Add(lobbyPlayerPrefab);

        // Mirror's base checks playerPrefab != null before calling our override
        if (playerPrefab == null && lobbyPlayerPrefab != null)
            playerPrefab = lobbyPlayerPrefab;

        if (string.IsNullOrEmpty(LocalPlayerName))
            LocalPlayerName = PlayerPrefs.GetString("PlayerName", $"Player{Random.Range(1000,9999)}");

        if (EOSLobbyCode.Instance == null)
            new GameObject("EOSLobbyCode").AddComponent<EOSLobbyCode>();
    }

    // ── Party Host ───────────────────────────────────────────────────
    public void StartPartyHost(System.Action<string> onCodeReady)
    {
        if (EOSLobbyCode.Instance == null) { onCodeReady?.Invoke(null); return; }

        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.Log("[GNM] StartPartyHost: already running");
            onCodeReady?.Invoke(CurrentLobbyCode);
            return;
        }

        if (!string.IsNullOrEmpty(CurrentLobbyCode))
        {
            StartHostOnMainMenu();
            onCodeReady?.Invoke(CurrentLobbyCode);
            return;
        }

        string code = EOSLobbyCode.GenerateCode();
        EOSLobbyCode.Instance.CreateSession(code, readyCode =>
        {
            CurrentLobbyCode = readyCode;
            _sessionDestroyed = false;
            StartHostOnMainMenu();
            Debug.Log($"[GNM] Party hosted, code={readyCode}");
            onCodeReady?.Invoke(readyCode);
        }, () =>
        {
            Debug.LogError("[GNM] CreateSession failed");
            onCodeReady?.Invoke(null);
        });
    }

    void StartHostOnMainMenu()
    {
        onlineScene    = menuSceneName;
        offlineScene   = menuSceneName;
        maxConnections = 5;
        _sessionDestroyed = false;
        StartHost();
    }

    // ── Join Party ───────────────────────────────────────────────────
    public void JoinParty(string lobbyCode, System.Action onFailed)
    {
        if (EOSLobbyCode.Instance == null) { onFailed?.Invoke(); return; }
        onlineScene      = menuSceneName;
        offlineScene     = menuSceneName;
        CurrentLobbyCode = string.Empty;

        EOSLobbyCode.Instance.FindSession(lobbyCode,
            hostId => { networkAddress = hostId; StartClient(); },
            () => { Debug.LogError($"[GNM] Party not found: {lobbyCode}"); onFailed?.Invoke(); });
    }

    // ── Start Game ───────────────────────────────────────────────────
    public void StartGame(string sceneName)
    {
        if (!NetworkServer.active) { Debug.LogError("[GNM] StartGame: not server"); return; }
        ServerChangeScene(sceneName);
    }

    // ── Leave ────────────────────────────────────────────────────────
    public void LeaveParty()
    {
        Debug.Log($"[GNM] LeaveParty server={NetworkServer.active} client={NetworkClient.isConnected}");
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            if (NetworkServer.connections.Count > 1)
                PartyManager.Instance?.NotifyHostDisconnected();
            StartCoroutine(ShutdownAfterRpc());
        }
        else if (NetworkClient.isConnected)
        {
            IsIntentionallyJoining = true;
            StopClient();
        }
        else if (NetworkServer.active) StopServer();
    }

    IEnumerator ShutdownAfterRpc()
    {
        yield return new WaitForSeconds(0.3f);
        StopHost();
    }

    void DestroySessionIfEmpty()
    {
        if (_sessionDestroyed || string.IsNullOrEmpty(CurrentLobbyCode)) return;
        _sessionDestroyed = true;
        CurrentLobbyCode  = string.Empty;
        EOSLobbyCode.Instance?.DestroySession();
        Debug.Log("[GNM] Session destroyed — party empty");
    }

    // ── Mirror Callbacks ─────────────────────────────────────────────

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        bool isLobby = string.IsNullOrEmpty(networkSceneName) || networkSceneName == menuSceneName;

        if (isLobby)
        {
            if (lobbyPlayerPrefab == null) { Debug.LogError("[GNM] lobbyPlayerPrefab not assigned!"); return; }
            NetworkServer.AddPlayerForConnection(conn, Instantiate(lobbyPlayerPrefab));
        }
        else
        {
            if (playerPrefab == null) { Debug.LogError("[GNM] playerPrefab not assigned!"); return; }
            var pos = GetStartPosition()?.position ?? Vector3.zero;
            NetworkServer.AddPlayerForConnection(conn, Instantiate(playerPrefab, pos, Quaternion.identity));
        }
        Debug.Log($"[GNM] OnServerAddPlayer conn={conn.connectionId} isLobby={isLobby}");
        UpdateSessionPlayerCount(NetworkServer.connections.Count);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        int remaining = NetworkServer.connections.Count - 1;

        base.OnServerDisconnect(conn);
        if (!NetworkServer.active) return;

        Debug.Log($"[GNM] OnServerDisconnect conn={conn.connectionId} remaining={remaining}");

        if (remaining == 0)
        {
            UpdateSessionPlayerCount(0);
            DestroySessionIfEmpty();
            return;
        }

        UpdateSessionPlayerCount(remaining);
    }

    void UpdateSessionPlayerCount(int playerCount)
    {
        if (!NetworkServer.active || EOSLobbyCode.Instance == null) return;
        EOSLobbyCode.Instance.UpdateSessionPlayerCount(playerCount);
    }

    public override void OnStartHost()   { base.OnStartHost();   Debug.Log("[GNM] OnStartHost"); }
    public override void OnStartServer() { base.OnStartServer(); Debug.Log("[GNM] OnStartServer"); }
    public override void OnStartClient() { base.OnStartClient(); Debug.Log("[GNM] OnStartClient"); }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[GNM] OnClientConnect");
        IsIntentionallyJoining = false; // successfully connected, clear flag
        if (!NetworkServer.active)
            MenuController.Instance?.OnJoinedParty();
    }

    public override void OnStopHost()
    {
        base.OnStopHost();
        Debug.Log("[GNM] OnStopHost");
        LobbyPlayer.All.Clear();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log("[GNM] OnStopClient");
        LobbyPlayer.All.Clear();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log("[GNM] OnStopServer");
        // If server stopped with no remaining connections, the party is empty — destroy session
        DestroySessionIfEmpty();
        if (PartyManager.Instance != null)
        {
            PartyManager.Instance.memberNames.Clear();
            PartyManager.Instance.selectedSceneName = "";
            PartyManager.Instance.selectedModeDisplay = "";
        }
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log($"[GNM] OnClientDisconnect intentionalJoin={IsIntentionallyJoining} hostDisconnecting={IsHostDisconnecting}");
        LobbyPlayer.All.Clear();
        bool wasIntentional = IsIntentionallyJoining;
        bool wasHostDisconnect = IsHostDisconnecting;
        IsIntentionallyJoining = false;
        IsHostDisconnecting    = false;
        if (!wasIntentional && !wasHostDisconnect)
            MenuController.Instance?.OnDisconnectedFromParty();
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        Debug.Log($"[GNM] ServerSceneChanged: {sceneName}");
    }

    public override void OnClientChangeScene(string newSceneName, SceneOperation op, bool customHandling)
    {
        base.OnClientChangeScene(newSceneName, op, customHandling);
        Debug.Log($"[GNM] ClientChangeScene: {newSceneName}");
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(GameNetworkManager))]
public class GameNetworkManagerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        UnityEditor.SerializedProperty prop = serializedObject.GetIterator();
        prop.NextVisible(true);
        while (prop.NextVisible(false))
            UnityEditor.EditorGUILayout.PropertyField(prop, true);
        serializedObject.ApplyModifiedProperties();

        var gnm = (GameNetworkManager)target;
        UnityEditor.EditorGUI.BeginChangeCheck();
        gnm.menuSceneAsset = (UnityEditor.SceneAsset)UnityEditor.EditorGUILayout.ObjectField(
            "Menu Scene", gnm.menuSceneAsset, typeof(UnityEditor.SceneAsset), false);
        if (UnityEditor.EditorGUI.EndChangeCheck() && gnm.menuSceneAsset != null)
        {
            gnm.menuSceneName = gnm.menuSceneAsset.name;
            UnityEditor.EditorUtility.SetDirty(gnm);
        }
        UnityEditor.EditorGUILayout.LabelField("Menu Scene Name", gnm.menuSceneName);
    }
}
#endif
