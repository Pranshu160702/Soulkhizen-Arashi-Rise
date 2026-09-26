using System.Collections;
using UnityEngine;
using Mirror;
using cowsins;

public class NetworkPlayer : NetworkBehaviour
{
    // ── SyncVars ─────────────────────────────────────────────────────
    [SyncVar] public bool isDead = false;
    [SyncVar] public int  kills  = 0;

    [SyncVar] private Vector3 _syncPos;
    [SyncVar] private float   _syncYRot;
    [SyncVar] public  float   syncXRot; // vertical look (pitch) for TPS upper body aim
    [SyncVar] public  float   syncYRot; // horizontal look (yaw) for TPS body rotation

    // Animation state synced to TPS body on remote clients
    [SyncVar] public float syncAnimH;
    [SyncVar] public float syncAnimV;
    [SyncVar] public bool  syncIsRunning;
    [SyncVar] public bool  syncIsWalking;
    [SyncVar] public bool  syncIsIdle;
    [SyncVar] public bool  syncIsJumping;
    [SyncVar] public bool  syncIsAirborne;
    [SyncVar] public bool  syncIsCrouching;
    [SyncVar] public bool  syncIsCrouchWalking;
    [SyncVar] public bool  syncIsAiming;
    [SyncVar] public bool  syncIsFiring;
    [SyncVar] public bool  syncIsReloading;
    [SyncVar] public bool  syncIsReloadingEmpty;
    [SyncVar] public bool  syncIsEquipping;
    [SyncVar] public bool  syncIsMelee1;
    [SyncVar] public bool  syncIsMelee2;
    [SyncVar] public bool  syncIsMelee3;
    [SyncVar] public bool  syncIsMelee4;

    public float HealthPct => _stats != null && _stats.maxHealth > 0f
        ? _stats.health / _stats.maxHealth : 1f;

    // ── Refs ─────────────────────────────────────────────────────────
    private PlayerStats    _stats;
    private PlayerControl  _control;
    private Rigidbody      _rb;
    private Transform      _playerChild; // the 'Player' child that actually moves
    private PlayerController _playerController; // optional — only present on custom prefabs

    // Cowsins camera pivot for xRotation (vertical look)
    private Transform _cameraPivot;

    // Child objects toggled per local/remote
    private GameObject _tpsPlayer;   // visible to others
    private GameObject _camera;      // local only
    private GameObject _playerUI;    // local only

    // Local anim state derived from velocity (used when PlayerController is absent)
    private float _smoothH;
    private float _smoothV;
    private float _smoothHVel;
    private float _smoothVVel;
    private bool  _wasGrounded = true;

    // ── Awake ────────────────────────────────────────────────────────

    void Awake()
    {
        _stats            = GetComponentInChildren<PlayerStats>();
        _control          = GetComponentInChildren<PlayerControl>();
        _rb               = GetComponentInChildren<Rigidbody>();
        _playerController = GetComponentInChildren<PlayerController>();

        // 'Player' is the direct child that Cowsins moves via Rigidbody
        var t = transform.Find("Player");
        _playerChild = t != null ? t : transform;

        // Find Cowsins camera object (parent of CameraPivot) for yaw+pitch
        var cameraPivotT = FindInChildren("CameraPivot");
        _cameraPivot = cameraPivotT != null ? cameraPivotT.transform.parent : null;

        // Find by name in hierarchy
        _tpsPlayer = FindInChildren("TPSPlayer");
        _camera    = FindInChildren("Camera");
        _playerUI  = FindInChildren("PlayerUI");

        // Disable everything by default — OnStartLocalPlayer / OnStartClient will configure
        foreach (var cam in GetComponentsInChildren<Camera>(true))
            cam.enabled = false;
        foreach (var al in GetComponentsInChildren<AudioListener>(true))
            al.enabled = false;

        _control?.LoseControl();
    }

    // ── Mirror ───────────────────────────────────────────────────────

    public override void OnStartLocalPlayer()
    {
        // Local player: show camera, show TPS body (testing)
        if (_camera    != null) _camera.SetActive(true);
        if (_playerUI  != null) _playerUI.SetActive(true);

        foreach (var cam in GetComponentsInChildren<Camera>(true))
            cam.enabled = true;
        foreach (var al in GetComponentsInChildren<AudioListener>(true))
            al.enabled = true;

        _control?.GrantControl();

        if (GameHUD.instance != null)
            GameHUD.instance.networkPlayer = this;

        _stats?.AddOnDieListener(OnLocalDied);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (isLocalPlayer) return;

        // Remote player: hide camera, show TPS body
        if (_camera    != null) _camera.SetActive(false);
        if (_tpsPlayer != null) _tpsPlayer.SetActive(true);
        if (_playerUI  != null) _playerUI.SetActive(false);

        _control?.LoseControl();
    }

    // ── Update ───────────────────────────────────────────────────────

    void Update()
    {
        if (isLocalPlayer)
        {
            // Cowsins never rotates Player — yaw lives in the camera object
            float yaw = _cameraPivot != null ? _cameraPivot.localEulerAngles.y : _playerChild.eulerAngles.y;
            CmdSyncTransform(_playerChild.position, yaw);
            SyncAnimState();
        }
        else
            ApplyRemoteTransform();
    }

    void ApplyRemoteTransform()
    {
        if (_playerChild == null) return;
        _playerChild.position = Vector3.Lerp(_playerChild.position, _syncPos, Time.deltaTime * 15f);
        _playerChild.rotation = Quaternion.Lerp(
            _playerChild.rotation,
            Quaternion.Euler(0f, _syncYRot, 0f),
            Time.deltaTime * 15f);
    }

    void SyncAnimState()
    {
        float h, v, xRot;
        bool running, walking, idle, jumping, airborne, crouching, crouchWalking,
             aiming, firing, reloading, reloadEmpty, equipping,
             melee1, melee2, melee3, melee4;

        if (_playerController != null)
        {
            // Custom PlayerController path
            h            = _playerController.animHorizontal;
            v            = _playerController.animVertical;
            running      = _playerController.animIsRunning;
            walking      = _playerController.animIsWalking;
            idle         = _playerController.animIsIdle;
            jumping      = _playerController.animIsJumping;
            airborne     = _playerController.animIsAirborne;
            crouching    = _playerController.animIsCrouching;
            crouchWalking = _playerController.animIsCrouchWalking;
            aiming       = _playerController.animIsAiming;
            firing       = _playerController.animIsFiring;
            reloading    = _playerController.animIsReloading;
            reloadEmpty  = _playerController.animIsReloadingEmpty;
            equipping    = _playerController.animIsEquipping;
            melee1       = _playerController.animIsMelee1;
            melee2       = _playerController.animIsMelee2;
            melee3       = _playerController.animIsMelee3;
            melee4       = _playerController.animIsMelee4;
            xRot         = _playerController.xRotation;
        }
        else
        {
            // Cowsins path — derive state from Rigidbody velocity
            Vector3 vel    = _rb != null ? _rb.linearVelocity : Vector3.zero;
            Vector3 localV = _playerChild.InverseTransformDirection(vel);
            float   speed  = new Vector2(localV.x, localV.z).magnitude;
            bool    grounded = _rb != null && Mathf.Abs(vel.y) < 0.5f;

            float targetH = speed > 0.1f ? Mathf.Clamp(localV.x / Mathf.Max(speed, 0.01f), -1f, 1f) : 0f;
            float targetV = speed > 0.1f ? Mathf.Clamp(localV.z / Mathf.Max(speed, 0.01f), -1f, 1f) : 0f;
            _smoothH = Mathf.SmoothDamp(_smoothH, targetH, ref _smoothHVel, 0.1f);
            _smoothV = Mathf.SmoothDamp(_smoothV, targetV, ref _smoothVVel, 0.1f);

            h            = _smoothH;
            v            = _smoothV;
            running      = speed > 10f && grounded;
            walking      = speed > 0.5f && speed <= 10f && grounded;
            idle         = speed < 0.5f && grounded;
            airborne     = !grounded;
            jumping      = !grounded && !_wasGrounded == false; // rising
            crouching    = false; // Cowsins handles crouch internally
            crouchWalking = false;
            aiming       = false;
            firing       = false;
            reloading    = false;
            reloadEmpty  = false;
            equipping    = false;
            melee1 = melee2 = melee3 = melee4 = false;

            // Vertical look: pitch from camera X, yaw from camera Y
            xRot = _cameraPivot != null ? _cameraPivot.localEulerAngles.x : 0f;
            if (xRot > 180f) xRot -= 360f;

            _wasGrounded = grounded;
        }

        float yawRot = _cameraPivot != null ? _cameraPivot.localEulerAngles.y : _playerChild.eulerAngles.y;

        CmdSyncAnim(h, v, running, walking, idle, jumping, airborne,
                    crouching, crouchWalking, aiming, firing, reloading,
                    reloadEmpty, equipping, melee1, melee2, melee3, melee4, xRot, yawRot);
    }

    [Command(requiresAuthority = false)]
    void CmdSyncTransform(Vector3 pos, float yRot)
    {
        _syncPos  = pos;
        _syncYRot = yRot;
    }

    [Command(requiresAuthority = false)]
    void CmdSyncAnim(float h, float v, bool running, bool walking, bool idle,
                     bool jumping, bool airborne, bool crouching, bool crouchWalking,
                     bool aiming, bool firing, bool reloading,
                     bool reloadingEmpty, bool equipping,
                     bool melee1, bool melee2, bool melee3, bool melee4,
                     float xRot, float yRot)
    {
        syncAnimH           = h;
        syncAnimV           = v;
        syncIsRunning       = running;
        syncIsWalking       = walking;
        syncIsIdle          = idle;
        syncIsJumping       = jumping;
        syncIsAirborne      = airborne;
        syncIsCrouching     = crouching;
        syncIsCrouchWalking = crouchWalking;
        syncIsAiming        = aiming;
        syncIsFiring        = firing;
        syncIsReloading     = reloading;
        syncIsReloadingEmpty = reloadingEmpty;
        syncIsEquipping     = equipping;
        syncIsMelee1        = melee1;
        syncIsMelee2        = melee2;
        syncIsMelee3        = melee3;
        syncIsMelee4        = melee4;
        syncXRot            = xRot;
        syncYRot            = yRot;
    }

    // ── Death / Respawn ──────────────────────────────────────────────

    void OnLocalDied()
    {
        if (!isDead) CmdDied();
    }

    [Command]
    void CmdDied()
    {
        if (isDead) return;
        isDead = true;
        RpcOnDied(GameNetworkManager.LocalPlayerName, gameObject.name);
    }

    [Server]
    public void TakeDamage(float damage, string killerName = "", uint killerNetId = 0)
    {
        if (isDead) return;
        _stats?.Damage(damage, false);
        if (_stats != null && _stats.IsDead && !isDead)
        {
            isDead = true;
            if (killerNetId != 0 && NetworkServer.spawned.TryGetValue(killerNetId, out var id))
            {
                var killer = id.GetComponent<NetworkPlayer>();
                if (killer != null)
                {
                    killer.kills++;
                    GameManager.instance?.RegisterKill(killerName, killer.kills);
                }
            }
            RpcOnDied(killerName, gameObject.name);
        }
    }

    [ClientRpc]
    void RpcOnDied(string killer, string victim)
    {
        GameHUD.AddKillFeed(killer, victim);
        if (!isLocalPlayer) return;
        _control?.LoseControl();
        GameHUD.instance?.ShowRespawnScreen(5f);
        StartCoroutine(RespawnAfter(5f));
    }

    IEnumerator RespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        CmdRespawn();
    }

    [Command]
    void CmdRespawn()
    {
        var spawn = SpawnManager.instance != null ? SpawnManager.instance.GetBestSpawn() : null;
        var pos   = spawn != null ? spawn.position : Vector3.up * 2f;
        isDead    = false;
        _stats?.Respawn(pos);
        RpcRespawned(pos);
    }

    [ClientRpc]
    void RpcRespawned(Vector3 pos)
    {
        if (_rb != null) { _rb.position = pos; _rb.linearVelocity = Vector3.zero; }
        else _playerChild.position = pos;
        if (!isLocalPlayer) return;
        _control?.GrantControl();
        GameHUD.instance?.HideRespawnScreen();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    GameObject FindInChildren(string childName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t.gameObject;
        return null;
    }
}
