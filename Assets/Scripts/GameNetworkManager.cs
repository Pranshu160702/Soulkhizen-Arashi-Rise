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

    public static string LocalPlayerName  { get; set; }
    public static string CurrentLobbyCode { get; set; }

    // True while we are intentionally stopping to join someone else
    public static bool IsLeavingToJoin  { get; set; } = false;
    // True while a guest is intentionally leaving a party
    public static bool IsLeavingParty   { get; set; } = false;

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

        if (playerPrefab == null && lobbyPlayerPrefab != null)
            playerPrefab = lobbyPlayerPrefab;

        if (string.IsNullOrEmpty(LocalPlayerName))
            LocalPlayerName = PlayerPrefs.GetString("PlayerName", $"Player{Random.Range(1000, 9999)}");

        if (EOSLobbyCode.Instance == null)
            new GameObject("EOSLobbyCode").AddComponent<EOSLobbyCode>();
    }

    // ── Host ─────────────────────────────────────────────────────────

    // Creates an EOS session and starts a Mirror host. Only called when the
    // player explicitly wants to be joinable (i.e. they share their code).
    public void StartPartyHost(System.Action<string> onCodeReady)
    {
        if (EOSLobbyCode.Instance == null) { onCodeReady?.Invoke(null); return; }

        if (NetworkServer.active)
        {
            onCodeReady?.Invoke(CurrentLobbyCode);
            return;
        }

        string code = EOSLobbyCode.GenerateCode();
        EOSLobbyCode.Instance.CreateSession(code, readyCode =>
        {
            CurrentLobbyCode = readyCode;
            onlineScene  = menuSceneName;
            offlineScene = menuSceneName;
            maxConnections = 5;
            StartHost();
            Debug.Log($"[GNM] Party hosted, code={readyCode}");
            onCodeReady?.Invoke(readyCode);
        }, () =>
        {
            Debug.LogError("[GNM] CreateSession failed");
            onCodeReady?.Invoke(null);
        });
    }

    // ── Join ──────────────────────────────────────────────────────────

    // Searches EOS for the code. If found, stops any existing host/client
    // then connects. onNotFound fires if the code doesn't exist in EOS.
    public void JoinParty(string lobbyCode, System.Action onNotFound)
    {
        if (EOSLobbyCode.Instance == null) { onNotFound?.Invoke(); return; }

        EOSLobbyCode.Instance.FindSession(lobbyCode,
            hostId =>
            {
                // Found — now leave our own party if we have one, then connect
                StartCoroutine(LeaveAndConnect(hostId, lobbyCode));
            },
            () =>
            {
                Debug.Log($"[GNM] Party not found: {lobbyCode}");
                onNotFound?.Invoke();
            });
    }

    IEnumerator LeaveAndConnect(string hostId, string lobbyCode)
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            IsLeavingToJoin = true;
            StopCurrentSession();

            float t = 8f; // enough for the 0.3s RPC delay + shutdown
            while ((NetworkServer.active || NetworkClient.active) && t > 0f)
            { t -= Time.deltaTime; yield return null; }

            IsLeavingToJoin = false;
        }

        yield return null;
        yield return new WaitForSeconds(1f);

        CurrentLobbyCode = lobbyCode;
        onlineScene  = menuSceneName;
        offlineScene = menuSceneName;
        networkAddress = hostId;
        StartClient();
    }

    void StopCurrentSession()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            // Notify guests before pulling the rug
            if (NetworkServer.connections.Count > 1)
                PartyManager.Instance?.NotifyHostDisconnected();
            StartCoroutine(ShutdownCurrentThenContinue());
        }
        else if (NetworkClient.isConnected)
        {
            StopClient();
        }
        else if (NetworkServer.active)
        {
            EOSLobbyCode.Instance?.DestroySession();
            CurrentLobbyCode = string.Empty;
            StopServer();
        }
    }

    // Used by LeaveAndConnect — gives the RPC a frame to reach clients before stopping
    IEnumerator ShutdownCurrentThenContinue()
    {
        yield return new WaitForSeconds(0.3f);
        EOSLobbyCode.Instance?.DestroySession();
        CurrentLobbyCode = string.Empty;
        StopHost();
    }

    // ── Start Game ────────────────────────────────────────────────────

    public void StartGame(string sceneName)
    {
        if (!NetworkServer.active) { Debug.LogError("[GNM] StartGame: not server"); return; }
        ServerChangeScene(sceneName);
    }

    // ── Leave Party ───────────────────────────────────────────────────

    public void LeaveParty()
    {
        Debug.Log($"[GNM] LeaveParty server={NetworkServer.active} client={NetworkClient.isConnected}");

        if (NetworkServer.active && NetworkClient.isConnected)
        {
            // We are the host — notify guests then shut down
            if (NetworkServer.connections.Count > 1)
                PartyManager.Instance?.NotifyHostDisconnected();
            StartCoroutine(ShutdownAfterRpc());
        }
        else if (NetworkClient.isConnected)
        {
            IsLeavingParty = true;
            StopClient();
        }
        else if (NetworkServer.active)
        {
            EOSLobbyCode.Instance?.DestroySession();
            CurrentLobbyCode = string.Empty;
            StopServer();
        }
    }

    IEnumerator ShutdownAfterRpc()
    {
        IsLeavingParty = true;
        yield return new WaitForSeconds(0.3f);
        EOSLobbyCode.Instance?.DestroySession();
        CurrentLobbyCode = string.Empty;
        StopHost();
    }

    // ── Mirror Callbacks ──────────────────────────────────────────────

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
        if (EOSLobbyCode.Instance != null && NetworkServer.active)
            EOSLobbyCode.Instance.UpdateSessionPlayerCount(NetworkServer.connections.Count);
        if (PartyManager.Instance != null && NetworkServer.active)
            PartyManager.Instance.SetMemberCount(NetworkServer.connections.Count);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // Reset playerName to "" so SyncVar fires fresh on rejoin
        // Guard against destroyed identity (e.g. during StopHost)
        if (conn.identity != null && conn.identity.gameObject != null)
        {
            var lp = conn.identity.GetComponent<LobbyPlayer>();
            if (lp != null) lp.playerName = "";
        }

        base.OnServerDisconnect(conn);
        if (!NetworkServer.active) return;

        int remaining = Mathf.Max(0, NetworkServer.connections.Count);
        Debug.Log($"[GNM] OnServerDisconnect conn={conn.connectionId} remaining={remaining}");
        if (EOSLobbyCode.Instance != null)
            EOSLobbyCode.Instance.UpdateSessionPlayerCount(remaining);
        if (PartyManager.Instance != null)
            PartyManager.Instance.SetMemberCount(remaining);
    }

    public override void OnStartHost()   { base.OnStartHost();   Debug.Log("[GNM] OnStartHost"); }
    public override void OnStartServer() { base.OnStartServer(); Debug.Log("[GNM] OnStartServer"); }
    public override void OnStartClient()
    {
        // Re-register scene NetworkIdentity objects so Mirror can find them
        // on first join and on every rejoin (StopClient clears the registry)
        NetworkClient.PrepareToSpawnSceneObjects();
        base.OnStartClient();
        Debug.Log("[GNM] OnStartClient");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[GNM] OnClientConnect");
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
        // Destroy cards for all tracked remote players before clearing the list
        foreach (var lp in LobbyPlayer.All)
            if (!lp.isOwned && !string.IsNullOrEmpty(lp.playerName))
                MenuController.Instance?.DestroyCardForPlayer(lp.playerName);
        LobbyPlayer.All.Clear();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log("[GNM] OnStopServer");
        if (PartyManager.Instance != null)
        {
            PartyManager.Instance.selectedSceneName  = "";
            PartyManager.Instance.selectedModeDisplay = "";
            PartyManager.Instance.memberCount = 0;
        }
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log($"[GNM] OnClientDisconnect leavingToJoin={IsLeavingToJoin} leavingParty={IsLeavingParty}");
        LobbyPlayer.All.Clear();

        bool wasIntentional = IsLeavingToJoin || IsLeavingParty;
        IsLeavingToJoin = false;
        IsLeavingParty  = false;

        if (!wasIntentional)
            MenuController.Instance?.OnLostConnection();
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
        var prop = serializedObject.GetIterator();
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
