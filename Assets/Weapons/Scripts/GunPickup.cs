using UnityEngine;
using cowsins;

/// Place on a world GameObject with a Collider (set Is Trigger = true).
/// Assign the Weapon_SO that this pickup grants.
public class GunPickup : MonoBehaviour
{
    [Tooltip("The Cowsins Weapon_SO scriptable object this pickup grants")]
    public Weapon_SO weapon;

    [Tooltip("Bonus reserve ammo added on pickup (0 = no ammo added)")]
    public int bonusReserveAmmo = 0;

    [Tooltip("Spin axis for the pickup prop")]
    public Vector3 spinAxis = Vector3.up;
    public float spinSpeed = 45f;

    void Update()
    {
        if (spinAxis != Vector3.zero)
            transform.Rotate(spinAxis * spinSpeed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        var np = other.GetComponentInParent<NetworkPlayer>();
        if (np == null || !np.isLocalPlayer) return;

        var wc = np.GetComponentInChildren<WeaponController>();
        if (wc == null || weapon == null) return;

        // Try to add to inventory; if full, swap with current weapon
        if (!wc.TryToAddWeapons(weapon, weapon.magazineSize, bonusReserveAmmo, null))
            wc.SwapWeapons(weapon, weapon.magazineSize, bonusReserveAmmo, null);

        gameObject.SetActive(false);
        Destroy(gameObject, 0.1f);
    }
}
