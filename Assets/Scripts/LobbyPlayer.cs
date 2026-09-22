using Mirror;
using UnityEngine;

public class LobbyPlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))] public string playerName;
    [SyncVar(hook = nameof(OnRoomCodeChanged))] public string roomCode;
    [SyncVar(hook = nameof(OnHostChanged))] public bool isHost;

    public static System.Collections.Generic.List<LobbyPlayer> All = new();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All = new();

    // ── Server-side: name synced, update PartyManager ────────────────
    void OnNameChanged(string oldName, string newName)
    {
        Debug.Log($"[LobbyPlayer] OnNameChanged '{oldName}' -> '{newName}' isServer={isServer}");

        if (isServer && !string.IsNullOrEmpty(newName))
        {
            if (!string.IsNullOrEmpty(oldName))
                PartyManager.Instance?.RemoveMember(oldName);
            PartyManager.Instance?.AddMember(newName);
        }

        // Both server and client refresh the visual party
        MenuController.Instance?.RefreshParty();
    }

    void OnRoomCodeChanged(string _, string newCode)
    {
        if (!string.IsNullOrEmpty(newCode))
        {
            GameNetworkManager.CurrentLobbyCode = newCode;
            MenuController.Instance?.RefreshParty();
        }
    }

    void OnHostChanged(bool _, bool newVal)
    {
        MenuController.Instance?.RefreshStartButton();
    }

    // ── Mirror callbacks ─────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        roomCode = GameNetworkManager.CurrentLobbyCode;
        isHost   = connectionToClient != null && connectionToClient.connectionId == 0;
    }

    public override void OnStartClient()
    {
        if (!All.Contains(this)) All.Add(this);
        Debug.Log($"[LobbyPlayer] OnStartClient name='{playerName}'");
        // Sync the party code to this client from the host's LobbyPlayer
        if (!string.IsNullOrEmpty(roomCode))
            GameNetworkManager.CurrentLobbyCode = roomCode;
        if (!string.IsNullOrEmpty(playerName))
            MenuController.Instance?.RefreshParty();
    }

    public override void OnStartLocalPlayer()
    {
        Debug.Log($"[LobbyPlayer] OnStartLocalPlayer — sending name: {GameNetworkManager.LocalPlayerName}");
        CmdSetName(GameNetworkManager.LocalPlayerName);
        MenuController.Instance?.RefreshStartButton();
        MenuController.Instance?.RefreshLeaveButton();
    }

    public override void OnStopClient()
    {
        Debug.Log($"[LobbyPlayer] OnStopClient name='{playerName}'");
        All.Remove(this);
        MenuController.Instance?.RefreshParty();
    }

    public override void OnStopServer()
    {
        if (!string.IsNullOrEmpty(playerName))
            PartyManager.Instance?.RemoveMember(playerName);
    }

    void OnDestroy() => All.Remove(this);

    [ClientRpc]
    public void RpcBecomeHost()
    {
        Debug.Log($"[LobbyPlayer] {playerName} is now the host");
        MenuController.Instance?.RefreshStartButton();
    }

    [Command]
    void CmdSetName(string name) => playerName = name;
}
