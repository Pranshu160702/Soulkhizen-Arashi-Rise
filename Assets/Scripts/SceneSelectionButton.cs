using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Button))]
public class SceneSelectionButton : MonoBehaviour
{
#if UNITY_EDITOR
    public SceneAsset sceneAsset;
#endif
    [HideInInspector] public string sceneName;
    public string displayName;

    void Start()
    {
        var btn = GetComponent<Button>();
        // Remove persistent (Inspector) and runtime listeners to prevent old scene loading
        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(() =>
            MenuController.Instance?.SelectGameMode(sceneName, displayName));
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SceneSelectionButton))]
public class SceneSelectionButtonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var btn = (SceneSelectionButton)target;

        EditorGUI.BeginChangeCheck();
        btn.sceneAsset = (SceneAsset)EditorGUILayout.ObjectField("Scene", btn.sceneAsset, typeof(SceneAsset), false);
        if (EditorGUI.EndChangeCheck() && btn.sceneAsset != null)
        {
            btn.sceneName = btn.sceneAsset.name;
            EditorUtility.SetDirty(btn);
        }

        btn.displayName = EditorGUILayout.TextField("Display Name", btn.displayName);
        EditorGUILayout.LabelField("Scene Name", btn.sceneName);
    }
}
#endif
