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
    private Coroutine _reHostCoroutine;

    private static readonly Regex NameRegex      = new Regex(@"^[a-zA-Z0-9]+$");
    private static readonly Regex PartyCodeRegex = new Regex(@"^[a-zA-Z0-9]{6}$");

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
        if (IsEOSReady())            { _waitingForEOS = false; DoAutoHost(); }
        else if (_eosWaitTimer <= 0f){ _waitingForEOS = false; SetPartyCode("EOS Failed"); }
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
        RefreshParty();
        RefreshStartButton();
        RefreshLeaveButton();
        AutoHost();
    }

    void InitLocalPlayerSlot()
    {
        if (partySlots == null || partySlots.Length == 0 || partySlots[0] == null) return;
        partySlots[0].DestroyCard();
        partySlots[0].SpawnCard(GameNetworkManager.LocalPlayerName, true);
    }

    public void OnMainMenuShown() => AutoHost();

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

    // ── Auto Host ────────────────────────────────────────────────────

    void AutoHost()
    {
        if (Net == null) return;
        if (_waitingForEOS) { _eosWaitTimer = 15f; return; }

        if (Mirror.NetworkServer.active || Mirror.NetworkClient.isConnected)
        {
            SetPartyCode(GameNetworkManager.CurrentLobbyCode);
            return;
        }

        if (!string.IsNullOrEmpty(GameNetworkManager.CurrentLobbyCode))
        {
            SetPartyCode(GameNetworkManager.CurrentLobbyCode);
            return;
        }

        SetPartyCode("Generating...");
        if (!IsEOSReady()) { _waitingForEOS = true; _eosWaitTimer = 15f; return; }
        DoAutoHost();
    }

    void DoAutoHost()
    {
        Net.StartPartyHost(code =>
        {
            SetPartyCode(string.IsNullOrEmpty(code) ? "------" : code);
            if (!string.IsNullOrEmpty(code))
            {
                InitLocalPlayerSlot();
                RefreshStartButton();
                RefreshLeaveButton();
            }
        });
    }

    void SetPartyCode(string code) { if (_partyCodeText) _partyCodeText.text = code; }

    // ── Party Refresh ────────────────────────────────────────────────

    public void RefreshParty()
    {
        if (partySlots == null) return;

        var others = new System.Collections.Generic.List<string>();
        if (PartyManager.Instance != null && PartyManager.Instance.memberNames.Count > 0)
        {
            foreach (var n in PartyManager.Instance.memberNames)
                if (n != GameNetworkManager.LocalPlayerName)
                    others.Add(n);
        }
        else
        {
            foreach (var lp in LobbyPlayer.All)
                if (!string.IsNullOrEmpty(lp.playerName) && lp.playerName != GameNetworkManager.LocalPlayerName)
                    others.Add(lp.playerName);
        }

        for (int i = 1; i < partySlots.Length; i++)
        {
            if (partySlots[i] == null) continue;
            int idx = i - 1;
            if (idx < others.Count)
            {
                string name = others[idx];
                if (!partySlots[i].IsOccupied || partySlots[i].OccupiedName != name)
                    partySlots[i].SpawnCard(name, true);
            }
            else
                partySlots[i].DestroyCard();
        }

        SetPartyCode(string.IsNullOrEmpty(GameNetworkManager.CurrentLobbyCode)
            ? "------" : GameNetworkManager.CurrentLobbyCode);

        RefreshStartButton();
        RefreshLeaveButton();
    }

    public void RefreshStartButton()
    {
        if (startButton == null) return;
        bool isHost = Mirror.NetworkServer.active;
        startButton.interactable = isHost;
        if (queueButton != null) queueButton.interactable = isHost;
    }

    public void RefreshLeaveButton()
    {
        if (leaveButton == null) return;
        bool inParty = Mirror.NetworkClient.isConnected;
        leaveButton.gameObject.SetActive(inParty);
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
        DoJoin(code);
    }

    void DoJoin(string code)
    {
        for (int i = 1; i < partySlots.Length; i++) partySlots[i]?.DestroyCard();
        LoadingOverlay.Show();

        if (Mirror.NetworkServer.active || Mirror.NetworkClient.isConnected)
        {
            GameNetworkManager.IsIntentionallyJoining = true;
            GameNetworkManager.CurrentLobbyCode = string.Empty;
            Net?.LeaveParty();
            StartCoroutine(WaitForDisconnectThenJoin(code));
            return;
        }

        ExecuteJoin(code);
    }

    void ExecuteJoin(string code)
    {
        GameNetworkManager.IsIntentionallyJoining = true;
        Net.JoinParty(code, () =>
        {
            LoadingOverlay.Hide();
            GameNetworkManager.IsIntentionallyJoining = false;
            SetJoinStatus("Party Not Found");
            if (joinCodeInput) joinCodeInput.interactable = true;
            RefreshJoinButton();
            InitLocalPlayerSlot();
            AutoHost();
        });
    }

    // Called by GNM.OnClientConnect when we successfully join someone's party
    public void OnJoinedParty()
    {
        LoadingOverlay.Hide();
        Debug.Log("[MenuController] OnJoinedParty");
        if (joinCodeInput) { joinCodeInput.text = ""; joinCodeInput.interactable = true; }
        RefreshJoinButton();
        SetJoinStatus("Party Joined Successfully", success: true);
        if (_fadeStatusCoroutine != null) StopCoroutine(_fadeStatusCoroutine);
        _fadeStatusCoroutine = StartCoroutine(FadeJoinStatus());
        RefreshStartButton();
        RefreshLeaveButton();
    }

    IEnumerator FadeJoinStatus()
    {
        yield return new WaitForSeconds(5f);
        SetJoinStatus("");
        _fadeStatusCoroutine = null;
    }

    IEnumerator WaitForDisconnectThenJoin(string code)
    {
        float t = 8f;
        while ((Mirror.NetworkServer.active || Mirror.NetworkClient.active) && t > 0f)
        { t -= Time.deltaTime; yield return null; }
        yield return null;
        ExecuteJoin(code);
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
        DoJoin(code);
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
        foreach (var slot in partySlots) slot?.DestroyCard();
        LoadingOverlay.Show();
        Net?.LeaveParty();
        if (_reHostCoroutine != null) StopCoroutine(_reHostCoroutine);
        _reHostCoroutine = StartCoroutine(ReHostAfterLeave());
    }

    IEnumerator ReHostAfterLeave()
    {
        float t = 8f;
        while ((Mirror.NetworkServer.active || Mirror.NetworkClient.active) && t > 0f)
        { t -= Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(3f);
        _reHostCoroutine = null;
        LoadingOverlay.Hide();
        InitLocalPlayerSlot();
        AutoHost();
    }

    // Called by GNM when client unexpectedly disconnects (host left, kicked, etc.)
    public void OnDisconnectedFromParty()
    {
        Debug.Log("[MenuController] Disconnected from party — re-hosting");
        for (int i = 1; i < partySlots.Length; i++) partySlots[i]?.DestroyCard();
        GameNetworkManager.CurrentLobbyCode = string.Empty;
        RefreshStartButton();
        RefreshLeaveButton();
        LoadingOverlay.Show();
        if (_reHostCoroutine != null) StopCoroutine(_reHostCoroutine);
        _reHostCoroutine = StartCoroutine(ReHostAfterLeave());
    }

    // Called by GNM RPC when the host intentionally leaves with members still in party
    public void OnHostDisconnected()
    {
        Debug.Log("[MenuController] Host disconnected — party disbanded");
        for (int i = 1; i < partySlots.Length; i++) partySlots[i]?.DestroyCard();
        GameNetworkManager.CurrentLobbyCode = string.Empty;
        RefreshStartButton();
        RefreshLeaveButton();
        SetJoinStatus("Host has disconnected");
        if (_fadeStatusCoroutine != null) StopCoroutine(_fadeStatusCoroutine);
        _fadeStatusCoroutine = StartCoroutine(FadeJoinStatus());
        LoadingOverlay.Show();
        if (_reHostCoroutine != null) StopCoroutine(_reHostCoroutine);
        _reHostCoroutine = StartCoroutine(ReHostAfterLeave());
    }

    // ── Scene Selection ──────────────────────────────────────────────

    public void SelectGameMode(string sceneName, string displayName)
    {
        if (!Mirror.NetworkServer.active) return;
        _selectedSceneName       = sceneName;
        _selectedModeDisplayName = displayName;
        if (queueValueText) queueValueText.text = displayName;
        if (PartyManager.Instance != null && Mirror.NetworkServer.active)
            PartyManager.Instance.SetGameMode(sceneName, displayName);
        ShowPartyTab();
    }

    // Called by PartyManager when host changes game mode — updates guests
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
