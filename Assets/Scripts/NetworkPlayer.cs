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

    public float HealthPct => _stats != null && _stats.maxHealth > 0f
        ? _stats.health / _stats.maxHealth : 1f;

    // ── Refs ─────────────────────────────────────────────────────────
    private PlayerStats    _stats;
    private PlayerControl  _control;
    private Rigidbody      _rb;
    private Transform      _playerChild; // the 'Player' child that actually moves

    // Child objects toggled per local/remote
    private GameObject _tpsPlayer;   // visible to others
    private GameObject _camera;      // local only
    private GameObject _playerUI;    // local only

    // ── Awake ────────────────────────────────────────────────────────

    void Awake()
    {
        _stats   = GetComponentInChildren<PlayerStats>();
        _control = GetComponentInChildren<PlayerControl>();
        _rb      = GetComponentInChildren<Rigidbody>();

        // 'Player' is the direct child that Cowsins moves via Rigidbody
        var t = transform.Find("Player");
        _playerChild = t != null ? t : transform;

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
        // Local player: show camera, hide TPS body
        if (_camera    != null) _camera.SetActive(true);
        if (_tpsPlayer != null) _tpsPlayer.SetActive(false);
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
            CmdSyncTransform(_playerChild.position, _playerChild.eulerAngles.y);
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

    [Command(requiresAuthority = false)]
    void CmdSyncTransform(Vector3 pos, float yRot)
    {
        _syncPos  = pos;
        _syncYRot = yRot;
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
