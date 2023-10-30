namespace CrowdControl.Games.Packs
{
    // Actions that the player can do in game.
    public enum GameAction
    {
        Jump,
        SwapGrenades,
        Use,
        Reload,
        SwapWeapons,
        Melee,
        FlashlightToggle,
        ThrowGrenade,
        Fire,
        Crouch,
        ZoomHold,

        //ZoomIn, // These two are kind of useless so I'm excluding them.
        //ZoomOut,
        RunForward,

        RunBackwards,
        StrafeLeft,
        StrafeRight,

        //ShowScore, // Unused in single player
        Pause
    }
}