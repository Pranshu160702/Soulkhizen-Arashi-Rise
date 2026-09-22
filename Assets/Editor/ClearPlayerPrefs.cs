#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class ClearPlayerPrefs
{
    [MenuItem("SOULKHIZEN/Clear PlayerPrefs")]
    static void Clear()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[SOULKHIZEN] PlayerPrefs cleared.");
    }
}
#endif
