using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EpicTransport;
using cowsins;

public class MenuController : MonoBehaviour
{
    public static MenuController Instance { get; private set; }

    [Header("Main Sections")]
    public GameObject header;
    public GameObject body;
    public GameObject footer;
    public GameObject friendsPanel;
    public GameObject loginPanel;

    [Header("Tabs")]
    public GameObject queueTab;
    public GameObject settingsTab;
    public GameObject partyTab;

    [Header("Header Buttons")]
    public CowsinsButton playButton;
    public CowsinsButton queueButton;
    public CowsinsButton settingsButton;

    [Header("Party Slots")]
    public GameObject playerCardPrefab;
    public PartySlot[] partySlots; // 0 = local, 1-4 = others

    [Header("Party - MyPartyPanel")]
    public TextMeshProUGUI queueValueText;
    public CowsinsButton generateCodeButton;
    public TextMeshProUGUI slotsRemainingText;

    [Header("Party - JoinOtherPartyPanel")]
    public TMP_InputField joinCodeInput;
    public CowsinsButton joinConfirmButton;
    public TextMeshProUGUI joinStatusText;

    [Header("Footer")]
    public CowsinsButton startButton;
    public CowsinsButton leaveButton;

    // ── Private ──────────────────────────────────────────────────────
    private TMP_InputField _nameInput;
    private Button _nameConfirmBtn;
    private TextMeshProUGUI _nameErrorText;
    private TextMeshProUGUI _partyCodeText;

    private bool _waitingForEOS;
    private float _eosWaitTimer;
    private string _selectedSceneName;
    private string _selectedModeDisplayName;
    private Coroutine _fadeStatusCoroutine;

    private static readonly Regex NameRegex      = new Regex(@"^[a-zA-Z0-9]+$",      RegexOptions.None, System.TimeSpan.FromSeconds(1));
    private static readonly Regex PartyCodeRegex = new Regex(@"^[a-zA-Z0-9]{6}$", RegexOptions.None, System.TimeSpan.FromSeconds(1));

    private GameNetworkManager Net => GameNetworkManager.singleton as GameNetworkManager;

    // ── Unity ────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        var card = loginPanel?.transform.Find("Card");
        _nameInput      = card?.Find("NameInputField")?.GetComponent<TMP_InputField>();
        _nameConfirmBtn = card?.Find("NameConfirmBtn")?.GetComponent<Button>();
        _nameErrorText  = card?.Find("ErrorText")?.GetComponent<TextMeshProUGUI>();

        var myPartyPanel = partyTab?.transform.Find("JoinPartyPanel/MyPartyPanel");
        _partyCodeText = myPartyPanel?.Find("PartyCodeText")?.GetComponent<TextMeshProUGUI>();

        var firstBtn = queueTab?.GetComponentInChildren<SceneSelectionButton>(true);
        _selectedSceneName       = firstBtn != null ? firstBtn.sceneName   : "Training";
        _selectedModeDisplayName = firstBtn != null ? firstBtn.displayName : "TRAINING RANGE";

        if (partySlots != null && playerCardPrefab != null)
            foreach (var slot in partySlots)
                if (slot != null) slot.playerCardPrefab = playerCardPrefab;
    }

    void Start()
    {
        StartCoroutine(EOSTick());

        _nameConfirmBtn?.onClick.AddListener(OnNameConfirmed);
        _nameInput?.onValueChanged.AddListener(_ => ClearError());

        playButton?.onClick.AddListener(ShowPartyTab);
        queueButton?.onClick.AddListener(ShowQueueTab);
        settingsButton?.onClick.AddListener(ShowSettingsTab);

        generateCodeButton?.onClick.AddListener(EnsureHosted);
        joinConfirmButton?.onClick.AddListener(OnConfirmJoin);
        startButton?.onClick.AddListener(OnStartClicked);
        leaveButton?.onClick.AddListener(OnLeaveClicked);

        if (joinCodeInput != null)
        {
            joinCodeInput.characterLimit = 6;
            joinCodeInput.onValueChanged.AddListener(v =>
            {
                if (v != v.ToUpper()) joinCodeInput.text = v.ToUpper();
                RefreshJoinButton();
            });
        }

        if (!PlayerPrefs.HasKey("PlayerName"))
            ShowLoginPanel();
        else
        {
            GameNetworkManager.LocalPlayerName = PlayerPrefs.GetString("PlayerName");
            ShowMainMenu();
        }
    }

    void Update()
    {
        if (!_waitingForEOS) return;
        _eosWaitTimer -= Time.deltaTime;
        if (IsEOSReady())             { _waitingForEOS = false; DoHost(); }
        else if (_eosWaitTimer <= 0f) { _waitingForEOS = false; SetPartyCode("EOS Failed"); }
    }

    // ── Login ────────────────────────────────────────────────────────

    void ShowLoginPanel()
    {
        loginPanel?.SetActive(true);
        header?.SetActive(false);
        body?.SetActive(false);
        footer?.SetActive(false);
        friendsPanel?.SetActive(false);
    }

    void OnNameConfirmed()
    {
        string n = _nameInput != null ? _nameInput.text.Trim() : "";
        if (n.Length < 3)          { ShowError("Name must be at least 3 characters."); return; }
        if (!NameRegex.IsMatch(n)) { ShowError("Only letters and numbers allowed.");   return; }

        PlayerPrefs.SetString("PlayerName", n);
        PlayerPrefs.Save();
        GameNetworkManager.LocalPlayerName = n;
        loginPanel?.SetActive(false);
        ShowMainMenu();
    }

    void ShowError(string msg) { if (_nameErrorText) _nameErrorText.text = msg; }
    void ClearError()          { if (_nameErrorText) _nameErrorText.text = ""; }

    // ── Main Menu ────────────────────────────────────────────────────

    void ShowMainMenu()
    {
        header?.SetActive(true);
        body?.SetActive(true);
        footer?.SetActive(true);
        friendsPanel?.SetActive(true);
        if (queueValueText) queueValueText.text = _selectedModeDisplayName;
        InitLocalPlayerSlot();
        RefreshStartButton();
        RefreshLeaveButton();
    }

    void InitLocalPlayerSlot()
    {
        if (partySlots == null || partySlots.Length == 0 || partySlots[0] == null) return;
        partySlots[0].DestroyCard();
        partySlots[0].SpawnCard(GameNetworkManager.LocalPlayerName, true);
    }

    // ── Tabs ─────────────────────────────────────────────────────────

    void SetTab(GameObject active)
    {
        queueTab?.SetActive(queueTab == active);
        settingsTab?.SetActive(settingsTab == active);
        partyTab?.SetActive(partyTab == active);
    }

    public void ShowQueueTab()    => SetTab(queueTab);
    public void ShowSettingsTab() => SetTab(settingsTab);
    public void ShowPartyTab()    => SetTab(partyTab);

    // ── Host on Demand ────────────────────────────────────────────────
    // Called when the player wants to share their party code (e.g. clicks "Copy Code" or opens party panel)

    public void EnsureHosted()
    {
        if (Net == null) return;
        if (Mirror.NetworkServer.active)
        {
            SetPartyCode(GameNetworkManager.CurrentLobbyCode);
            return;
        }

        SetPartyCode("Generating...");

        if (!IsEOSReady()) { _waitingForEOS = true; _eosWaitTimer = 15f; return; }
        DoHost();
    }

    void DoHost()
    {
        Net.StartPartyHost(code =>
        {
            SetPartyCode(string.IsNullOrEmpty(code) ? "------" : code);
            InitLocalPlayerSlot();
            RefreshStartButton();
            RefreshLeaveButton();
        });
    }

    void SetPartyCode(string code) { if (_partyCodeText) _partyCodeText.text = code; }

    void ClearGuestSlots()
    {
        if (partySlots == null) return;
        for (int i = 1; i < partySlots.Length; i++) partySlots[i]?.DestroyCard();
    }


    // ── Card Lifecycle ────────────────────────────────────────────────

    // Finds the first empty guest slot and spawns a card there
    public void SpawnCardForPlayer(string playerName)
    {
        if (partySlots == null) { Debug.Log($"[MC] SpawnCardForPlayer('{playerName}') — partySlots is null!"); return; }
        // Don't double-spawn
        for (int i = 1; i < partySlots.Length; i++)
            if (partySlots[i] != null && partySlots[i].OccupiedName == playerName)
            {
                Debug.Log($"[MC] SpawnCardForPlayer('{playerName}') — already in slot {i}, skipping");
                return;
            }
        // Find first empty slot
        for (int i = 1; i < partySlots.Length; i++)
        {
            if (partySlots[i] != null && !partySlots[i].IsOccupied)
            {
                Debug.Log($"[MC] SpawnCardForPlayer('{playerName}') — spawning in slot {i}");
                partySlots[i].SpawnCard(playerName, true);
                return;
            }
        }
        Debug.Log($"[MC] SpawnCardForPlayer('{playerName}') — NO empty slot found!");
    }

    // Finds the slot occupied by this player and destroys their card
    public void DestroyCardForPlayer(string playerName)
    {
        if (partySlots == null) return;
        for (int i = 1; i < partySlots.Length; i++)
            if (partySlots[i] != null && partySlots[i].OccupiedName == playerName)
            {
                Debug.Log($"[MC] DestroyCardForPlayer('{playerName}') — destroying slot {i}");
                partySlots[i].DestroyCard();
                return;
            }
        Debug.Log($"[MC] DestroyCardForPlayer('{playerName}') — not found in any slot");
    }

    public void RefreshSlotsRemaining(int memberCount)
    {
        if (slotsRemainingText == null) return;
        int remaining = PartyManager.MaxMembers - memberCount;
        slotsRemainingText.text = $"{remaining} slot{(remaining == 1 ? "" : "s")} remaining";
    }

    public void RefreshStartButton()
    {
        if (startButton == null) return;
        bool isHost  = Mirror.NetworkServer.active;
        bool inParty = isHost || Mirror.NetworkClient.isConnected;
        startButton.interactable = isHost;
        if (queueButton != null) queueButton.interactable = isHost;
        if (generateCodeButton != null) generateCodeButton.gameObject.SetActive(!inParty);
    }

    public void RefreshLeaveButton()
    {
        if (leaveButton == null) return;
        bool inParty = Mirror.NetworkServer.active || Mirror.NetworkClient.isConnected;
        leaveButton.gameObject.SetActive(inParty);
    }

    // Shared reset called by all disconnect paths (intentional leave, host disconnect, crash, quit)
    void ResetToNoParty()
    {
        Debug.Log("[MC] ResetToNoParty");
        ClearGuestSlots();
        GameNetworkManager.CurrentLobbyCode = string.Empty;
        SetPartyCode("------");
        leaveButton?.gameObject.SetActive(false);
        generateCodeButton?.gameObject.SetActive(true);
        if (startButton != null) startButton.interactable = false;
        if (queueButton != null) queueButton.interactable = false;
        if (slotsRemainingText != null) slotsRemainingText.text = "";
        LoadingOverlay.Hide();
    }

    // ── Join Party ───────────────────────────────────────────────────

    void OnConfirmJoin()
    {
        string code = joinCodeInput != null ? joinCodeInput.text.Trim().ToUpper() : "";
        if (!PartyCodeRegex.IsMatch(code)) { SetJoinStatus("ENTER VALID CODE"); return; }

        SetJoinStatus("Searching...");
        joinCodeInput.interactable = false;
        if (joinConfirmButton) joinConfirmButton.interactable = false;

        if (!IsEOSReady()) { StartCoroutine(WaitThenJoin(code)); return; }
        ExecuteJoin(code);
    }

    void ExecuteJoin(string code)
    {
        LoadingOverlay.Show();
        Net.JoinParty(code, () =>
        {
            // Not found — restore UI, stay in current state (no re-host)
            LoadingOverlay.Hide();
            SetJoinStatus("Party Not Found");
            if (joinCodeInput) joinCodeInput.interactable = true;
            RefreshJoinButton();
            // Restore party code display (we never left our own party)
            SetPartyCode(string.IsNullOrEmpty(GameNetworkManager.CurrentLobbyCode)
                ? "------" : GameNetworkManager.CurrentLobbyCode);
        });
    }

    // Called by GNM.OnClientConnect when we successfully join someone's party
    public void OnJoinedParty()
    {
        Debug.Log("[MC] OnJoinedParty");
        LoadingOverlay.Hide();
        if (joinCodeInput) { joinCodeInput.text = ""; joinCodeInput.interactable = true; }
        RefreshJoinButton();
        SetJoinStatus("Party Joined Successfully", success: true);
        if (_fadeStatusCoroutine != null) StopCoroutine(_fadeStatusCoroutine);
        _fadeStatusCoroutine = StartCoroutine(FadeJoinStatus());
        SetPartyCode(string.IsNullOrEmpty(GameNetworkManager.CurrentLobbyCode)
            ? "------" : GameNetworkManager.CurrentLobbyCode);
        RefreshStartButton();
        RefreshLeaveButton();
    }

    IEnumerator FadeJoinStatus()
    {
        yield return new WaitForSeconds(5f);
        SetJoinStatus("");
        _fadeStatusCoroutine = null;
    }

    IEnumerator WaitThenJoin(string code)
    {
        float t = 15f;
        while (!IsEOSReady() && t > 0f) { t -= Time.deltaTime; yield return null; }
        if (!IsEOSReady())
        {
            SetJoinStatus("EOS not ready.");
            if (joinCodeInput) joinCodeInput.interactable = true;
            RefreshJoinButton();
            yield break;
        }
        ExecuteJoin(code);
    }

    // ── Start Game ───────────────────────────────────────────────────

    void OnStartClicked()
    {
        if (Net == null || !Mirror.NetworkServer.active) return;
        LoadingOverlay.Show();
        Net.StartGame(_selectedSceneName);
    }

    // ── Leave Party ──────────────────────────────────────────────────

    void OnLeaveClicked()
    {
        ClearGuestSlots();
        leaveButton.gameObject.SetActive(false);
        LoadingOverlay.Show();
        Net?.LeaveParty();
        StartCoroutine(WaitForLeave());
    }

    IEnumerator WaitForLeave()
    {
        float t = 6f;
        while ((Mirror.NetworkServer.active || Mirror.NetworkClient.active) && t > 0f)
        { t -= Time.deltaTime; yield return null; }
        ResetToNoParty();
        InitLocalPlayerSlot();
    }

    // Called by GNM on any unintentional disconnect (crash, network drop, host vanished, game quit)
    public void OnLostConnection()
    {
        Debug.Log("[MC] OnLostConnection");
        ResetToNoParty();
        InitLocalPlayerSlot();
    }

    // Called by GNM RPC when the host intentionally leaves with members still in party
    public void OnHostDisconnected()
    {
        ResetToNoParty();
        InitLocalPlayerSlot();
        SetJoinStatus("Host has disconnected");
        if (_fadeStatusCoroutine != null) StopCoroutine(_fadeStatusCoroutine);
        _fadeStatusCoroutine = StartCoroutine(FadeJoinStatus());
    }

    // ── Scene Selection ──────────────────────────────────────────────

    public void SelectGameMode(string sceneName, string displayName)
    {
        if (!Mirror.NetworkServer.active) return;
        _selectedSceneName       = sceneName;
        _selectedModeDisplayName = displayName;
        if (queueValueText) queueValueText.text = displayName;
        if (PartyManager.Instance != null)
            PartyManager.Instance.SetGameMode(sceneName, displayName);
        ShowPartyTab();
    }

    public void OnGameModeChanged(string sceneName, string displayName)
    {
        _selectedSceneName       = sceneName;
        _selectedModeDisplayName = displayName;
        if (queueValueText) queueValueText.text = displayName;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    void SetJoinStatus(string msg, bool success = false)
    {
        if (joinStatusText == null) return;
        joinStatusText.text  = msg;
        joinStatusText.color = success ? new Color(0.29f, 0.85f, 0.29f) : Color.white;
    }

    void RefreshJoinButton()
    {
        if (joinConfirmButton)
            joinConfirmButton.interactable = joinCodeInput != null && joinCodeInput.text.Length == 6;
    }

    static bool IsEOSReady()
    {
        try { return EOSSDKComponent.Initialized; }
        catch { return false; }
    }

    IEnumerator EOSTick()
    {
        var wait = new WaitForSeconds(0.1f);
        while (true)
        {
            if (!Mirror.NetworkServer.active && !Mirror.NetworkClient.isConnected)
                EOSSDKComponent.Tick();
            yield return wait;
        }
    }
}
