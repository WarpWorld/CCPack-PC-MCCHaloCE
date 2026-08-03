#define DEVELOPMENT

using CrowdControl.Common;

namespace CrowdControl.Games.Packs.MCCHaloCE.Effects;

// Lists every effect for the Effect Pack.
public static class CursedHaloEffectList
{
    public const string PositiveIncentive = "Helpful incentives";
    public const string NegativeIncentive = "Hurtful incentives";
    public const string NeutralIncentive = "Chaotic Neutral incentives";


    private const float PRICINGFACTOR = 0.1f;

    public static EffectList Effects = new List<Effect> {
//#if DEVELOPMENT
//            new("Abort all injection", "abortallinjection")
//            {
//                Description = "Dev only. Use before reloading the effect pack to reset any modified memory and prevent any further" +
//                "memory modification before the reload."
//, Price = (uint) (* PRICINGFACTOR )},
//            new("Test duration", "testdurationparam"){Duration = TimeSpan.FromMilliseconds(9431), Description = "Dev only, verify that the code and duration are passed properly.", Price = (uint) (* PRICINGFACTOR )},
//            new("Test multieffect", "testmultieffect"){Duration = TimeSpan.FromMilliseconds(9431), Description = "Dev only, verify that multiple one-shot effects do not overwrite each other.", Price = (uint) (* PRICINGFACTOR )},
//#endif


        new("Take half of the current weapon ammo", "takeammo_half") {Category = NegativeIncentive,
            Description = "Yoink half of the ammo/battery of the currently held weapon.", Price = (uint) (500 * PRICINGFACTOR /*, ScaleFactor = 1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Take all of the current weapon ammo", "takeammo_all") {Category = NegativeIncentive,
            Description = "Yoink all of the ammo/battery of the currently held weapon.", Price = (uint) (2200 * PRICINGFACTOR )},
        new("Duplicate current ammo", "takeammo_duplicate") {Category = PositiveIncentive,
            Description = "Duplicate the ammo of the currently held weapon. Goes over any cap, including clip size.", Price = (uint) (500 * PRICINGFACTOR )},
        new("Fuller auto", "fullauto_limitedammo") {Category = NeutralIncentive, Duration = 15,
            Description = "Make the player fire non-stop.", Price = (uint) (1500 * PRICINGFACTOR )},
        new("Fullest auto", "fullauto_unlimitedammo") {Category = NeutralIncentive, Duration = 5,
            Description = "Make the player fire non-stop with infinite ammo and no time between shots.", Price = (uint) (650 * PRICINGFACTOR )},
        new("One health and a dream.", "criticalhealth") { Category = NegativeIncentive, Duration = 10,
            Description = "Momentarily reduces health to 0.0000000001.", Price = (uint) (300 * PRICINGFACTOR )},
        new("Invincible NPCs", "enemyreceiveddamage_immortal") { Category = NegativeIncentive, Duration = 10,
            Description = "NPCs won't take damage from any source", Price = (uint) (1650 * PRICINGFACTOR )},
        new("Play the objective", "oneshotscripteffect_" + (int)OneShotEffect.RandomLoadout){ Category = NegativeIncentive,
            Description = "Be a team player, and hold the oddball.", Price = (uint) (1200 * PRICINGFACTOR )},

        new("Spawn random vehicle", "oneshotscripteffect_" + (int)OneShotEffect.SpawnRandomVehicle){ Category = PositiveIncentive,
            Description = "Spawn a random vehicle.", Price = (uint) (1000 * PRICINGFACTOR )},

        // Player stats change
        new("Give one shield charge", "shield_plus1") {Category = PositiveIncentive,
            Description = "Increases the current shield by a full charge (the normal full shield amount). It stacks infinitely.", Price = (uint) (500 * PRICINGFACTOR )},
        new("Remove shield charge", "shield_minus1") {Category = NegativeIncentive,
            Description = "Removes a full charge (the normal full shield amount) from the shield amount.", Price = (uint) (500 * PRICINGFACTOR )},

        new("Disable shield regen", "shieldRegen_no") {Category = NegativeIncentive, Duration = TimeSpan.FromSeconds(15),
            Description = "Shields won't recharge at all for a bit.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Constant shield regen", "shieldRegen_instant") { Category = PositiveIncentive, Duration = TimeSpan.FromSeconds(15),
            Description = "Shields will start regenerating instantly instead of after some time without receiving damage.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Heal up", "health_1") {Category = PositiveIncentive,
            Description = "Sets health to full.", Price = (uint) (500 * PRICINGFACTOR )},
        //new("Critical health", "health_min") {Category = HealthAndShieldsCategory,
        //    Description = "Sets health to \"barely alive\"", Price = (uint) (* PRICINGFACTOR )},
        new("Health regeneration", "healthRegen") {Category = PositiveIncentive, Duration = TimeSpan.FromSeconds(15),
            Description = "Regenerate health over time. Won't go over 100% health, but will keep healing any new damage for the duration. It stacks.", Price = (uint) (750 * PRICINGFACTOR )},
        new("Slow toxins", "slowpoison") {Category = NegativeIncentive, Duration = TimeSpan.FromSeconds(15),
            Description = "Lose health over time. Won't kill you, but it stacks.", Price = (uint) (750 * PRICINGFACTOR )},
        new("Health peg up", "health_gain1peg") { Category = PositiveIncentive,
            Description = "Heal one peg of health. It can go over max health.", Price = (uint) (50 * PRICINGFACTOR )},
        new("Health peg down", "health_lose1peg") { Category = NegativeIncentive,
            Description = "Remove one peg of health. Won't kill the player.", Price = (uint) (50 * PRICINGFACTOR )},

        // Speed related
        new("Fast run", "playerspeed_brisk") {Category = PositiveIncentive, Duration = TimeSpan.FromSeconds(20),
            Description = "Midly increases player running speed.", Price = (uint) (500 * PRICINGFACTOR )},
        new("Cursed speed", "playerspeed_ludicrous") {Category = NegativeIncentive, Duration = TimeSpan.FromSeconds(20),
            Description = "Increases player running speed so much that not falling and not clipping through slopes becomes a challenge.", Price = (uint) (1500 * PRICINGFACTOR )},
        new("Slow run", "playerspeed_slow") {Category = NegativeIncentive, Duration = TimeSpan.FromSeconds(20),
            Description = "Reduces player running speed.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Unstable airtime", "unstableairtime") {Category = NegativeIncentive, Duration = TimeSpan.FromSeconds(20),
            Description = "While on air and not in a vehicle, any momentum gets exponentially bigger and bigger.", Price = (uint) (1200 * PRICINGFACTOR )},
        new("Alien zoomies", "enemyspeed_ludicrous") {Category = NegativeIncentive, Duration = TimeSpan.FromSeconds(20),
            Description = "Makes non-players run extremely fast.", Price = (uint) (900 * PRICINGFACTOR )},

        // Damage related
        new("Quad damage", "enemyreceiveddamage_quad") { Category = PositiveIncentive, Duration = 20,
            Description = "Enemies receive 4 times the damage.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Healing bullets", "enemyreceiveddamage_reversed") { Category = NeutralIncentive, Duration = 20,
            Description = "Damage HEALS non-players, without a max health cap.", Price = (uint) (1200 * PRICINGFACTOR )},
        new("Plot armor", "playerreceiveddamage_invulnerable") { Category = PositiveIncentive, Duration = 20,
            Description = "I'll have you know that I've become indestructible, determination that is incorruptible!", Price = (uint) (2000 * PRICINGFACTOR )},
        new("Heaven or hell", "allreceiveddamage_instadeath") { Category = NeutralIncentive, Duration = 20,
            Description = "Enemies (except dropships) die in one hit. But so do you.", Price = (uint) (1700 * PRICINGFACTOR )},
        new("Nerf war", "allreceiveddamage_invulnerable") { Category = NegativeIncentive, Duration = 20,
            Description = "Nobody receives ANY damage.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Glass cannon", "allreceiveddamage_glass") { Category = NeutralIncentive, Duration = 20,
            Description = "Deal triple damage, but also receive it.", Price = (uint) (650 * PRICINGFACTOR )},
        new("One shot one kill", "continuouseffect_" + (int)OneShotEffect.OneShotOneKill) { Category = PositiveIncentive, Duration = 20,
            Description = "Kill any enemy in one hit, including dropships.", Price = (uint) (2000 * PRICINGFACTOR )},
        new("Deathless", "continuouseffect_" + (int)OneShotEffect.Deathless) { Category = PositiveIncentive, Duration = 20,
            Description = "You do take damage, but you won't die.", Price = (uint) (2000 * PRICINGFACTOR )},

        // Mixed hinderances
        new("Kill player", "oneshotscripteffect_" + (int)OneShotEffect.KillPlayer) { Category = NegativeIncentive,
            Description = "Kills the player unceremoniously. Can be blocked by invincibility incentives.", Price = (uint) (5000 * PRICINGFACTOR )},
        new("Reset level", "oneshotscripteffect_" + (int)OneShotEffect.RestartLevel) { Category = NegativeIncentive,
            Description = "Resets the current mission", Price = (uint) (2500 /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(60)*/)},
        new("Give all vehicles", "oneshotscripteffect_" + (int)OneShotEffect.GiveAllVehicles) { Category = NeutralIncentive,
            Description = "Drops all the available vehicles on top of the player.", Price = (uint) (2000 * PRICINGFACTOR )},
        
        new("Rapture", "oneshotscripteffect_" + (int)OneShotEffect.Rapture) { Category = NegativeIncentive, Duration = 15,
            Description = "Heavens call, and all the worthy shall ascend. And then fall to their death.", Price = (uint) (3500 * PRICINGFACTOR /*, ScaleFactor=1.5f, ScaleDecayTime = TimeSpan.FromMinutes(4)*/)},
        new("Grenades: Steal 6 of each", "grenades_take") { Category = NegativeIncentive,
            Description = "Takes 6 grenades away of each type.", Price = (uint) (500 * PRICINGFACTOR )},
        new("Give instant checkpoint", "oneshotscripteffect_" + (int)OneShotEffect.GiveUnsafeCheckpoint) { Category = NeutralIncentive,
            Description = "Gives a checkpoint immediately, regardless of if the player is about to die or falling to their doom.", Price = (uint) (1700 * PRICINGFACTOR )},
        new("Shove", "addspeed_shove1") { Category = NegativeIncentive,
            Description = "Shove the player in a random direction and strength. Does not work on vehicles.", Price = (uint) (750 * PRICINGFACTOR /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("The shakes", "addspeed_shake") { Category = NegativeIncentive, Duration = 7,
            Description = "Shake the player for a bit. Does not work on vehicles.", Price = (uint) (1200 * PRICINGFACTOR /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Drunk", "addspeed_drunk") { Category = NegativeIncentive, Duration = 30,
            Description = "Make the player stumble for a while. Does not work on vehicles.", Price = (uint) (1500 * PRICINGFACTOR )},

        // Mixed help
        new("Active camouflage", "oneshotscripteffect_" + (int)OneShotEffect.ActiveCamo) {Category = PositiveIncentive,
            Description = "Gives active camouflage.", Price = (uint) (1700 * PRICINGFACTOR )},
        new("Slipspace jump", "oneshotscripteffect_" + (int)OneShotEffect.SkipLevel) {Category = PositiveIncentive,
            Description = "Skip the current level!", Price = (uint) (2500 /*, ScaleFactor=3.5f, ScaleDecayTime = TimeSpan.FromMinutes(99999)*/)},
        new("AI break", "continuouseffect_" + (int)OneShotEffect.AiBreak) {Category = PositiveIncentive, Duration = 15,
            Description = "Turns all AI off for a bit.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Grenades: Give 6 of each", "grenades_give") { Category = PositiveIncentive,
            Description = "Gives 6 grenades of each type.", Price = (uint) (500 * PRICINGFACTOR )},
        new("Give safe checkpoint", "oneshotscripteffect_" + (int)OneShotEffect.GiveSafeCheckpoint) { Category = PositiveIncentive,
            Description = "Gives a checkpoint as soon as the player is in a safe situation. If that ever comes.", Price = (uint) (400 * PRICINGFACTOR )},
        new("Truly infinite ammo", "continuouseffect_" + (int)OneShotEffect.TrulyInfiniteAmmo) { Category = PositiveIncentive, Duration = 15,
            Description = "Bottomless clips. No heat. Infinite ammo and battery. A boatload of grenades. Discombobulate.", Price = (uint) (1500 * PRICINGFACTOR )},

        // Movement
        new("Jetpack", "continuouseffect_" + (int)OneShotEffect.Jetpack) { Category = PositiveIncentive, Duration = 15,
            Description="Take no fall damage. Hold jump to fly and crouch to hover.", Price = (uint) (1500 * PRICINGFACTOR )},
        new("Low gravity", "continuouseffect_" + (int)OneShotEffect.LowGravity) { Category = NeutralIncentive, Duration = 15,
            Description="Decreases gravity by a lot.", Price = (uint) (800 * PRICINGFACTOR )},
        new("High gravity", "continuouseffect_" + (int)OneShotEffect.HighGravity) { Category = NeutralIncentive, Duration = 15,
            Description="Increases gravity by a lot.", Price = (uint) (800 * PRICINGFACTOR )},
        new("Super jump", "continuouseffect_" + (int)OneShotEffect.SuperJump) { Category = NegativeIncentive, Duration = 15,
            Description="Jump like a dragoon. Safe landing not included.", Price = (uint) (500 * PRICINGFACTOR )},

        // Oddities
        new("Body snatcher", "continuouseffect_" + (int)OneShotEffect.BodySnatcher) { Category = NegativeIncentive, Duration = 15,
            Description= "Possess anyone you touch. When it is over, you're stuck. And the game may \"despawn\" you like any other NPC.", Price = (uint) (2000 * PRICINGFACTOR )},
        new("This is awkward", "continuouseffect_" + (int)OneShotEffect.AwkwardMoment) { Category = NegativeIncentive, Duration = 10,
            Description= "Prevents action by anyone for a bit.", Price = (uint) (1650 * PRICINGFACTOR )},
        new("Medusa-117", "continuouseffect_" + (int)OneShotEffect.Medusa) { Category = PositiveIncentive, Duration = 15,
            Description = "Anyone that starts looking at you with you will die.", Price = (uint) (2000 * PRICINGFACTOR )},
        // Visibility and HUD
        new("Thunderstorm", "thunderstorm") { Category = NegativeIncentive, Duration = 10,
            Description = "Pitch black, the only light coming from occasional thunder.", Price = (uint) (1650 * PRICINGFACTOR )},
        new("Paranoia", "paranoia") { Category = NegativeIncentive, Duration = 10,
            Description = "Give the player additional anxiety.", Price = (uint) (1000 * PRICINGFACTOR /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Movie mode", "continuouseffect_" + (int)OneShotEffect.MovieBars) { Category = NeutralIncentive, Duration = 15,
            Description = "Sets the mood for some popcorn.", Price = (uint) (250 * PRICINGFACTOR /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("No HUD", "continuouseffect_" + (int)OneShotEffect.NoHud) { Category = NegativeIncentive, Duration = 15,
            Description = "Disable the HUD.", Price = (uint) (500 * PRICINGFACTOR )},
        new("Expert aiming mode", "continuouseffect_" + (int)OneShotEffect.NoCrosshair) { Category = NegativeIncentive, Duration = 15,
            Description = "Disable the crosshair.", Price = (uint) (400 * PRICINGFACTOR /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Silence", "continuouseffect_" + (int)OneShotEffect.Silence) { Category = NegativeIncentive, Duration = 25,
            Description = "Disable sound.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("HUD Malfunction", "oneshotscripteffect_" + (int)OneShotEffect.Malfunction) { Category = NegativeIncentive,
            Description = "Disable a random section of the HUD (health, shields, motion sensor or crosshair) permanently. Does nothing if all are disabled already.", Price = (uint) (750 * PRICINGFACTOR /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("HUD Technician", "oneshotscripteffect_" + (int)OneShotEffect.RepairHud) { Category = PositiveIncentive,
            Description = "Make a random disabled section of the HUD (health, shields, motion sensor or crosshair) visible again. Does nothing if all are visible already.", Price = (uint) (750 * PRICINGFACTOR )},

        // Key manipulation (I count mouse buttons as keys).
        new("Crab rave", "crabrave") { Category = NegativeIncentive, Duration = 15,
            Description = "You're a crab, John. Move sideways only.", Price = (uint) (1000 * PRICINGFACTOR /*, ScaleFactor=1.5f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Moonwalk", "moonwalk") { Category = NegativeIncentive, Duration = 15,
            Description = "Force to move backwards.", Price = (uint) (1000 * PRICINGFACTOR /*, ScaleFactor=1.5f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Bunny hop", "forcerepeatedjump") { Category = NegativeIncentive, Duration = 15,
            Description = "Force repeated jumping.", Price = (uint) (1000 * PRICINGFACTOR /*, ScaleFactor=1.5f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Grenade out!", "forcegrenades") { Category = NegativeIncentive, Duration = 15,
            Description = "Let 'em have it! Yeah, frag 'em! Nade 'em!", Price = (uint) (1200 * PRICINGFACTOR )},
        new("Pacifist", "preventattacking") { Category = NegativeIncentive, Duration = 15,
            Description = "Do no harm. Yet.", Price = (uint) (1800 * PRICINGFACTOR )},
        new("Reverse movement", "reversemovement") { Category = NegativeIncentive, Duration = 15,
            Description = "Severely confuse your legs. WASD controls are reversed.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Randomize controls", "randomizecontrols") { Category = NegativeIncentive, Duration = 60,
            Description = "Randomly swap all non-walking controls", Price = (uint) (2000 * PRICINGFACTOR )},
        new("Berserker", "berserker") { Category = PositiveIncentive, Duration = 20,
            Description = "Rip and tear until it's done. Or time runs out. Melee only, one-shot enemies, fast run, can't die, heal up when finished.", Price = (uint) (1700 * PRICINGFACTOR )},
        new("Turret mode", "turretmode") { Category = NeutralIncentive, Duration = 15,
            Description = "Stay still, but get infinite ammo and bottomless magazines for the duration.", Price = (uint) (1000 * PRICINGFACTOR )},
        new("Sneaky beaky like", "forcecrouch") { Category = NegativeIncentive, Duration = 15,
            Description = "Force the player to stay crouched.", Price = (uint) (1200 * PRICINGFACTOR )},

        // Mouse manipulation.
        new("Foot fetish", "forcemouse_down") { Category = NegativeIncentive, Duration = 10,
            Description = "Force the player to look down.", Price = (uint) (1000 * PRICINGFACTOR /*, ScaleFactor=1.5f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Heavensward gaze", "forcemouse_up") { Category = NegativeIncentive, Duration = 10,
            Description = "Force the player to look up.", Price = (uint) (1000 * PRICINGFACTOR /*, ScaleFactor=1.5f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("S.P.E.E.N. protocol", "forcemouse_spin") { Category = NegativeIncentive, Duration = 10,
            Description = "Force to spin.", Price = (uint) (1500 * PRICINGFACTOR /*, ScaleFactor=1.5f, ScaleDecayTime = TimeSpan.FromMinutes(1) */)},
        new("Joycon drift", "forcemouse_drift") { Category = NegativeIncentive, Duration = 10,
            Description = "Slightly move the cursor constantly.", Price = (uint) (250 * PRICINGFACTOR /*, ScaleFactor=1.3f, ScaleDecayTime = TimeSpan.FromMinutes(1)*/)},
        new("Shaky hands", "forcemouseshake") { Category = NegativeIncentive, Duration = 10,
            Description = "Shake the crosshair.", Price = (uint) (1500 * PRICINGFACTOR )},
        // Discarded
        //new("Break shield", "shield_break") {Category = Discarded,
        //    Description = "Sets shields to 0. Discarded because it breaks the give/take shield bid wars.", Price = (uint) (* PRICINGFACTOR )},
        //new("Anchor", "playerspeed_anchored") {Category = Discarded, Duration = TimeSpan.FromSeconds(20),
        //    Description = "Anchors the player to the ground. Replaced by Turret Mode.", Price = (uint) (* PRICINGFACTOR )},
        //new("Confused legs", "playerspeed_reversed") {Category = Discarded, Duration = TimeSpan.FromSeconds(20),
        //    Description = "Reverses movement direction for any force, including your legs. Can cause clipping through slopes. Discared with a better implementation in Reverse Movement.", Price = (uint) (* PRICINGFACTOR )},
        //new("Ludicrous damage", "enemyreceiveddamage_ludicrous") { Category = Discarded, Duration = 20,
        //    Description = "Any damage will kill an enemy or enemy vehicle. Replaced with One Shot One Kill, which can also destroy dropships.", Price = (uint) (* PRICINGFACTOR )},
        //new("Mythic", "enemyreceiveddamage_half") { Category = Discarded, Duration = 20,
        //    Description = "Enemies have twice the health and shields. Discarded because it was too hard to notice, so we just went with fully invincible enemies.", Price = (uint) (* PRICINGFACTOR )},
        //new("Lore accurate Mjolnir armor", "playerreceiveddamage_tenth") { Category = Discarded, Duration = 20,
        //    Description = "Player receives massively reduced damage. Discarded because if you are gonna take only 10% damage, you might as well just use full invincibility.", Price = (uint) (* PRICINGFACTOR )},
        //new("The Kat treatment", "playerreceiveddamage_instadeath") { Category = Discarded, Duration = 20,
        //    Description = "One hit will break your shields, the next will kill you. Your helmet won't help you. Discarded due too much overlapping with Heaven or Hell.", Price = (uint) (* PRICINGFACTOR )},
        //new("Give weapons", "oneshotscripteffect_ + (int)OneShotEffect.GiveWeapons") {Category = Discarded,
        //    Description = "Spawns a wide selection of weapons around the player. Replaced with \"swap to random loadout\".", Price = (uint) (* PRICINGFACTOR )},
        //new("Give all vehicles... safely", "continuouseffect_ + (int)OneShotEffect") {Category = Discarded, Duration = 10,
        //    Description = "Spawns all available vehicles on top of the player and makes it invulnerable long enough to survive it. Discared because give all vehicles can be survived, and replaced by Spawn random vehicle.", Price = (uint) (* PRICINGFACTOR )},
        //new("Crash the game", "oneshotscripteffect_ + (int)OneShotEffect.CrashGame") { Category = Discarded,
        //    Description = "Crashes the game. Literally. Discared because it was never a good idea.", Price = (uint) (* PRICINGFACTOR )},
        //new("Reset to checkpoint", "oneshotscripteffect_ + (int)OneShotEffect.ResetCheckpoint") { Category = Discarded,
        //    Description = "Resets the game to the last checkpoint. Discarded due to overlappint with Kill Player.", Price = (uint) (* PRICINGFACTOR )},
        //new("Such devastation", "oneshotscripteffect_ + (int)OneShotEffect.DestroyEverything") { Category = Discarded,
        //    Description = "Destroy EVERYTHING nearby. This was not your intention. Discarded because it has a massive chance of softlock.", Price = (uint) (* PRICINGFACTOR )},
        //new("Big mode", "continuouseffect_ + (int)OneShotEffect") { Category = Discarded, Duration = 15,
        //    Description="Become huge. Discarded because it is not seen unless you're in a vehicle or doing a kick.", Price = (uint) (* PRICINGFACTOR )},
        //new("Pocket spartan", "continuouseffect_ + (int)OneShotEffect") { Category = Discarded, Duration = 15,
        //    Description="Become small. Discarded because it is not seen unless you're in a vehicle or doing a kick.", Price = (uint) (* PRICINGFACTOR )},
        //new("Highly visible NPCs.", "continuouseffect_ + (int)OneShotEffect") { Category = Discarded, Duration = 15,
        //    Description = "NPCs and objects are always bright. Discarded because it was barely noticeable out of dark levels.", Price = (uint) (* PRICINGFACTOR )},
        //new("Barely visible NPCs", "continuouseffect_ + (int)OneShotEffect") { Category = Discarded, Duration = 15,
        //    Description = "NPCs and objects are always the darkest they can be. Discarded because it was barely noticeable out of dark levels", Price = (uint) (* PRICINGFACTOR )},
        //new("Give non-warthog grenades", "grenades_givenowarthog") { Category = Discarded,
        //    Description = "Gives 6 grenades of each type, except throwable warthogs. They are a bit too useful.", Price = (uint) (* PRICINGFACTOR )},
        //new("Rambo", "forcefire") { Category = Discarded, Duration = 15,
        //    Description = "Fire at will. The crowd's will!. Replaced by fuller auto and fullest auto.", Price = (uint) (* PRICINGFACTOR )},
        //new("Aliens move backwards", "enemyspeed_reversed") {Category = Discarded, Duration = TimeSpan.FromSeconds(20),
        //    Description = "Non-players will move in the opposite direction to where they want to. Discarded because it did not always work and was not very impressive.", Price = (uint) (* PRICINGFACTOR )},
        //new("Static aliens", "enemyspeed_anchored") {Category = Discarded, Duration = TimeSpan.FromSeconds(20),
        //    Description = "Non-players are rooted in place. Discarded because it did not always work and was not very impressive.", Price = (uint) (* PRICINGFACTOR )},
    };
}