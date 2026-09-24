using UnityEngine;
using Mirror;

public class PartyManager : NetworkBehaviour
{
    public static PartyManager Instance { get; private set; }

    [SyncVar(hook = nameof(OnSceneNameChanged))]
    public string selectedSceneName = "";

    [SyncVar(hook = nameof(OnModeDisplayChanged))]
    public string selectedModeDisplay = "";

    [SyncVar(hook = nameof(OnMemberCountChanged))]
    public int memberCount = 0;

    public const int MaxMembers = 5;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!string.IsNullOrEmpty(selectedSceneName) && !string.IsNullOrEmpty(selectedModeDisplay))
            MenuController.Instance?.OnGameModeChanged(selectedSceneName, selectedModeDisplay);
        MenuController.Instance?.RefreshSlotsRemaining(memberCount);
    }

    void OnSceneNameChanged(string _, string newVal) { }

    void OnModeDisplayChanged(string _, string newDisplay)
    {
        if (!string.IsNullOrEmpty(selectedSceneName) && !string.IsNullOrEmpty(newDisplay))
            MenuController.Instance?.OnGameModeChanged(selectedSceneName, newDisplay);
    }

    void OnMemberCountChanged(int _, int newCount)
    {
        MenuController.Instance?.RefreshSlotsRemaining(newCount);
    }

    [Server]
    public void SetMemberCount(int count)
    {
        memberCount = Mathf.Clamp(count, 0, MaxMembers);
    }

    [Server]
    public void SetGameMode(string sceneName, string displayName)
    {
        selectedSceneName   = sceneName;
        selectedModeDisplay = displayName;
    }

    [Server]
    public void NotifyHostDisconnected() => RpcHostDisconnected();

    [ClientRpc]
    void RpcHostDisconnected()
    {
        if (!NetworkServer.active)
            MenuController.Instance?.OnHostDisconnected();
    }
}
