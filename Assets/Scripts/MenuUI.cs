using UnityEngine;
using TMPro;
using UnityEngine.UI;
using EpicTransport;

public class MenuUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject playPanel;
    public GameObject joinPanel;

    [Header("Join Panel")]
    public TMP_InputField roomCodeInput;
    public TextMeshProUGUI joinStatusText;
    public Button confirmJoinButton;

    private GameNetworkManager NetManager
    {
        get
        {
            if (_netManager == null)
                _netManager = FindAnyObjectByType<GameNetworkManager>();
            return _netManager;
        }
    }
    private GameNetworkManager _netManager;

    void Update()
    {
        // Only tick EOS when transport isn't active — transport ticks it via ServerEarlyUpdate/ClientEarlyUpdate
        if (!Mirror.NetworkServer.active && !Mirror.NetworkClient.isConnected)
            EpicTransport.EOSSDKComponent.Tick();
    }

    void Start()
    {
        // Warm up the reference early but don't fail if not found yet
        _ = NetManager;

        playPanel?.SetActive(false);
        joinPanel?.SetActive(false);

        if (roomCodeInput != null)
        {
            roomCodeInput.characterLimit = 6;
            roomCodeInput.onValueChanged.AddListener(OnCodeInputChanged);
        }

        UpdateEnterButton();
    }

    void OnCodeInputChanged(string val)
    {
        if (roomCodeInput != null && val != val.ToUpper())
        {
            int caret = roomCodeInput.caretPosition;
            roomCodeInput.text = val.ToUpper();
            roomCodeInput.caretPosition = caret;
        }
        if (joinStatusText != null) joinStatusText.text = "";
        UpdateEnterButton();
    }

    void UpdateEnterButton()
    {
        if (confirmJoinButton == null) return;
        bool ready = roomCodeInput != null && roomCodeInput.text.Length == 6;
        confirmJoinButton.interactable = ready;
        float alpha = ready ? 1f : 0.25f;

        var img = confirmJoinButton.GetComponent<Image>();
        if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, alpha);

        var textChild = confirmJoinButton.transform.Find("Text");
        if (textChild != null)
        {
            var cg = textChild.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = alpha;
            var mr = textChild.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.SetColor("_FaceColor", new Color(1f, 1f, 1f, alpha));
        }
    }

    void SetPanelButtonsFaded(GameObject panel, bool faded, string exemptName = null)
    {
        if (panel == null) return;
        var vlg = panel.GetComponentInChildren<VerticalLayoutGroup>();
        if (vlg == null) return;
        foreach (Transform child in vlg.transform)
        {
            if (exemptName != null && child.name == exemptName) continue;
            var btn = child.GetComponent<Button>() ?? child.GetComponentInChildren<Button>();
            if (btn != null) btn.interactable = !faded;
            var et = child.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (et != null) et.enabled = !faded;
            var img = child.GetComponent<Image>();
            if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, faded ? 0.25f : 1f);
            var mr = child.GetComponentInChildren<MeshRenderer>(true);
            if (mr != null) mr.material.SetColor("_FaceColor", new Color(1f, 1f, 1f, faded ? 0.25f : 1f));
        }
    }

    void SetPlayButtonsFaded(bool faded, string exemptName = null)
        => SetPanelButtonsFaded(playPanel, faded, exemptName);

    public void OnPlayClicked()
    {
        playPanel?.SetActive(true);
        joinPanel?.SetActive(false);
        SetPlayButtonsFaded(false);
        SetPanelButtonsFaded(mainPanel, true, "Btn_PlayCampaign");
    }

    // ── Host Game ──
    public void OnHostClicked()
    {
        if (NetManager == null) { Debug.LogError("[MenuUI] netManager is null"); return; }
        LoadingOverlay.Show();
        if (!IsEOSReady()) { StartCoroutine(WaitForEOSThenHost()); return; }
        DoHost();
    }

    void DoHost()
    {
        NetManager.StartPartyHost(code =>
        {
            LoadingOverlay.Hide();
            if (code == null && joinStatusText != null)
                joinStatusText.text = "Failed to create room. Try again.";
        });
    }

    System.Collections.IEnumerator WaitForEOSThenHost()
    {
        float t = 15f;
        while (!IsEOSReady() && t > 0f) { t -= UnityEngine.Time.deltaTime; yield return null; }
        if (!IsEOSReady())
        {
            Debug.LogError("[MenuUI] EOS not ready after timeout — cannot host");
            LoadingOverlay.Hide();
            if (joinStatusText != null) joinStatusText.text = "EOS not ready. Try again.";
            yield break;
        }
        Debug.Log("[MenuUI] EOS ready — proceeding to host");
        DoHost();
    }

    // ── Join Game ──
    public void OnJoinClicked()
    {
        bool show = joinPanel != null && !joinPanel.activeSelf;
        joinPanel?.SetActive(show);
        if (joinStatusText != null) joinStatusText.text = "";
        if (roomCodeInput != null) roomCodeInput.text = "";
        UpdateEnterButton();
        if (show) StartCoroutine(DelayedUpdateEnterButton());

        SetPlayButtonsFaded(show, "Btn_JoinGame");

        if (joinPanel != null)
        {
            var joinBtn = joinPanel.transform.parent?.gameObject;
            if (joinBtn != null)
            {
                var et = joinBtn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (et != null) et.enabled = !show;
                var btn = joinBtn.GetComponent<Button>();
                if (btn != null) btn.interactable = !show;
            }
        }
    }

    System.Collections.IEnumerator DelayedUpdateEnterButton()
    {
        yield return null; yield return null;
        UpdateEnterButton();
    }

    public void OnConfirmJoinClicked()
    {
        if (NetManager == null) { Debug.LogError("[MenuUI] netManager is null"); return; }
        string code = roomCodeInput != null ? roomCodeInput.text.Trim().ToUpper() : "";
        if (code.Length != 6) { if (joinStatusText != null) joinStatusText.text = "Enter a valid 6-character code."; return; }
        if (joinStatusText != null) joinStatusText.text = "Searching...";
        if (roomCodeInput != null) roomCodeInput.interactable = false;
        if (confirmJoinButton != null) confirmJoinButton.interactable = false;
        LoadingOverlay.Show();
        if (!IsEOSReady()) { StartCoroutine(WaitForEOSThenJoin(code)); return; }
        DoJoin(code);
    }

    void DoJoin(string code)
    {
        NetManager.JoinParty(code, () =>
        {
            // Failed
            if (joinStatusText != null) joinStatusText.text = "Room not found.";
            LoadingOverlay.Hide();
            if (roomCodeInput != null) roomCodeInput.interactable = true;
            if (confirmJoinButton != null) confirmJoinButton.interactable = true;
            UpdateEnterButton();
        });
        // Note: on success Mirror will load the Lobby scene automatically via onlineScene
    }

    System.Collections.IEnumerator WaitForEOSThenJoin(string code)
    {
        float t = 15f;
        while (!IsEOSReady() && t > 0f) { t -= UnityEngine.Time.deltaTime; yield return null; }
        if (!IsEOSReady())
        {
            if (joinStatusText != null) joinStatusText.text = "EOS failed.";
            LoadingOverlay.Hide();
            if (roomCodeInput != null) roomCodeInput.interactable = true;
            if (confirmJoinButton != null) confirmJoinButton.interactable = true;
            UpdateEnterButton();
            yield break;
        }
        DoJoin(code);
    }

    static bool IsEOSReady()
    {
        try { return EOSSDKComponent.Initialized; }
        catch { return false; }
    }

    // ── Quick Test ──
    public void OnTestingClicked()
    {
        if (NetManager == null) { Debug.LogError("[MenuUI] netManager is null"); return; }
        LoadingOverlay.Show();
        NetManager.StartPartyHost(_ => { });
    }

    // ── Show Rooms ──
    public void OnJoinRandomClicked()
    {
        joinPanel?.SetActive(false);
        SetPlayButtonsFaded(false);
        SetPanelButtonsFaded(mainPanel, false);
        UnityEngine.SceneManagement.SceneManager.LoadScene("Rooms");
    }

    // ── Back buttons ──
    public void OnBackFromPlay()
    {
        ReenableJoinButton();
        SetPlayButtonsFaded(false);
        SetPanelButtonsFaded(mainPanel, false);
        joinPanel?.SetActive(false);
        playPanel?.SetActive(false);
    }

    void ReenableJoinButton()
    {
        if (joinPanel == null) return;
        var joinBtn = joinPanel.transform.parent?.gameObject;
        if (joinBtn == null) return;
        var et = joinBtn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
        if (et != null) et.enabled = true;
        var btn = joinBtn.GetComponent<Button>();
        if (btn != null) btn.interactable = true;
    }

    public void OnBackFromJoin()
    {
        joinPanel?.SetActive(false);
        if (joinStatusText != null) joinStatusText.text = "";
    }

    public void OnBackClicked()
    {
        joinPanel?.SetActive(false);
        playPanel?.SetActive(false);
        SetPlayButtonsFaded(false);
        LoadingOverlay.Hide();
    }

    public void OnDeveloperClicked()
    {
        var extras = FindInScene("Developer") ?? FindInScene("EXTRAS");
        if (extras != null) extras.SetActive(!extras.activeSelf);
    }

    public void OnDeveloperBackClicked()
    {
        var extras = FindInScene("Developer") ?? FindInScene("EXTRAS");
        if (extras != null) extras.SetActive(false);
    }

    GameObject FindInScene(string name)
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var r = FindDeep(root.transform, name);
            if (r != null) return r.gameObject;
        }
        return null;
    }

    UnityEngine.Transform FindDeep(UnityEngine.Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (UnityEngine.Transform child in parent)
        {
            var r = FindDeep(child, name);
            if (r != null) return r;
        }
        return null;
    }
}
