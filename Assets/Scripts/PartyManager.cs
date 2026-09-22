using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// Must be on a GameObject with NetworkIdentity in the MainMenu scene.
/// Tracks party members and leader, synced to all clients.
/// </summary>
public class PartyManager : NetworkBehaviour
{
    public static PartyManager Instance { get; private set; }

    public readonly SyncList<string> memberNames = new SyncList<string>();

    [SyncVar(hook = nameof(OnLeaderChanged))]
    public string leaderName = "";

    public bool IsLocalLeader => leaderName == GameNetworkManager.LocalPlayerName;

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
        // Subscribe to SyncList changes so clients refresh cards when list changes
        memberNames.Callback += OnMemberListChanged;
        // Initial refresh in case list already has data
        MenuController.Instance?.RefreshParty();
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

    void OnLeaderChanged(string _, string newLeader)
    {
        Debug.Log($"[PartyManager] Leader changed to '{newLeader}'");
        MenuController.Instance?.RefreshParty();
    }

    [Server]
    public void AddMember(string name)
    {
        if (string.IsNullOrEmpty(name) || memberNames.Contains(name)) return;
        memberNames.Add(name);
        if (memberNames.Count == 1) leaderName = name;
        Debug.Log($"[PartyManager] AddMember '{name}' total={memberNames.Count}");
    }

    [Server]
    public void RemoveMember(string name)
    {
        if (!memberNames.Contains(name)) return;
        memberNames.Remove(name);
        if (leaderName == name && memberNames.Count > 0)
            leaderName = memberNames[0];
        Debug.Log($"[PartyManager] RemoveMember '{name}' total={memberNames.Count}");
    }
}
