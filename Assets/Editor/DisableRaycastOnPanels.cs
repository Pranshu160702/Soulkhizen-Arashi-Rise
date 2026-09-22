#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public class DisableRaycastOnPanels
{
    [MenuItem("SOULKHIZEN/Disable Raycast Target on Selected")]
    static void DisableRaycast()
    {
        foreach (var go in Selection.gameObjects)
        {
            var images = go.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                // Only disable on objects that have no Button component
                if (img.GetComponent<Button>() == null && img.GetComponentInParent<Button>() == null)
                {
                    Undo.RecordObject(img, "Disable Raycast Target");
                    img.raycastTarget = false;
                }
            }
        }
        Debug.Log("[SOULKHIZEN] Raycast Target disabled on all non-button Images in selection.");
    }
}
#endif
