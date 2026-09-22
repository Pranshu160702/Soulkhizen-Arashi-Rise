using UnityEngine;

/// <summary>
/// Attach to PlayerCard_0 through PlayerCard_4.
/// Manages spawning/destroying the PlayerCard prefab inside PlayerCardSlot.
/// </summary>
public class PartySlot : MonoBehaviour
{
    [Tooltip("The PlayerCardSlot transform — card prefab spawns inside this")]
    public Transform cardSlot;

    [Tooltip("The + icon shown when empty — null for slot 0 (local player always present)")]
    public GameObject plusIcon;

    [HideInInspector] public GameObject playerCardPrefab; // set by MenuController

    private PlayerCard _activeCard;
    private string _occupiedName;
    public bool IsOccupied => _activeCard != null;
    public string OccupiedName => _occupiedName;

    public PlayerCard SpawnCard(string playerName, bool isReady = true,
        Sprite banner = null, Sprite rankIcon = null,
        string rankLabel = null)
    {
        // Destroy existing card first to avoid duplicates
        if (_activeCard != null) DestroyCard();

        if (playerCardPrefab == null || cardSlot == null) return null;

        var go = Instantiate(playerCardPrefab, cardSlot);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        _activeCard = go.GetComponent<PlayerCard>();
        _activeCard?.Initialize(playerName, isReady, banner, rankIcon, rankLabel);
        _occupiedName = playerName;

        if (plusIcon) plusIcon.SetActive(false);
        return _activeCard;
    }

    public void DestroyCard()
    {
        if (_activeCard != null)
        {
            Destroy(_activeCard.gameObject);
            _activeCard = null;
        }
        _occupiedName = null;
        if (plusIcon) plusIcon.SetActive(true);
    }

    public void SetStatus(bool isReady) => _activeCard?.SetStatus(isReady);
}
