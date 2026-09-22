using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to the PlayerCard prefab root.
/// Auto-finds all child references by name.
/// </summary>
public class PlayerCard : MonoBehaviour
{
    [Header("Visuals (auto-found)")]
    public Image bannerImage;           // Banner itself
    public TextMeshProUGUI statusText;  // Banner > StatusBadge > StatusText
    public Image statusBadge;           // Banner > StatusBadge
    public TextMeshProUGUI nameText;    // Banner > NameBar > NameText
    public Image rankImage;             // Banner > RankPanel > Rank
    public TextMeshProUGUI rankText;    // Banner > RankPanel > RankText

    [Header("Defaults")]
    public Sprite defaultBanner;
    public Sprite defaultRankIcon;
    public string defaultRankLabel = "UNRANKED";

    private static readonly Color ReadyColor       = new Color(0.29f, 0.85f, 0.29f, 1f);
    private static readonly Color UnavailableColor = new Color(0.5f,  0.5f,  0.5f,  1f);

    void Awake()
    {
        Debug.Log($"[PlayerCard] Awake on '{gameObject.name}', children: {transform.childCount}");
        for (int i = 0; i < transform.childCount; i++)
            Debug.Log($"[PlayerCard] child[{i}] = '{transform.GetChild(i).name}'");

        // Try direct child first, then one level deeper (in case component is on a parent)
        var banner = transform.Find("Banner") 
                  ?? transform.Find("PlayerCard/Banner");

        bannerImage = banner?.GetComponent<Image>();
        statusBadge = banner?.Find("StatusBadge")?.GetComponent<Image>();
        statusText  = banner?.Find("StatusBadge/StatusText")?.GetComponent<TextMeshProUGUI>();
        nameText    = banner?.Find("NameBar/NameText")?.GetComponent<TextMeshProUGUI>();
        rankImage   = banner?.Find("RankPanel/Rank")?.GetComponent<Image>();
        rankText    = banner?.Find("RankPanel/RankText")?.GetComponent<TextMeshProUGUI>();

        Debug.Log($"[PlayerCard] banner:{banner != null} name:{nameText != null} status:{statusText != null} rank:{rankText != null}");
    }

    public void Initialize(string playerName, bool isReady = true,
        Sprite banner = null, Sprite rankIcon = null, string rankLabel = null)
    {
        // Re-find in case Awake hasn't run yet
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

        if (nameText)    nameText.text       = playerName;
        if (rankText)    rankText.text       = rankLabel ?? defaultRankLabel;
        if (bannerImage && banner != null)   bannerImage.sprite = banner;
        else if (bannerImage && defaultBanner != null) bannerImage.sprite = defaultBanner;
        if (rankImage && rankIcon != null)   rankImage.sprite   = rankIcon;
        else if (rankImage && defaultRankIcon != null) rankImage.sprite   = defaultRankIcon;

        SetStatus(isReady);
        Debug.Log($"[PlayerCard] Initialize — name:{playerName} nameText:{nameText != null}");
    }

    public void SetStatus(bool isReady)
    {
        if (statusText)  statusText.text  = isReady ? "READY" : "UNAVAILABLE";
        if (statusBadge) statusBadge.color = isReady ? ReadyColor : UnavailableColor;
    }
}
