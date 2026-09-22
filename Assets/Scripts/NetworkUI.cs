using UnityEngine;
using Mirror;

public class NetworkUI : MonoBehaviour
{
    private string joinCode = "";
    private string displayCode = "";

    void OnGUI()
    {
        if (NetworkServer.active || NetworkClient.isConnected)
        {
            GUILayout.Label(NetworkServer.active ? $"Hosting - Code: {displayCode}" : "Connected");
            if (GUILayout.Button("Disconnect")) FindAnyObjectByType<GameNetworkManager>().LeaveParty();
            return;
        }

        if (GUILayout.Button("Host"))
            FindAnyObjectByType<GameNetworkManager>().StartPartyHost(code => displayCode = code);

        GUILayout.Label("Lobby Code:");
        joinCode = GUILayout.TextField(joinCode, GUILayout.Width(200));
        if (GUILayout.Button("Join"))
            FindAnyObjectByType<GameNetworkManager>().JoinParty(joinCode, () => Debug.LogError("Lobby not found"));
    }
}
