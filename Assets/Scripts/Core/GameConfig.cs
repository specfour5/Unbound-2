using System.Collections.Generic;

/// <summary>
/// Carries setup-menu choices from the duel scene into the side-scroller scene.
/// </summary>
public static class GameConfig
{
    /// <summary>
    /// One weapon id per Tank.HardpointLayout entry (null/empty = empty slot).
    /// </summary>
    public static List<string> loadoutWeaponIds;
    public static float windMultiplier = 1f;
}
