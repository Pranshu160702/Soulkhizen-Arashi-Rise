using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class PartyManager : NetworkBehaviour
{
    public static PartyManager Instance { get; private set; }

    public readonly SyncList<string> memberNames = new SyncList<string>();

    [SyncVar(hook = nameof(OnSceneNameChanged))]
    public string selectedSceneName = "";

    [SyncVar(hook = nameof(OnModeDisplayChanged))]
    public string selectedModeDisplay = "";

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
        memberNames.Callback += OnMemberListChanged;
        MenuController.Instance?.RefreshParty();
        if (!string.IsNullOrEmpty(selectedSceneName) && !string.IsNullOrEmpty(selectedModeDisplay))
            MenuController.Instance?.OnGameModeChanged(selectedSceneName, selectedModeDisplay);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        memberNames.Callback -= OnMemberListChanged;
    }

    void OnMemberListChanged(SyncList<string>.Operation op, int index, string oldItem, string newItem)
    {
        Debug.Log($"[PartyManager] memberNames changed op={op} new='{newItem}'");
        MenuController.Instance?.RefreshParty();
    }

    void OnSceneNameChanged(string _, string newVal) { }
    void OnModeDisplayChanged(string _, string newDisplay)
    {
        if (!string.IsNullOrEmpty(selectedSceneName) && !string.IsNullOrEmpty(newDisplay))
            MenuController.Instance?.OnGameModeChanged(selectedSceneName, newDisplay);
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
        {
            GameNetworkManager.IsHostDisconnecting = true;
            MenuController.Instance?.OnHostDisconnected();
        }
    }

    [Server]
    public void AddMember(string name)
    {
        if (string.IsNullOrEmpty(name) || memberNames.Contains(name)) return;
        memberNames.Add(name);
        Debug.Log($"[PartyManager] AddMember '{name}' total={memberNames.Count}");
    }

    [Server]
    public void RemoveMember(string name)
    {
        if (!memberNames.Contains(name)) return;
        memberNames.Remove(name);
        Debug.Log($"[PartyManager] RemoveMember '{name}' total={memberNames.Count}");
    }
}
