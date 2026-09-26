using UnityEngine;
using UnityEditor;

public class BuildCombinedPlayerPrefab
{
    [MenuItem("Tools/Build Combined FPS+TPS Player Prefab")]
    static void Build()
    {
        // ── Load assets ──────────────────────────────────────────────
        var fpsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Cowsins/Prefabs/PlayerControllers/Networked_Player.prefab");
        var commandoFbx = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/DSRVD/Models/Male.fbx");
        var commandoAnimator = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/Characters/Commando/Animator/CommandoAnimator.controller");

        if (fpsPrefab == null)
        { Debug.LogError("[BuildCombinedPlayer] Networked_Player.prefab not found."); return; }
        if (commandoFbx == null)
        { Debug.LogError("[BuildCombinedPlayer] Male.fbx not found."); return; }
        if (commandoAnimator == null)
        { Debug.LogError("[BuildCombinedPlayer] CommandoAnimator.controller not found."); return; }

        // ── Instantiate FPS prefab into scene ────────────────────────
        var root = (GameObject)PrefabUtility.InstantiatePrefab(fpsPrefab);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // ── Find and destroy the old capsule TPSPlayer ───────────────
        var oldTPS = FindDeep(root.transform, "TPSPlayer");
        if (oldTPS != null)
        {
            Debug.Log("[BuildCombinedPlayer] Removing old TPSPlayer capsule.");
            Object.DestroyImmediate(oldTPS.gameObject);
        }

        // ── Find the 'Player' child (Cowsins moves this) ─────────────
        // Cowsins root structure: Networked_Player > Player > (camera, arms, etc.)
        // TPSPlayer should be a sibling of Camera inside Player
        Transform playerChild = root.transform.Find("Player");
        Transform tpsParent   = playerChild != null ? playerChild : root.transform;

        // ── Instantiate Commando mesh as TPSPlayer ───────────────────
        var commandoGO = (GameObject)PrefabUtility.InstantiatePrefab(commandoFbx);
        commandoGO.name = "TPSPlayer";
        commandoGO.transform.SetParent(tpsParent, false);
        commandoGO.transform.localPosition = Vector3.zero;
        commandoGO.transform.localRotation = Quaternion.identity;
        commandoGO.transform.localScale    = Vector3.one;

        // Set layer to Default (0) so FPS weapon camera doesn't render it
        SetLayerRecursive(commandoGO, 0);

        // ── Assign animator controller ───────────────────────────────
        var anim = commandoGO.GetComponentInChildren<Animator>();
        if (anim == null)
        {
            // Commando.fbx root may not have Animator — add one
            anim = commandoGO.AddComponent<Animator>();
        }
        anim.runtimeAnimatorController = commandoAnimator;
        anim.applyRootMotion = false;

        // ── Add TPSAnimatorDriver ────────────────────────────────────
        commandoGO.AddComponent<TPSAnimatorDriver>();

        // ── Disable TPSPlayer by default (NetworkPlayer.Awake hides it) ─
        commandoGO.SetActive(false);

        // ── Save as new prefab ───────────────────────────────────────
        if (!AssetDatabase.IsValidFolder("Assets/DSRVD"))
            AssetDatabase.CreateFolder("Assets", "DSRVD");
        if (!AssetDatabase.IsValidFolder("Assets/DSRVD/Prefabs"))
            AssetDatabase.CreateFolder("Assets/DSRVD", "Prefabs");

        const string outPath = "Assets/DSRVD/Prefabs/Networked_Player_Combined.prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(root, outPath);
        Object.DestroyImmediate(root);

        if (saved != null)
            Debug.Log("[BuildCombinedPlayer] Saved to " + outPath);
        else
            Debug.LogError("[BuildCombinedPlayer] Failed to save prefab.");

        AssetDatabase.Refresh();
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }
}
