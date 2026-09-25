using Mirror;
using UnityEngine;

public class LobbyPlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))]     public string playerName;
    [SyncVar(hook = nameof(OnRoomCodeChanged))] public string roomCode;

    public static System.Collections.Generic.List<LobbyPlayer> All = new();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All = new();

    // ── Server callbacks ──────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        roomCode = GameNetworkManager.CurrentLobbyCode;
        Debug.Log($"[LP] OnStartServer netId={netId} connId={connectionToClient?.connectionId} name='{playerName}' roomCode='{roomCode}'");
    }

    // ── Client callbacks ──────────────────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!All.Contains(this)) All.Add(this);

        if (!string.IsNullOrEmpty(roomCode))
            GameNetworkManager.CurrentLobbyCode = roomCode;

        Debug.Log($"[LP] OnStartClient netId={netId} isOwned={isOwned} name='{playerName}' roomCode='{roomCode}' All.Count={All.Count}");

        // Hook only fires on value change — if playerName already set on spawn, spawn card manually
        if (!isOwned && !string.IsNullOrEmpty(playerName))
        {
            Debug.Log($"[LP] OnStartClient — name already set, calling SpawnCardForPlayer('{playerName}') directly");
            MenuController.Instance?.SpawnCardForPlayer(playerName);
        }
        else if (!isOwned && string.IsNullOrEmpty(playerName))
        {
            Debug.Log($"[LP] OnStartClient — name is EMPTY for non-owned LP netId={netId}, card will depend on OnNameChanged hook firing later");
        }
    }

    public override void OnStartLocalPlayer()
    {
        Debug.Log($"[LP] OnStartLocalPlayer netId={netId} sending CmdSetName='{GameNetworkManager.LocalPlayerName}'");
        CmdSetName(GameNetworkManager.LocalPlayerName);
        MenuController.Instance?.RefreshStartButton();
        MenuController.Instance?.RefreshLeaveButton();
    }

    public override void OnStopClient()
    {
        Debug.Log($"[LP] OnStopClient netId={netId} isOwned={isOwned} name='{playerName}'");
        bool wasTracked = All.Remove(this);
        if (wasTracked && !isOwned && !string.IsNullOrEmpty(playerName))
            MenuController.Instance?.DestroyCardForPlayer(playerName);
    }

    void OnDestroy()
    {
        Debug.Log($"[LP] OnDestroy netId={netId} name='{playerName}'");
        All.Remove(this);
    }

    // ── SyncVar hooks ─────────────────────────────────────────────────

    void OnNameChanged(string oldName, string newName)
    {
        Debug.Log($"[LP] OnNameChanged netId={netId} isOwned={isOwned} '{oldName}'->'{newName}'");
        if (isOwned) return;

        if (!string.IsNullOrEmpty(oldName))
        {
            Debug.Log($"[LP] OnNameChanged — destroying card for old name '{oldName}'");
            MenuController.Instance?.DestroyCardForPlayer(oldName);
        }
        if (!string.IsNullOrEmpty(newName))
        {
            Debug.Log($"[LP] OnNameChanged — spawning card for new name '{newName}'");
            MenuController.Instance?.SpawnCardForPlayer(newName);
        }
        else
        {
            Debug.Log($"[LP] OnNameChanged — newName is empty, no card spawned");
        }
    }

    void OnRoomCodeChanged(string _, string newCode)
    {
        Debug.Log($"[LP] OnRoomCodeChanged netId={netId} newCode='{newCode}'");
        if (!string.IsNullOrEmpty(newCode))
            GameNetworkManager.CurrentLobbyCode = newCode;
    }

    [Command]
    void CmdSetName(string name)
    {
        Debug.Log($"[LP] CmdSetName netId={netId} connId={connectionToClient?.connectionId} name='{name}'");
        playerName = name;
    }
}
