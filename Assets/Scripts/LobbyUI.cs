using Mirror;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    [Header("Header")]
    public TextMeshProUGUI roomCodeText;
    public TextMeshProUGUI playerCountText;

    [Header("Player List")]
    public Transform playerListContainer;
    public GameObject playerNameEntryPrefab;

    [Header("Footer")]
    public Button startButton;
    public Button leaveButton;
    public TextMeshProUGUI statusText;

    private Coroutine _dotsCoroutine;
    private string _statusBase;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        // Hide loading overlay that was shown while connecting
        LoadingOverlay.Hide();

        bool isHost = NetworkServer.active;

        if (roomCodeText != null)
            roomCodeText.text = !string.IsNullOrEmpty(GameNetworkManager.CurrentLobbyCode)
                ? GameNetworkManager.CurrentLobbyCode
                : "------";

        // Start button: hidden by default for non-hosts, shown via isHost SyncVar hook
        if (startButton != null)
        {
            startButton.gameObject.SetActive(NetworkServer.active);
            startButton.onClick.AddListener(OnStartClicked);
        }

        leaveButton?.onClick.AddListener(OnLeaveClicked);

        SetStatus(isHost ? "Waiting for players" : "Waiting for host to start");
        RefreshPlayerList();
    }

    public void SetStatus(string text)
    {
        if (statusText == null) return;
        if (_dotsCoroutine != null) { StopCoroutine(_dotsCoroutine); _dotsCoroutine = null; }
        _statusBase = text;
        statusText.text = text;
        _dotsCoroutine = StartCoroutine(AnimateDots());
    }

    public void UpdateRoomCode(string code)
    {
        if (!string.IsNullOrEmpty(code) && roomCodeText != null)
            roomCodeText.text = code;
    }

    public void RefreshPlayerList()
    {
        // Guard against calls during scene teardown
        if (this == null || !gameObject.activeInHierarchy) return;
        if (playerListContainer == null || playerNameEntryPrefab == null) return;

        // Safe destruction
        var children = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in playerListContainer) children.Add(child.gameObject);
        foreach (var child in children) Destroy(child);

        int index = 0;
        foreach (var player in LobbyPlayer.All)
        {
            var entry = Instantiate(playerNameEntryPrefab, playerListContainer);

            var nameTMP   = entry.transform.Find("PlayerName")?.GetComponent<TextMeshProUGUI>();
            var indexTMP  = entry.transform.Find("PlayerIndex")?.GetComponent<TextMeshProUGUI>();
            var hostBadge = entry.transform.Find("HostBadge")?.gameObject;

            if (nameTMP  != null) nameTMP.text  = string.IsNullOrEmpty(player.playerName) ? "Connecting..." : player.playerName;
            if (indexTMP != null) indexTMP.text = $"{index + 1:D2}";
            if (hostBadge != null) hostBadge.SetActive(player.isLocalPlayer && NetworkServer.active);

            index++;
        }

        if (playerCountText != null)
            playerCountText.text = $"{LobbyPlayer.All.Count} / 10 PLAYERS";
    }

    void OnStartClicked()
    {
        startButton.interactable = false; // prevent double-click
        FindAnyObjectByType<GameNetworkManager>()?.StartGame("Game");
    }

    void OnLeaveClicked()
    {
        if (_dotsCoroutine != null) { StopCoroutine(_dotsCoroutine); _dotsCoroutine = null; }
        leaveButton.interactable = false; // prevent double-click
        FindAnyObjectByType<GameNetworkManager>()?.LeaveParty();
    }

    IEnumerator AnimateDots()
    {
        int dots = 0;
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            dots = (dots + 1) % 4;
            if (statusText != null)
                statusText.text = _statusBase + new string('.', dots);
        }
    }
}
