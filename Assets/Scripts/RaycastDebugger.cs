using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class RaycastDebugger : MonoBehaviour
{
    void Update()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        var results = new List<RaycastResult>();
        var data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        EventSystem.current.RaycastAll(data, results);

        if (results.Count == 0) { Debug.Log("[Raycast] Nothing hit"); return; }

        foreach (var r in results)
            Debug.Log($"[Raycast] Hit: '{r.gameObject.name}' on '{r.gameObject.transform.parent?.name}' depth:{r.depth} sortOrder:{r.sortingOrder}");
    }
}
