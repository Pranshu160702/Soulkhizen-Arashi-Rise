using UnityEngine;
using UnityEngine.Animations.Rigging;
using cowsins;

public class SpineAimOffsetSync : MonoBehaviour
{
    [Header("Optional — assign if using custom PlayerController")]
    public PlayerController playerController;

    [Header("Optional — assign if using Cowsins PlayerMovement")]
    public Transform cowsinsCameraPivot; // the transform Cowsins rotates for pitch (parent of CameraPivot)

    public MultiAimConstraint spineAimConstraint;

    [Tooltip("The transform your MultiAimConstraint sources point at.")]
    public Transform aimTarget;

    [Tooltip("Camera used to cast the screen-center ray.")]
    public Camera aimCamera;

    private NetworkPlayer _ownerPlayer;

    void Start()
    {
        _ownerPlayer = GetComponentInParent<NetworkPlayer>();

        // Auto-find cowsins camera pivot if not assigned
        if (cowsinsCameraPivot == null && playerController == null)
        {
            var net = _ownerPlayer != null ? _ownerPlayer.transform : transform.root;
            var cameraPivot = FindDeep(net, "CameraPivot");
            if (cameraPivot != null)
                cowsinsCameraPivot = cameraPivot.parent;
        }
    }

    public void SetAimCamera(Camera cam) => aimCamera = cam;

    void LateUpdate()
    {
        if (_ownerPlayer == null || !_ownerPlayer.isLocalPlayer) return;
        if (spineAimConstraint == null) return;

        // Move aimTarget to screen-center look point
        if (aimTarget != null && aimCamera != null)
        {
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            aimTarget.position = ray.GetPoint(500f);
        }

        // Get vertical look angle from whichever controller is present
        float xRot = GetXRotation();
        float offsetX = Mathf.Lerp(75f, -75f, (Mathf.Clamp(xRot, -80f, 80f) + 80f) / 160f);
        var data = spineAimConstraint.data;
        data.offset = new Vector3(offsetX, data.offset.y, data.offset.z);
        spineAimConstraint.data = data;
    }

    float GetXRotation()
    {
        // Custom PlayerController path
        if (playerController != null)
            return playerController.xRotation;

        // Cowsins path — read pitch from camera pivot parent euler
        if (cowsinsCameraPivot != null)
        {
            float x = cowsinsCameraPivot.localEulerAngles.x;
            return x > 180f ? x - 360f : x;
        }

        // Fallback — read from the aim camera itself
        if (aimCamera != null)
        {
            float x = aimCamera.transform.localEulerAngles.x;
            return x > 180f ? x - 360f : x;
        }

        return 0f;
    }

    Transform FindDeep(Transform root, string boneName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }
}
