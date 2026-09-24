using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerCard : MonoBehaviour
{
    [Header("Visuals (auto-found)")]
    public Image bannerImage;
    public TextMeshProUGUI statusText;
    public Image statusBadge;
    public TextMeshProUGUI nameText;
    public Image rankImage;
    public TextMeshProUGUI rankText;

    [Header("Defaults")]
    public Sprite defaultBanner;
    public Sprite defaultRankIcon;
    public string defaultRankLabel = "UNRANKED";

    private static readonly Color ReadyColor       = new Color(0.29f, 0.85f, 0.29f, 1f);
    private static readonly Color UnavailableColor = new Color(0.5f,  0.5f,  0.5f,  1f);

    void Awake()
    {
        var banner  = transform.Find("Banner") ?? transform.Find("PlayerCard/Banner");
        bannerImage = banner?.GetComponent<Image>();
        statusBadge = banner?.Find("StatusBadge")?.GetComponent<Image>();
        statusText  = banner?.Find("StatusBadge/StatusText")?.GetComponent<TextMeshProUGUI>();
        nameText    = banner?.Find("NameBar/NameText")?.GetComponent<TextMeshProUGUI>();
        rankImage   = banner?.Find("RankPanel/Rank")?.GetComponent<Image>();
        rankText    = banner?.Find("RankPanel/RankText")?.GetComponent<TextMeshProUGUI>();
    }

    public void Initialize(string playerName, bool isReady = true,
        Sprite banner = null, Sprite rankIcon = null, string rankLabel = null)
    {
        if (nameText == null)
        {
            var b   = transform.Find("Banner");
            nameText    = b?.Find("NameBar/NameText")?.GetComponent<TextMeshProUGUI>();
            statusText  = b?.Find("StatusBadge/StatusText")?.GetComponent<TextMeshProUGUI>();
            statusBadge = b?.Find("StatusBadge")?.GetComponent<Image>();
            bannerImage = b?.GetComponent<Image>();
            rankImage   = b?.Find("RankPanel/Rank")?.GetComponent<Image>();
            rankText    = b?.Find("RankPanel/RankText")?.GetComponent<TextMeshProUGUI>();
        }

        if (nameText)    nameText.text = playerName;
        if (rankText)    rankText.text = rankLabel ?? defaultRankLabel;

        if (bannerImage) bannerImage.sprite = banner ?? defaultBanner;
        if (rankImage)   rankImage.sprite   = rankIcon ?? defaultRankIcon;

        SetStatus(isReady);
    }

    public void SetStatus(bool isReady)
    {
        if (statusText)  statusText.text   = isReady ? "READY" : "UNAVAILABLE";
        if (statusBadge) statusBadge.color = isReady ? ReadyColor : UnavailableColor;
    }
}
