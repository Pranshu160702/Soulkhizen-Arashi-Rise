using Mirror;
using UnityEngine;

public class LobbyPlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))]  public string playerName;
    [SyncVar(hook = nameof(OnRoomCodeChanged))] public string roomCode;

    public static System.Collections.Generic.List<LobbyPlayer> All = new();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All = new();

    // ── Server callbacks ──────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        roomCode = GameNetworkManager.CurrentLobbyCode;
    }

    // ── Client callbacks ──────────────────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!All.Contains(this)) All.Add(this);

        if (!string.IsNullOrEmpty(roomCode))
            GameNetworkManager.CurrentLobbyCode = roomCode;
    }

    public override void OnStartLocalPlayer()
    {
        CmdSetName(GameNetworkManager.LocalPlayerName);
        MenuController.Instance?.RefreshStartButton();
        MenuController.Instance?.RefreshLeaveButton();
    }

    public override void OnStopClient()
    {
        bool wasTracked = All.Remove(this);
        if (wasTracked && !isOwned && !string.IsNullOrEmpty(playerName))
            MenuController.Instance?.DestroyCardForPlayer(playerName);
    }

    void OnDestroy() => All.Remove(this);

    // ── SyncVar hooks ─────────────────────────────────────────────────

    void OnNameChanged(string oldName, string newName)
    {
        if (isOwned) return;

        if (!string.IsNullOrEmpty(oldName))
            MenuController.Instance?.DestroyCardForPlayer(oldName);
        if (!string.IsNullOrEmpty(newName))
            MenuController.Instance?.SpawnCardForPlayer(newName);
    }

    void OnRoomCodeChanged(string _, string newCode)
    {
        if (!string.IsNullOrEmpty(newCode))
            GameNetworkManager.CurrentLobbyCode = newCode;
    }

    [Command]
    void CmdSetName(string name) => playerName = name;
}
