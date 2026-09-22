using UnityEngine;
using UnityEngine.EventSystems;

/// Attach to FriendsPanel. Smoothly expands on hover, collapses when mouse leaves.
public class FriendsPanelHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public float collapsedWidth = 52f;
    public float expandedWidth  = 260f;
    public float animSpeed      = 8f;

    float _targetWidth;
    RectTransform _rt;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _targetWidth = collapsedWidth;
        SetWidth(collapsedWidth);
    }

    void Update()
    {
        float current = -_rt.offsetMin.x;
        float next = Mathf.Lerp(current, _targetWidth, Time.deltaTime * animSpeed);
        SetWidth(next);
    }

    public void OnPointerEnter(PointerEventData _) => _targetWidth = expandedWidth;
    public void OnPointerExit(PointerEventData _)  => _targetWidth = collapsedWidth;

    void SetWidth(float w) => _rt.offsetMin = new Vector2(-w, _rt.offsetMin.y);
}
