using UnityEngine;
using Mirror;
using cowsins;

public class TPSAnimatorDriver : MonoBehaviour
{
    private Animator          _animator;
    private NetworkPlayer     _net;
    private PlayerMovement    _playerMovement;
    private PlayerController  _playerController;
    private Rigidbody         _rb;
    private Transform         _playerChild;   // 'Player' GO — never rotates in Cowsins
    private Transform         _cowsinsCamera; // the GO Cowsins rotates for yaw+pitch (parent of CameraPivot)

    // Spine bones for vertical aim
    private Transform _spine;
    private Transform _spine1;
    private Transform _spine2;
    private Transform _neck;
    private Transform _head;

    private float _smoothXRot;
    private float _smoothYRot;
    private bool  _wasJumping;
    private bool  _wasGrounded = true;

    private static readonly int H                = Animator.StringToHash("Horizontal");
    private static readonly int V                = Animator.StringToHash("Vertical");
    private static readonly int IsRunning        = Animator.StringToHash("isRunning");
    private static readonly int IsWalking        = Animator.StringToHash("isWalking");
    private static readonly int IsIdle           = Animator.StringToHash("isIdle");
    private static readonly int IsJumping        = Animator.StringToHash("isJumping"); // Trigger
    private static readonly int IsAirborne       = Animator.StringToHash("isAirborne");
    private static readonly int IsCrouching      = Animator.StringToHash("isCrouching");
    private static readonly int IsCrouchWalking  = Animator.StringToHash("isCrouchWalking");
    private static readonly int IsFiring         = Animator.StringToHash("isFiring");
    private static readonly int IsReloading      = Animator.StringToHash("isReloading");
    private static readonly int IsReloadingEmpty = Animator.StringToHash("isReloadingEmpty");
    private static readonly int IsEquipping      = Animator.StringToHash("isEquipping");
    private static readonly int IsMelee1         = Animator.StringToHash("isMelee1");
    private static readonly int IsMelee2         = Animator.StringToHash("isMelee2");
    private static readonly int IsMelee3         = Animator.StringToHash("isMelee3");
    private static readonly int IsMelee4         = Animator.StringToHash("isMelee4");
    private static readonly int IsDead           = Animator.StringToHash("isDead");

    void Start()
    {
        _animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        // Find NetworkPlayer in parents
        Transform t = transform.parent;
        while (t != null && _net == null)
        {
            _net = t.GetComponent<NetworkPlayer>();
            t = t.parent;
        }

        if (_net == null)  { Debug.LogError("[TPSAnimatorDriver] No NetworkPlayer found."); return; }
        if (_animator == null) { Debug.LogError("[TPSAnimatorDriver] No Animator found."); return; }

        Debug.Log("[TPSAnimatorDriver] Ready | animator=" + _animator.name);

        // Force animator into Idle immediately so it never starts in a wrong state
        _animator.SetBool(IsIdle,          true);
        _animator.SetBool(IsRunning,       false);
        _animator.SetBool(IsWalking,       false);
        _animator.SetBool(IsAirborne,      false);
        _animator.SetBool(IsCrouching,     false);
        _animator.SetBool(IsCrouchWalking, false);
        _animator.SetFloat(H,              0f);
        _animator.SetFloat(V,              0f);

        _playerMovement   = _net.GetComponentInChildren<PlayerMovement>();
        _playerController = _net.GetComponentInChildren<PlayerController>();
        _rb               = _net.GetComponentInChildren<Rigidbody>();

        var pc = _net.transform.Find("Player");
        _playerChild = pc != null ? pc : _net.transform;

        // Cowsins camera = parent of CameraPivot (the GO that rotates for yaw+pitch)
        var cameraPivot = FindDeep(_net.transform, "CameraPivot");
        _cowsinsCamera  = cameraPivot != null ? cameraPivot.parent : null;

        _spine  = FindDeep(transform, "mixamorig:Spine");
        _spine1 = FindDeep(transform, "mixamorig:Spine1");
        _spine2 = FindDeep(transform, "mixamorig:Spine2");
        _neck   = FindDeep(transform, "mixamorig:Neck");
        _head   = FindDeep(transform, "mixamorig:Head");

        // Wire SpineAimOffsetSync if present
        var aimSync = GetComponentInChildren<SpineAimOffsetSync>(true);
        if (aimSync != null)
        {
            var aimCam = _net.GetComponentInChildren<Camera>(true);
            if (aimCam != null) aimSync.SetAimCamera(aimCam);
            // PlayerController path
            var aimPc = _net.GetComponentInChildren<PlayerController>();
            if (aimPc != null) aimSync.playerController = aimPc;
            // Cowsins path — pass camera pivot parent
            if (aimPc == null && _cowsinsCamera != null)
                aimSync.cowsinsCameraPivot = _cowsinsCamera;
        }
    }

    Transform FindDeep(Transform root, string boneName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    void Update()
    {
        if (_net == null || _animator == null) return;

        float h, v, xRot, yRot;
        bool running, walking, idle, jumping, airborne, crouching, crouchWalking,
             firing, reloading, reloadEmpty, equipping,
             melee1, melee2, melee3, melee4;

        if (_net.isLocalPlayer && _playerController != null)
        {
            // ── Local player: read directly from PlayerController ──
            h             = _playerController.animHorizontal;
            v             = _playerController.animVertical;
            running       = _playerController.animIsRunning;
            walking       = _playerController.animIsWalking;
            idle          = _playerController.animIsIdle;
            jumping       = _playerController.animIsJumping;
            airborne      = _playerController.animIsAirborne;
            crouching     = _playerController.animIsCrouching;
            crouchWalking = _playerController.animIsCrouchWalking;
            firing        = _playerController.animIsFiring;
            reloading     = _playerController.animIsReloading;
            reloadEmpty   = _playerController.animIsReloadingEmpty;
            equipping     = _playerController.animIsEquipping;
            melee1        = _playerController.animIsMelee1;
            melee2        = _playerController.animIsMelee2;
            melee3        = _playerController.animIsMelee3;
            melee4        = _playerController.animIsMelee4;
            xRot          = _playerController.xRotation;
            yRot          = _playerChild.eulerAngles.y;
        }
        else if (_net.isLocalPlayer && _playerMovement != null)
        {
            // ── Local player: read directly from Cowsins PlayerMovement ──
            float speed   = _playerMovement.CurrentSpeed;
            bool grounded = _playerMovement.Grounded;

            Vector3 localVel = _playerChild.InverseTransformDirection(_rb.linearVelocity);
            float   mag      = new Vector2(localVel.x, localVel.z).magnitude;
            h = mag > 0.1f ? Mathf.Clamp(localVel.x / Mathf.Max(mag, 0.01f), -1f, 1f) : 0f;
            v = mag > 0.1f ? Mathf.Clamp(localVel.z / Mathf.Max(mag, 0.01f), -1f, 1f) : 0f;

            bool isCrouching = _playerMovement.IsCrouching;
            running      = speed >= _playerMovement.RunSpeed  * 0.8f && grounded && !isCrouching;
            walking      = speed >= _playerMovement.WalkSpeed * 0.5f && speed < _playerMovement.RunSpeed * 0.8f && grounded && !isCrouching;
            idle         = _playerMovement.IsIdle && grounded;
            airborne     = !grounded;
            jumping      = !grounded && _wasGrounded;
            crouching    = isCrouching && !_playerMovement.IsSliding;
            crouchWalking = isCrouching && speed > 0.1f;
            firing = reloading = reloadEmpty = equipping = false;
            melee1 = melee2 = melee3 = melee4 = false;

            if (_cowsinsCamera != null)
            {
                Vector3 euler = _cowsinsCamera.localEulerAngles;
                yRot = euler.y;
                xRot = euler.x > 180f ? euler.x - 360f : euler.x;
            }
            else { yRot = 0f; xRot = 0f; }

            _wasGrounded = grounded;
        }
        else
        {
            // ── Remote player: read from SyncVars ──
            h             = _net.syncAnimH;
            v             = _net.syncAnimV;
            running       = _net.syncIsRunning;
            walking       = _net.syncIsWalking;
            idle          = _net.syncIsIdle;
            jumping       = _net.syncIsJumping;
            airborne      = _net.syncIsAirborne;
            crouching     = _net.syncIsCrouching;
            crouchWalking = _net.syncIsCrouchWalking;
            firing        = _net.syncIsFiring;
            reloading     = _net.syncIsReloading;
            reloadEmpty   = _net.syncIsReloadingEmpty;
            equipping     = _net.syncIsEquipping;
            melee1        = _net.syncIsMelee1;
            melee2        = _net.syncIsMelee2;
            melee3        = _net.syncIsMelee3;
            melee4        = _net.syncIsMelee4;
            xRot          = _net.syncXRot;
            yRot          = _net.syncYRot;
        }

        // ── Debug ──
        if (_net.isLocalPlayer && _playerController != null)
            Debug.Log($"[TPS] idle={idle} walk={walking} run={running} air={airborne} crouch={crouching}");

        // ── Drive animator ──
        _animator.SetFloat(H,                h);
        _animator.SetFloat(V,                v);
        _animator.SetBool(IsRunning,         running);
        _animator.SetBool(IsWalking,         walking);
        _animator.SetBool(IsIdle,            idle);
        _animator.SetBool(IsAirborne,        airborne);
        _animator.SetBool(IsCrouching,       crouching);
        _animator.SetBool(IsCrouchWalking,   crouchWalking);
        _animator.SetBool(IsFiring,          firing);
        _animator.SetBool(IsReloading,       reloading);
        _animator.SetBool(IsReloadingEmpty,  reloadEmpty);
        _animator.SetBool(IsEquipping,       equipping);
        _animator.SetBool(IsMelee1,          melee1);
        _animator.SetBool(IsMelee2,          melee2);
        _animator.SetBool(IsMelee3,          melee3);
        _animator.SetBool(IsMelee4,          melee4);
        _animator.SetBool(IsDead,            _net.isDead);

        if (jumping && !_wasJumping)
            _animator.SetTrigger(IsJumping);
        _wasJumping = jumping;

        // ── Smooth angles ──
        _smoothXRot = Mathf.LerpAngle(_smoothXRot, xRot, Time.deltaTime * 20f);
        _smoothYRot = Mathf.LerpAngle(_smoothYRot, yRot, Time.deltaTime * 20f);

        // ── Rotate TPSPlayer to match player body yaw ──
        if (_playerController != null)
            transform.localRotation = Quaternion.Euler(0f, _smoothYRot + 45f, 0f);
        else
            transform.localRotation = Quaternion.Euler(0f, _smoothYRot + 45f, 0f);
    }

    void LateUpdate()
    {
        // Spine aim is handled by SpineAimOffsetSync + MultiAimConstraint (Animation Rigging)
        // Manual bone rotation removed to avoid conflict
    }
}
