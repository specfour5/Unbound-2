using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// One weapon type. The battle's chosen def flows from the setup menu into
/// every Tank, and each fired Projectile reads its behavior from the def:
///   penetration    - how easily the shell pushes through terrain pixels and
///                    armor (vs their hardness values).
///   explosiveForce - damage dealt, and how hard a material it can break.
///   explosiveSize  - blast radius: how far the explosion travels.
/// The Basic Cannon reproduces the game's long-standing behavior.
/// </summary>
[System.Serializable]
public class WeaponDef
{
    public string id = "basic_cannon";
    public string displayName = "Basic Cannon";
    [TextArea] public string description = "Standard shell. Reliable against dirt and armor.";
    public bool available = true;

    [Header("Projectile")]
    [Tooltip("Vs terrain/armor hardness: higher burrows deeper and punches armor.")]
    public float penetration = 1f;
    [Tooltip("Damage dealt, and what hardness of terrain it can break.")]
    public float explosiveForce = 55f;
    [Tooltip("Blast radius: how far the explosion travels.")]
    public float explosiveSize = 4.5f;

    [Header("Firing")]
    [Tooltip("Fixed muzzle velocity: shot power is a function of the weapon, not a charge.")]
    public float muzzleVelocity = 22f;
    [Tooltip("Seconds between shots (used by the real-time mode).")]
    public float cooldown = 2.5f;

    [Header("Elevation")]
    [Tooltip("Max barrel elevation in degrees above the hull's front axis (vehicle-relative).")]
    public float maxElevation = 45f;
    [Tooltip("Min barrel elevation in degrees (negative = depression below the front axis).")]
    public float minElevation = -10f;
}

/// <summary>All known weapons. New types get an entry here and appear in the setup menu.</summary>
public static class WeaponCatalog
{
    public static readonly WeaponDef BasicCannon = new WeaponDef
    {
        id = "basic_cannon",
        displayName = "Basic Cannon",
        description = "Standard shell. Reliable against dirt and armor.",
        available = true,
        penetration = 1f,
        explosiveForce = 55f,
        explosiveSize = 4.5f,
        muzzleVelocity = 22f,
        cooldown = 2.5f,
        maxElevation = 45f,
        minElevation = -10f,
    };

    public static readonly List<WeaponDef> All = new List<WeaponDef> { BasicCannon };
}
