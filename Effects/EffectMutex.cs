namespace CrowdControl.Games.Packs.Effects
{
    // Mutexes used to avoid overlapping effects from running in parallel and undoing each other.
    public static class EffectMutex
    {
        public const string SetHealth = "setHealth";
        public const string SetShield = "setShield";
        public const string ShieldRegen = "shieldRegen";
        public const string SetGrenades = "grenades";
        public const string PlayerSpeed = "playerSpeed";
        public const string NPCSpeed = "npcSpeed";
        public const string PlayerReceivedDamage = "PlayerReceivedDamage";
        public const string NPCReceivedDamage = "NPCReceivedDamage";
        public const string ArmorLock = "armor lock";
        public const string ViewingControls = "ViewingControls";
        public const string AIBehaviour = "AIBehaviour";
        public const string Gravity = "gravity";
        public const string Size = "shieldRegen";
        public const string ObjectLightScale = "object_light_scale";
        public const string KeyDisable = "keysOverride";
        public const string KeyPress = "keyPress";
        public const string KeyChange = "keysOverride";
        public const string Ammo = "ammo";
        public const string MouseForcedMove = "mouseForcedMove";
        public const string LevelChangeOrRestart = "levelChangeOrRestart";
    }
}