using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EpicTransport;
using System.Collections;

/// <summary>
/// Drop this on the same GameObject as cowsins' MainMenuManager in Assets/Scenes/MainMenu.unity.
/// Wire up the serialized fields in the Inspector to the existing Cowsins UI elements.
/// </summary>
public class SoulkhizenMenuUI : MonoBehaviour
{
    [Header("Cowsins Sections (CanvasGroups)")]
    [Tooltip("The main/home section CanvasGroup")]
    public CanvasGroup mainSection;
    [Tooltip("The play/multiplayer section CanvasGroup")]
    public CanvasGroup playSection;
    [Tooltip("The join-by-code sub-panel CanvasGroup (inside play section)")]
    public CanvasGroup joinSection;

    [Header("Join Panel")]
    public TMP_InputField roomCodeInput;
    public TextMeshProUGUI joinStatusText;
    public Button confirmJoinButton;

    [Header("Optional")]
    public Button hostButton;
    public Button joinButton;
    public Button browseButton;
    public Button quickTestButton;
    public Button backFromPlayButton;
    public Button backFromJoinButton;

    private GameNetworkManager NetManager
    {
        get
        {
            if (_net == null) _net = FindAnyObjectByType<GameNetworkManager>();
            return _net;
        }
    }
    private GameNetworkManager _net;

    void Update()
    {
        if (!Mirror.NetworkServer.active && !Mirror.NetworkClient.isConnected)
            EOSSDKComponent.Tick();
    }

    void Start()
    {
        _ = NetManager;

        ShowSection(mainSection);
        HideSection(playSection);
        HideSection(joinSection);

        if (roomCodeInput != null)
        {
            roomCodeInput.characterLimit = 6;
            roomCodeInput.onValueChanged.AddListener(OnCodeChanged);
        }

        hostButton?.onClick.AddListener(OnHostClicked);
        joinButton?.onClick.AddListener(OnJoinClicked);
        browseButton?.onClick.AddListener(OnBrowseClicked);
        quickTestButton?.onClick.AddListener(OnQuickTestClicked);
        backFromPlayButton?.onClick.AddListener(OnBackFromPlay);
        backFromJoinButton?.onClick.AddListener(OnBackFromJoin);
        confirmJoinButton?.onClick.AddListener(OnConfirmJoin);

        RefreshConfirmButton();
    }

    // ── Section helpers ──────────────────────────────────────────────

    void ShowSection(CanvasGroup cg)
    {
        if (cg == null) return;
        cg.gameObject.SetActive(true);
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    void HideSection(CanvasGroup cg)
    {
        if (cg == null) return;
        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;
        cg.gameObject.SetActive(false);
    }

    // ── Button callbacks ─────────────────────────────────────────────

    public void OnPlayClicked()
    {
        HideSection(mainSection);
        ShowSection(playSection);
        HideSection(joinSection);
    }

    public void OnHostClicked()
    {
        if (NetManager == null) return;
        LoadingOverlay.Show();
        if (!IsEOSReady()) { StartCoroutine(WaitThenHost()); return; }
        DoHost();
    }

    public void OnJoinClicked()
    {
        bool show = joinSection != null && !joinSection.gameObject.activeSelf;
        if (show) ShowSection(joinSection);
        else HideSection(joinSection);
        if (joinStatusText != null) joinStatusText.text = "";
        if (roomCodeInput != null) roomCodeInput.text = "";
        RefreshConfirmButton();
    }

    public void OnConfirmJoin()
    {
        if (NetManager == null) return;
        string code = roomCodeInput != null ? roomCodeInput.text.Trim().ToUpper() : "";
        if (code.Length != 6) { SetStatus("Enter a valid 6-character code."); return; }
        SetStatus("Searching...");
        if (roomCodeInput != null) roomCodeInput.interactable = false;
        if (confirmJoinButton != null) confirmJoinButton.interactable = false;
        LoadingOverlay.Show();
        if (!IsEOSReady()) { StartCoroutine(WaitThenJoin(code)); return; }
        DoJoin(code);
    }

    public void OnBrowseClicked()
    {
        LoadingOverlay.Show();
        UnityEngine.SceneManagement.SceneManager.LoadScene("Rooms");
    }

    public void OnQuickTestClicked()
    {
        if (NetManager == null) return;
        LoadingOverlay.Show();
        NetManager.StartPartyHost(_ => { });
    }

    public void OnBackFromPlay()
    {
        HideSection(playSection);
        HideSection(joinSection);
        ShowSection(mainSection);
    }

    public void OnBackFromJoin()
    {
        HideSection(joinSection);
        SetStatus("");
    }

    // ── Internal ─────────────────────────────────────────────────────

    void OnCodeChanged(string val)
    {
        if (roomCodeInput != null && val != val.ToUpper())
        {
            int caret = roomCodeInput.caretPosition;
            roomCodeInput.text = val.ToUpper();
            roomCodeInput.caretPosition = caret;
        }
        SetStatus("");
        RefreshConfirmButton();
    }

    void RefreshConfirmButton()
    {
        if (confirmJoinButton == null) return;
        bool ready = roomCodeInput != null && roomCodeInput.text.Length == 6;
        confirmJoinButton.interactable = ready;
        var img = confirmJoinButton.GetComponent<Image>();
        if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, ready ? 1f : 0.35f);
    }

    void SetStatus(string msg)
    {
        if (joinStatusText != null) joinStatusText.text = msg;
    }

    void DoHost()
    {
        NetManager.StartPartyHost(code =>
        {
            LoadingOverlay.Hide();
            if (code == null) SetStatus("Failed to create room. Try again.");
        });
    }

    void DoJoin(string code)
    {
        NetManager.JoinParty(code, () =>
        {
            SetStatus("Room not found.");
            LoadingOverlay.Hide();
            if (roomCodeInput != null) roomCodeInput.interactable = true;
            RefreshConfirmButton();
        });
    }

    IEnumerator WaitThenHost()
    {
        float t = 15f;
        while (!IsEOSReady() && t > 0f) { t -= Time.deltaTime; yield return null; }
        if (!IsEOSReady()) { LoadingOverlay.Hide(); SetStatus("EOS not ready. Try again."); yield break; }
        DoHost();
    }

    IEnumerator WaitThenJoin(string code)
    {
        float t = 15f;
        while (!IsEOSReady() && t > 0f) { t -= Time.deltaTime; yield return null; }
        if (!IsEOSReady())
        {
            SetStatus("EOS not ready.");
            LoadingOverlay.Hide();
            if (roomCodeInput != null) roomCodeInput.interactable = true;
            RefreshConfirmButton();
            yield break;
        }
        DoJoin(code);
    }

    static bool IsEOSReady()
    {
        try { return EOSSDKComponent.Initialized; }
        catch { return false; }
    }
}
