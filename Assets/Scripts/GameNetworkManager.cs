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

    public void JoinParty(string lobbyCode, System.Action onNotFound)
    {
        if (EOSLobbyCode.Instance == null) { onNotFound?.Invoke(); return; }

        EOSLobbyCode.Instance.FindSession(lobbyCode,
            hostId =>
            {
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
        Debug.Log($"[GNM] LeaveAndConnect START server={NetworkServer.active} client={NetworkClient.active} leavingToJoin={IsLeavingToJoin} leavingParty={IsLeavingParty}");

        if (NetworkServer.active || NetworkClient.active)
        {
            IsLeavingToJoin = true;
            StopCurrentSession();

            float t = 8f;
            while ((NetworkServer.active || NetworkClient.active) && t > 0f)
            { t -= Time.deltaTime; yield return null; }

            Debug.Log($"[GNM] LeaveAndConnect after stop: server={NetworkServer.active} client={NetworkClient.active} timedOut={t <= 0f}");
            IsLeavingToJoin = false;
        }

        // Ensure flags are clear before connecting
        IsLeavingParty  = false;
        IsLeavingToJoin = false;
        // Destroy any stale client-side spawned objects from the previous session.
        // If NetworkClient.spawned still has entries (e.g. host LP netId=1), Mirror will
        // skip OnStartClient for them on rejoin, so the card never spawns.
        NetworkClient.DestroyAllClientObjects();
        LobbyPlayer.All.Clear();
        Debug.Log($"[GNM] LeaveAndConnect flags cleared, waiting 1s before StartClient");

        yield return new WaitForSeconds(1f);

        CurrentLobbyCode = lobbyCode;
        onlineScene  = menuSceneName;
        offlineScene = menuSceneName;
        networkAddress = hostId;
        Debug.Log($"[GNM] LeaveAndConnect calling StartClient leavingToJoin={IsLeavingToJoin} leavingParty={IsLeavingParty}");
        StartClient();
    }

    void StopCurrentSession()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
        {
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

        // Log all currently spawned LPs the server knows about
        foreach (var kv in NetworkServer.spawned)
        {
            var lp = kv.Value?.GetComponent<LobbyPlayer>();
            if (lp == null) continue;
            Debug.Log($"[GNM]   SpawnedLP netId={kv.Key} connId={kv.Value.connectionToClient?.connectionId} name='{lp.playerName}' observers={kv.Value.observers?.Count}");
        }

        if (EOSLobbyCode.Instance != null && NetworkServer.active)
            EOSLobbyCode.Instance.UpdateSessionPlayerCount(NetworkServer.connections.Count);
        if (PartyManager.Instance != null && NetworkServer.active)
            PartyManager.Instance.SetMemberCount(NetworkServer.connections.Count);

        if (isLobby)
            StartCoroutine(ResyncLobbyPlayersToConn(conn));
    }

    IEnumerator ResyncLobbyPlayersToConn(NetworkConnectionToClient conn)
    {
        Debug.Log($"[GNM] ResyncLobbyPlayersToConn ENTER conn={conn.connectionId}");
        yield return null; // wait one frame

        if (!NetworkServer.connections.ContainsKey(conn.connectionId))
        {
            Debug.Log($"[GNM] ResyncLobbyPlayersToConn conn={conn.connectionId} GONE before resync — aborting");
            yield break;
        }

        Debug.Log($"[GNM] ResyncLobbyPlayersToConn RESUME conn={conn.connectionId} — scanning spawned objects");
        int processed = 0;
        foreach (var kv in NetworkServer.spawned)
        {
            var lp = kv.Value?.GetComponent<LobbyPlayer>();
            if (lp == null) continue;

            bool isOwnedByNewConn = kv.Value.connectionToClient == conn;
            Debug.Log($"[GNM]   Checking netId={kv.Key} name='{lp.playerName}' ownedByNewConn={isOwnedByNewConn} observers={kv.Value.observers?.Count}");

            if (isOwnedByNewConn) continue;
            if (string.IsNullOrEmpty(lp.playerName))
            {
                Debug.Log($"[GNM]   Skipping netId={kv.Key} — playerName is empty");
                continue;
            }

            bool alreadyObserver = kv.Value.observers != null && kv.Value.observers.ContainsKey(conn.connectionId);
            Debug.Log($"[GNM]   RebuildObservers for netId={kv.Key} name='{lp.playerName}' alreadyObserver={alreadyObserver}");
            NetworkServer.RebuildObservers(kv.Value, true);
            Debug.Log($"[GNM]   observers after rebuild={kv.Value.observers?.Count}");

            string name = lp.playerName;
            Debug.Log($"[GNM]   Toggling playerName '' then '{name}' on netId={kv.Key}");
            lp.playerName = "";
            lp.playerName = name;
            Debug.Log($"[GNM]   Toggle done, playerName='{lp.playerName}'");
            processed++;
        }
        Debug.Log($"[GNM] ResyncLobbyPlayersToConn DONE conn={conn.connectionId} processed={processed}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        Debug.Log($"[GNM] OnServerDisconnect conn={conn.connectionId} identity={conn.identity?.name} identityNull={conn.identity == null}");

        // Reset playerName to "" so SyncVar hook fires fresh on rejoin
        // Skip conn 0 (host's own connection) — host LP persists and never re-calls CmdSetName
        if (conn.connectionId != 0 && conn.identity != null && conn.identity.gameObject != null)
        {
            var lp = conn.identity.GetComponent<LobbyPlayer>();
            if (lp != null)
            {
                Debug.Log($"[GNM]   Resetting playerName from '{lp.playerName}' to '' for conn={conn.connectionId}");
                lp.playerName = "";
            }
            else
            {
                Debug.Log($"[GNM]   No LobbyPlayer on identity for conn={conn.connectionId}");
            }
        }
        else
        {
            Debug.Log($"[GNM]   Skipping playerName reset for conn={conn.connectionId} (host conn or null identity)");
        }

        base.OnServerDisconnect(conn);
        if (!NetworkServer.active) return;

        int remaining = Mathf.Max(0, NetworkServer.connections.Count);
        Debug.Log($"[GNM] OnServerDisconnect conn={conn.connectionId} remaining={remaining}");

        // Log remaining spawned LPs
        foreach (var kv in NetworkServer.spawned)
        {
            var lp = kv.Value?.GetComponent<LobbyPlayer>();
            if (lp == null) continue;
            Debug.Log($"[GNM]   RemainingLP netId={kv.Key} connId={kv.Value.connectionToClient?.connectionId} name='{lp.playerName}'");
        }

        if (EOSLobbyCode.Instance != null)
            EOSLobbyCode.Instance.UpdateSessionPlayerCount(remaining);
        if (PartyManager.Instance != null)
            PartyManager.Instance.SetMemberCount(remaining);
    }

    public override void OnStartHost()   { base.OnStartHost(); IsLeavingParty = false; IsLeavingToJoin = false; Debug.Log("[GNM] OnStartHost"); }
    public override void OnStartServer() { base.OnStartServer(); Debug.Log("[GNM] OnStartServer"); }
    public override void OnStartClient()
    {
        NetworkClient.PrepareToSpawnSceneObjects();
        base.OnStartClient();
        Debug.Log($"[GNM] OnStartClient leavingToJoin={IsLeavingToJoin} leavingParty={IsLeavingParty}");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log($"[GNM] OnClientConnect isServer={NetworkServer.active} leavingToJoin={IsLeavingToJoin} leavingParty={IsLeavingParty}");
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
        Debug.Log($"[GNM] OnStopClient leavingToJoin={IsLeavingToJoin} leavingParty={IsLeavingParty} LobbyPlayer.All.Count={LobbyPlayer.All.Count}");
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
