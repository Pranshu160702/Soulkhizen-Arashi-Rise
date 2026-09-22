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
            Debug.Log($"[RefreshParty] Source=PartyManager count={PartyManager.Instance.memberNames.Count} others={others.Count}");
        }
        else
        {
            foreach (var lp in LobbyPlayer.All)
                if (!string.IsNullOrEmpty(lp.playerName) && lp.playerName != GameNetworkManager.LocalPlayerName)
                    others.Add(lp.playerName);
            Debug.Log($"[RefreshParty] Source=LobbyPlayer.All count={LobbyPlayer.All.Count} others={others.Count}");
        }

        Debug.Log($"[RefreshParty] LocalPlayer='{GameNetworkManager.LocalPlayerName}' others=[{string.Join(",", others)}]");

        for (int i = 1; i < partySlots.Length; i++)
        {
            if (partySlots[i] == null) continue;
            int idx = i - 1;
            if (idx < others.Count)
            {
                string name = others[idx];
                Debug.Log($"[RefreshParty] Slot {i}: name='{name}' occupied={partySlots[i].IsOccupied} occupiedName='{partySlots[i].OccupiedName}' prefab={partySlots[i].playerCardPrefab != null}");
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
        // Only the host (leader) can start
        startButton.interactable = Mirror.NetworkServer.active;
    }

    public void RefreshLeaveButton()
    {
        if (leaveButton == null) return;
        bool isGuest = Mirror.NetworkClient.isConnected && !Mirror.NetworkServer.active;
        leaveButton.gameObject.SetActive(isGuest);
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
        // Only clear other players' slots — keep local player card in slot 0
        for (int i = 1; i < partySlots.Length; i++) partySlots[i]?.DestroyCard();

        if (Mirror.NetworkServer.active || Mirror.NetworkClient.isConnected)
        {
            Net?.LeaveParty();
            StartCoroutine(WaitForDisconnectThenJoin(code));
            return;
        }

        ExecuteJoin(code);
    }

    void ExecuteJoin(string code)
    {
        Net.JoinParty(code, () =>
        {
            // Not found — restore own party
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
        Net?.LeaveParty();
        StartCoroutine(ReHostAfterLeave());
    }

    IEnumerator ReHostAfterLeave()
    {
        float t = 8f;
        while ((Mirror.NetworkServer.active || Mirror.NetworkClient.active) && t > 0f)
        { t -= Time.deltaTime; yield return null; }
        // Extra wait for EOS session destroy to complete async before creating a new one
        yield return new WaitForSeconds(1.5f);
        InitLocalPlayerSlot();
        AutoHost();
    }

    // Called by GNM when client unexpectedly disconnects (host left, kicked, etc.)
    public void OnDisconnectedFromParty()
    {
        Debug.Log("[MenuController] Disconnected from party — re-hosting");
        for (int i = 1; i < partySlots.Length; i++) partySlots[i]?.DestroyCard();
        RefreshStartButton();
        RefreshLeaveButton();
        StartCoroutine(ReHostAfterLeave());
    }

    // ── Scene Selection ──────────────────────────────────────────────

    public void SelectGameMode(string sceneName, string displayName)
    {
        _selectedSceneName       = sceneName;
        _selectedModeDisplayName = displayName;
        if (queueValueText) queueValueText.text = displayName;
        ShowPartyTab();
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
