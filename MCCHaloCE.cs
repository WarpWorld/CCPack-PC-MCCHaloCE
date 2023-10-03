#define DEVELOPMENT

using ConnectorLib.Inject.AddressChaining;
using ConnectorLib.Inject.VersionProfiles;
using CrowdControl.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using CcLog = CrowdControl.Common.Log;
using ConnectorType = CrowdControl.Common.ConnectorType;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    // Utility method to make the byte arrays created to inject easier to read.
    public static class ByteArrayExtensions
    {
        // Careful: a literal byte is interpreted as int by the compiler.
        public static byte[] AppendNum(this byte[] bytes, short number)
        {
            return bytes.Concat(BitConverter.GetBytes(number)).ToArray();
        }

        public static byte[] AppendNum(this byte[] bytes, int number)
        {
            return bytes.Concat(BitConverter.GetBytes(number)).ToArray();
        }

        public static byte[] AppendNum(this byte[] bytes, long number)
        {
            return bytes.Concat(BitConverter.GetBytes(number)).ToArray();
        }

        public static byte[] Append(this byte[] bytes, params byte[] newBytes)
        {
            return bytes.Concat(newBytes).ToArray();
        }

        // Does nothing, but really useful to identify relative addresses that may need updating.
        public static byte[] AppendRelativePointer(this byte[] bytes, string pointedSectionId, params byte[] newBytes)
        {
            return bytes.Append(newBytes);
        }

        // Does nothing, but helps to identify jumping points.
        public static byte[] LocalJumpLocation(this byte[] bytes, string sectionId)
        {
            return bytes;
        }
    }

    public class MCCHaloCE : InjectEffectPack
    {
        private const string ProcessName = "MCC-Win64-Shipping";

        // Base address of halo1.dll in memory. Using relative addresses to this is much more reliable than absolute addresses.
        private AddressChain halo1BaseAddress_ch;

        private long halo1BaseAddress;

        // Points to the start of the player unit data structure.
        private AddressChain? basePlayerPointer_ch = null;

        // Points to where the injected code store the variables we use to communicate with the H1 scripts.
        private AddressChain? scriptVarInstantEffectsPointerPointer_ch = null;

        private AddressChain? scriptVarTimedEffectsPointerPointer = null;

        // Periodically checks if injections are needed.
        private static Timer injectionCheckerTimer;

        // Allows us to access the instance form the timer static methods.
        private static MCCHaloCE instance;

        // Store references to changed memory so it can be undone.
        private List<(string Identifier, long Address, byte[] originalBytes)> ReplacedBytes = new();

        private List<(string Identifier, long Address, int caveSize)> CreatedCaves = new();

        private const bool DEBUG = true;

        // If true, injection won't replace game code. Useful for testing assembly generation without crashing the game.
        private const bool DONT_OVERWRITE = false;

        // Default size of created code caves.
        private const int StandardCaveSizeBytes = 1024;

        #region Memory Identifiers

        // Strings used to identify the purpose of memory changes and group them.
        private const string PlayerPointerId = "playerPointer";

        private const string OnDamageConditionalId = "ondamageconditional";
        private const string ScriptVarPointerId = "scriptVarPointerId";
        private const string ScriptVar2PointerId = "scriptVar2PointerId";
        private const string OnCreateGameEngine = "oncreategameengine";
        private const string SpeedFactorId = "speedfactor";
        private const string UnstableAirtimeId = "unstableAirtime";

        #endregion Memory Identifiers

        #region Injection Offsets

        private const long ScriptInjectionPointOffset = 0xACC0E9;

        #endregion Injection Offsets

        #region Status Control

        private float PlayerSpeedFactor = 1;
        private float OthersSpeedFactor = 1;

        private float PlayerReceivedDamageFactor = 1;
        private float OthersReceivedDamageFactor = 1;
        private bool InstakillEnemies = false;

        // Deactivation script code for all parts in the HUD
        private const int Crosshair = 10;

        private const int Health = 11;
        private const int MotionSensor = 12;
        private const int Shield = 13;
        private List<int> HudParts = new() { Crosshair, Health, MotionSensor, Shield };

        // Deactivation script code for all parts in the HUD currently deactivated.
        private List<int> DisabledHubParts = new();

        private void RepairHUD(int specificPart = 0)
        {
            if (specificPart == 0)
            {
                DisabledHubParts.Clear();
            }
            else
            {
                DisabledHubParts = DisabledHubParts.Where(x => x != specificPart).ToList();
            }
        }

        #endregion Status Control

        public override Game Game { get; } = new("MCC Halo Combat Evolved", "MCCHaloCE", "PC", ConnectorType.PCConnector);

        public const string HealthAndShieldsCategory = "Health and shields";
        public const string SpeedCategory = "Movement speed";
        public const string DamageCategory = "Damage done/received";
        public const string MixedHinderancesCategory = "Mixed hinderances";
        public const string MixedHelpCategory = "Mixed help";
        public const string MovementCategory = "Movement";
        public const string OdditiesCategory = "Oddities";
        public const string VisibilityAndHudCategory = "Visibility and HUD";

        public override EffectList Effects => new List<Effect> {
#if DEVELOPMENT
            new("Abort all injection", "abortallinjection"),
#endif
            // Player stats change
            new("Give one shield charge", "shield_plus1") {Category = HealthAndShieldsCategory,
                Description = "Increases the current shield by a full charge (the normal full shield amount). It stacks infinitely."},
            new("Remove shield charge", "shield_minus1") {Category = HealthAndShieldsCategory,
                Description = "Removes a full charge (the normal full shield amount) from the shield amount."},
            new("Break shield", "shield_break") {Category = HealthAndShieldsCategory,
                Description = "Sets shields to 0."},
            new("No shield recovery", "shieldRegen_no") {Category = HealthAndShieldsCategory, Duration = TimeSpan.FromSeconds(15),
                Description = "Shields won't recharge at all for a bit."},
            new("Instant shield regeneration start", "shieldRegen_instant") { Category = HealthAndShieldsCategory, Duration = TimeSpan.FromSeconds(15),
                Description = "Shields will start regenerating instantly instead of after some time without receiving damage."},
            new("UNSC Medicare", "health_1") {Category = HealthAndShieldsCategory,
                Description = "Sets health to full."},
            new("USA Medicare", "health_min") {Category = HealthAndShieldsCategory,
                Description = "Sets health to \"barely alive\""},
            new("Health regeneration", "healthRegen") {Category = HealthAndShieldsCategory, Duration = TimeSpan.FromSeconds(15),
                Description = "Regenerate health over time."},

            // Speed related
            new("Fast run", "playerspeed_brisk") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Midly increases player speed."},
            new("Cursed speed", "playerspeed_ludicrous") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Increases player speed so much that not falling and not clipping through slopes becomes a challenge."},
            new("Henceforth he shall walk", "playerspeed_slow") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Reduces player speed"},
            new("Anchor", "playerspeed_anchored") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Anchors the player to the ground"},
            new("Confused legs", "playerspeed_reversed") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Reverses movement direction for any force, including your legs."},
            new("Unstable airtime", "unstableairtime") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "While on air, any momentum gets progressively bigger and bigger."},
            new("Alien zoomies", "enemyspeed_ludicrous") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Makes non-players run extremely fast."},
            new("Aliens move backwards", "enemyspeed_reversed") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Non-players will move in the opposite direction to where they want to."},
            new("Static aliens", "enemyspeed_anchored") {Category = SpeedCategory, Duration = TimeSpan.FromSeconds(20),
                Description = "Non-players are rooted in place."},

            // Damage related
            new("Quad damage", "enemyreceiveddamage_quad") { Category = DamageCategory, Duration = 20,
                Description = "Enemies receive 4 times the damage."},
            //new("Ludicrous damage", "enemyreceiveddamage_ludicrous") { Category = DamageCategory, Duration = 20,
            //    Description = "Any damage will kill an enemy or enemy vehicle"}, // Irrelevant since we have Omnipotent
            new("Mythic", "enemyreceiveddamage_half") { Category = DamageCategory, Duration = 20,
                Description = "Enemies have twice the health and shields."},
            new("Spartan medicine", "enemyreceiveddamage_reversed") { Category = DamageCategory, Duration = 20,
                Description = "Damage HEALS non-players, without a max health cap." },
            new("Lore accurate Mjolnir armor", "playerreceiveddamage_tenth") { Category = DamageCategory, Duration = 20,
                Description = "Player receives massively reduced damage."},
            new("The Kat treatment", "playerreceiveddamage_instadeath") { Category = DamageCategory, Duration = 20,
                Description = "One hit will break your shields, the next will kill you. Your helmet won't help you."},
            new("Plot armor", "playerreceiveddamage_invulnerable") { Category = DamageCategory, Duration = 20,
                Description = "I'll have you know that I've become indestructible, determination that is incorruptible!"},
            new("Heaven or hell", "allreceiveddamage_instadeath") { Category = DamageCategory, Duration = 20,
                Description = "Enemies (except dropships) die in one hit. But so do you."},
            new("Nerf war", "allreceiveddamage_invulnerable") { Category = DamageCategory, Duration = 20,
                Description = "Nobody receives ANY damage"},
            new("Glass cannon", "allreceiveddamage_glass") { Category = DamageCategory, Duration = 20,
                Description = "Deal triple damage, but also receive it."},
            new("One shot one kill", "continuouseffect_24") { Category = DamageCategory, Duration = 20,
                Description = "Kill any enemy in one hit, including dropships."},
            new("Deathless", "continuouseffect_25") { Category = DamageCategory, Duration = 20,
                Description = "You do take damage, but you won't die."},

            // Mixed hinderances
            new("Kill player", "oneshotscripteffect_1") { Category = MixedHinderancesCategory,
                Description = "Kills the player unceremoniously."},
            new("Crash the game", "oneshotscripteffect_2") { Category = MixedHinderancesCategory,
                Description = "Crashes the game. Literally."},
            new("Reset to checkpoint", "oneshotscripteffect_3") { Category = MixedHinderancesCategory,
                Description = "Resets the game to the last checkpoint."},
            new("Reset level", "oneshotscripteffect_4") { Category = MixedHinderancesCategory,
                Description = "Resets the current mission"},
            new("Give all vehicles", "oneshotscripteffect_5") { Category = MixedHinderancesCategory,
                Description = "Drops all the available vehicles on top of the player."},
            new("Armor lock", "continuouseffect_0") { Category = MixedHinderancesCategory, Duration = 5,
                Description = "Become invincinble, but unable to act."},
            new("Faulty armor lock", "continuouseffect_1") { Category = MixedHinderancesCategory, Duration = 5,
                Description = "Become unable to act, while still being very vincible."},
            new("Invert view controls", "continuouseffect_2") { Category = MixedHinderancesCategory, Duration = 5,
                Description = "Sets the view controls to inverted, or to normal if they were already inverted"},
            new("Rapture", "continuouseffect_15") { Category = MixedHinderancesCategory, Duration = 10,
                Description = "Heavens call, and you shall answer."}, // TODO: Include a forced jump, gravity does not affect you in the ground.

            // Mixed help
            new("Active camouflage", "oneshotscripteffect_6") {Category = MixedHelpCategory,
                Description = "Gives active camouflage."},
            new("Give weapons", "oneshotscripteffect_7") {Category = MixedHelpCategory,
                Description = "Spawns a wide selection of weapons around the player."},
            new("Slipspace jump", "oneshotscripteffect_8") {Category = MixedHelpCategory,
                Description = "Skip the current level!"},
            new("Give all vehicles... safely", "continuouseffect_3") {Category = MixedHelpCategory, Duration = 5,
                Description = "Spawns all available vehicles on top of the player and makes it invulnerable long enough to survive it."},
            new("AI break", "continuouseffect_4") {Category = MixedHelpCategory, Duration = 15,
                Description = "Turns all AI off for a bit."},

            // Movement
            new("Jetpack", "continuouseffect_5") { Category = "Movement", Duration = 15,
                Description="Take no fall damage. Hold jump to fly and crouch to hover." },
            new("High gravity", "continuouseffect_6") { Category = "Movement", Duration = 15,
                Description="Increases gravity by a lot." },
            new("Low gravity", "continuouseffect_7") { Category = "Movement", Duration = 15,
                Description="Decreases gravity by a lot." },
            new("Super jump", "continuouseffect_8") { Category = "Movement", Duration = 15,
                Description="Jump like a dragoon. Safe landing not included." },

            // Oddities
            new("Such devastation", "oneshotscripteffect_9") { Category = OdditiesCategory,
                Description = "Destroy EVERYTHING nearby. This was not your intention." },
            new("Big mode", "continuouseffect_9") { Category = OdditiesCategory, Duration = 15,
                Description="Become huge." },
            new("Pocket spartan", "continuouseffect_10") { Category = OdditiesCategory, Duration = 15,
                Description="Become small."},
            new("Body snatcher", "continuouseffect_11") { Category = OdditiesCategory, Duration = 15,
                Description= "Possess anyone you touch."},
            new("This is awkward", "continuouseffect_12") { Category = OdditiesCategory, Duration = 15,
                Description= "Prevents action by anyone for a bit."},
            new("Medusa-117", "continuouseffect_13") { Category = OdditiesCategory, Duration = 15,
                Description = "Anyone that locks eyes with you will die."},
            new("Truly infinite ammo", "continuouseffect_14") { Category = OdditiesCategory, Duration = 15,
                Description = "Bottomless clips. No heat. Infinite ammo and battery."}, // TODO: this does not add grenades. Update that.

            // Visibility and HUD
            new("Darkest stormy night", "continuouseffect_16") { Category = VisibilityAndHudCategory, Duration = 45,
                Description = "Pitch black, the only light coming from occasional thunder." },
            new("Highly visible NPCs.", "continuouseffect_17") { Category = VisibilityAndHudCategory, Duration = 15,
                Description = "NPCs and objects are always bright." },
            new("Barely visible NPCs", "continuouseffect_18") { Category = VisibilityAndHudCategory, Duration = 15,
                Description = "NPCs and objects are always the darkest they can be." },
            new("Movie mode", "continuouseffect_19") { Category = VisibilityAndHudCategory, Duration = 15,
                Description = "Sets the mood for some popcorn." },
            new("Paranoia", "continuouseffect_20") { Category = VisibilityAndHudCategory, Duration = 25,
                Description = "Give the player additional anxiety." },
            new("Blind", "continuouseffect_21") { Category = VisibilityAndHudCategory, Duration = 15,
                Description = "Disable the HUD." },
            new("Expert aiming mode", "continuouseffect_22") { Category = VisibilityAndHudCategory, Duration = 15,
                Description = "Disable the crosshair." },
            new("Silence", "continuouseffect_23") { Category = VisibilityAndHudCategory, Duration = 25,
                Description = "Disable sound." },
            new("Malfunction", "oneshotscripteffect_10") { Category = VisibilityAndHudCategory,
                Description = "Disable a random section of the HUD permanently." },// 10, 11, 12 and 13 are reserved for Malfunction
            new("HUD technician", "oneshotscripteffect_14") { Category = VisibilityAndHudCategory,
                Description = "Make every part of the HUD visible again." },

            // And that's enough for now.

        };

        #region init/deinit

        public MCCHaloCE(UserRecord player, Func<CrowdControlBlock, bool> responseHandler, Action<object> statusUpdateHandler)
            : base(player, responseHandler, statusUpdateHandler)
        {
            VersionProfiles = new List<VersionProfile>
            {
                new(ProcessName, InitGame, DeinitGame, null, ConnectorLib.Inject.Payload.DirectX.Direct3DVersion.Direct3D9 ),
            };

            instance = this;
        }

        private void InitGame()
        {
            SetHalo1BaseAddress();
            if (!DONT_OVERWRITE)
            {
                CreatePeriodicInjectionChecker();
            }
            else
            {
                CcLog.Message("Debugging mode. Injections are not automatic.");
            }
        }

        private void DeinitGame()
        {
            AbortAllInjection();
        }

        private void SetHalo1BaseAddress()
        {
            halo1BaseAddress_ch = AddressChain.ModuleBase(Connector, "halo1.dll");
            if (!halo1BaseAddress_ch.Calculate(out long halo1BaseAddress))
            {
                throw new Exception("Could not get halo1.dll base address");
            }

            this.halo1BaseAddress = halo1BaseAddress;
            CcLog.Message("Halo 1 base address: " + halo1BaseAddress);
        }

        #endregion init/deinit

        #region Autoinjecter

        // Activates the timer that periodically checks if code injections should be done, for instance, when going back to the main menu.
        private void CreatePeriodicInjectionChecker()
        {
            injectionCheckerTimer = new Timer(500);
            injectionCheckerTimer.Elapsed += OnPeriodicInjectionCheck;
            injectionCheckerTimer.AutoReset = true;
            injectionCheckerTimer.Enabled = true;
        }

        // Checks if the code injections should be done, by checking if an injection point still has its original code.
        private bool IsInjectionNeeded()
        {
            if (scriptVarInstantEffectsPointerPointer_ch == null)
            {
                CcLog.Message("scriptVarCommPointer was null");
                return true;
            }

            var scriptVarReadingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + ScriptInjectionPointOffset);

            // original instruction is 0x48, 0x63, 0x42, 0x34, // movsxd  rax,dword ptr [rdx+34]
            // if it is there, the code has been reset and needs to be reinjected.
            byte[] originalInstruction = new byte[] { 0x48, 0x63, 0x42, 0x34 }; // <-- the original instruction at that address
            byte[] bytesAtInjectionPoint = scriptVarReadingInstruction_ch.GetBytes(4);
            if (bytesAtInjectionPoint.Length != 4)
            {
                CcLog.Message("Bytes read had a length different than 4: " + bytesAtInjectionPoint.Length);
                return true;
            }
            for (int i = 0; i < bytesAtInjectionPoint.Length; i++)
            {
                if (originalInstruction[i] != bytesAtInjectionPoint[i])
                {
                    return false;
                }
            }

            CcLog.Message("The bytes read match the original instruction, which means our hooks were overwritten.");
            return true;
        }

        // Inject all needed code.
        private void InjectAllHooks()
        {
            InjectScriptHook();
            InjectPlayerBaseReader();
        }

        // Called by the periodic timer.
        private static void OnPeriodicInjectionCheck(Object source, ElapsedEventArgs e)
        {
            injectionCheckerTimer.Enabled = false;

            if (instance.IsInjectionNeeded())
            {
                CcLog.Message("Injecting.");
                instance.InjectAllHooks();
                CcLog.Message("Injection complete.");
            }

            injectionCheckerTimer.Enabled = true;
        }

        #endregion Autoinjecter

        #region Injecters

        /// <summary>
        /// Inserts code that writes pointers to the scripts variables, <see cref="scriptVarInstantEffectsPointerPointer_ch"/>
        /// and <see cref="scriptVarTimedEffectsPointerPointer"/>, which allows the effect pack to communicate with the H1 scripts.
        /// </summary>
        private void InjectScriptHook()
        {
            UndoInjection(ScriptVarPointerId);
            CcLog.Message("Injecting script communication hook.---------------------------");
            // Original replaced bytes. Total length: 16 (0x10)
            //0x48, 0x63, 0x42, 0x34, // movsxd  rax,dword ptr [rdx+34]
            //0x48, 0x03, 0xC2, //add rax,rdx
            //0x8B, 0x44, 0xC8, 0x04, // mov eax,[rax+rcx*8+04]
            //0x48, 0x83, 0xC4, 0x20, //add rsp, 20
            //0x5B, // pop rbx
            var scriptVarReadingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + ScriptInjectionPointOffset);
            int bytesToReplaceLength = 0x10;

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(scriptVarReadingInstruction_ch, bytesToReplaceLength);
            ReplacedBytes.Add((ScriptVarPointerId, injectionAddress, originalBytes));

            IntPtr scriptVarPointerPointer = CreateCodeCave(ProcessName, 8);
            IntPtr scriptVar2PointerPointer = CreateCodeCave(ProcessName, 8);
            CreatedCaves.Add((ScriptVarPointerId, (long)scriptVarPointerPointer, 8));
            CreatedCaves.Add((ScriptVarPointerId, (long)scriptVar2PointerPointer, 8));

            CcLog.Message("Script var 1 pointer: " + ((long)scriptVarPointerPointer).ToString("X"));
            CcLog.Message("Script var 2 pointer: " + ((long)scriptVar2PointerPointer).ToString("X"));

            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));
            scriptVarInstantEffectsPointerPointer_ch = AddressChain.Absolute(Connector, (long)scriptVarPointerPointer);
            scriptVarTimedEffectsPointerPointer = AddressChain.Absolute(Connector, (long)scriptVar2PointerPointer);

            // This script, for each of our script communication variables, hooks to where it is read.
            // The injected code checks if the one read is the script var with its original value, and
            // checks also that the nearby "anchor" variables defined in the script match it to avoid
            // false positives, then writes the pointer on a small code cave.
            byte[] variableGetter = new byte[]
            {
                0x52, // push rdx
                0x48, 0x8B, 0xD1, // mov rdx, rcx
                0x48, 0x6B, 0xD2, 0x08, //imul rdx, 0x8
                0x48, 0x01, 0xC2, // add rdx, rax
                0x48, 0x83, 0xC2, 0x04, // add rdx, 0x4
                0x81, 0x3A, 0x15, 0xCD, 0x5B, 0x07, // cmp [rdx], 0x75BCD15 ;compare to initial value of var
                0x75 }.AppendRelativePointer("checkIfScriptVar2", 0x2D) // jne 0x2D to the next var check
            .Append(
                0x48, 0x83, 0xC2, 0x08, // add rdx,08
                0x81, 0x3A, 0xB1, 0x68, 0xDE, 0x3A,//cmp [rdx],3ADE68B1 ;compare to value of right anchor, 987654321
                0x75).AppendRelativePointer("checkIfScriptVar2", 0x21) //jne (0X21), to the next var check
            .Append(
                0x48, 0x83, 0xEA, 0x10,//sub rdx,10
                0x81, 0x3A, 0xE7, 0xA4, 0x5D, 0x2E,//cmp [rdx],2E5DA4E7 // compare to value of left anchor 777888999)
                0x75).AppendRelativePointer("checkIfScriptVar2", 0x15)  //jne 0x15, to the next var check
            .Append(
                0x48, 0x83, 0xC2, 0x08, // add rdx, 08 <- reset offset to point again to the main variable instead of an anchor
                0x50, // push rax
                0x48, 0x8B, 0xC2, // mov rax, rdx
                0x48, 0xA3).AppendNum((long)scriptVarPointerPointer) // mov [VarPointerPointer], rax
            .Append(
                0x58, // pop rax
                0xEB).AppendRelativePointer("popPushedRegistersAndEnd", 0x33) //jmp pop rdx (31)
            .LocalJumpLocation("checkIfScriptVar2").Append(
                0x81, 0x3A, 0x00, 0x00, 0x00, 0x40, // cmp [rdx], 0x40 00 00 00 ;compare to initial value of var
                0x75).AppendRelativePointer("popPushedRegistersAndEnd", 0x2B) // jne 0x2B to "pop rdx" to avoid storing any variable that isn't our marker
            .Append(
                0x48, 0x83, 0xC2, 0x08, // add rdx,08
                0x81, 0x3A, 0x09, 0xA4, 0x5D, 0x2E,//cmp [rdx],2E5DA409 ; compare to value of right anchor, 777888777
                0x75).AppendRelativePointer("popPushedRegistersAndEnd", 0x1F) //jne (0X1F), to pop prdx
            .Append(
                0x48, 0x83, 0xEA, 0x10,//sub rdx,10
                0x81, 0x3A, 0xB1, 0xD0, 0x5E, 0x07,//cmp [rdx],75ED0B1 L compare to value of left anchor 123654321)
                0x75).AppendRelativePointer("popPushedRegistersAndEnd", 0x13)  //jne 0x13, to pop rdx
            .Append(
                0x48, 0x83, 0xC2, 0x08, // add rdx, 08 <- reset offset to point again to the main variable instead of an anchor
                0x50, // push rax
                0x48, 0x8B, 0xC2, // mov rax, rdx
                0x48, 0xA3).AppendNum((long)scriptVar2PointerPointer) // mov [VarPointerPointer], rax
            .Append(
                0x58 // pop rax
            ).LocalJumpLocation("popPushedRegistersAndEnd").Append(
                0x5A // pop rdx
            );

            byte[] originalWithVariableGetter = SpliceBytes(originalBytes, variableGetter, 0x7).ToArray(); // Inserts before mov eax,[rax+rcx*8+04]
            byte[] fullCaveCode = AppendUnconditionalJump(originalWithVariableGetter, injectionAddress + bytesToReplaceLength);

            long cavePointer = CodeCaveInjection(scriptVarReadingInstruction_ch, bytesToReplaceLength, fullCaveCode);
            CreatedCaves.Add((ScriptVarPointerId, cavePointer, StandardCaveSizeBytes));

            CcLog.Message("Script communication hook injection finished.----------------------");
        }

        /// <summary>
        /// Injects code that writes a pointer to the beginning of the player's data location, <see cref="basePlayerPointer_ch"/>.
        /// </summary>
        private void InjectPlayerBaseReader()
        {
            UndoInjection(PlayerPointerId);
            CcLog.Message("Injecting player base reader.---------------------------");
            //byte[] shieldsReadingInstructionPattern = { 0xF3, 0x0F, 0x10, 0x96, 0x9C, 0x00, 0x00, 0x00 };
            //var valueReadingInstruction_ch = AddressChain.AOB(Connector, 0, shieldsReadingInstructionPattern, "xxxxxxxx", 0, ConnectorLib.ScanHint.ExecutePage).Cache();
            var valueReadingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + 0xC50557);

            int bytesToReplaceLength = 0x17;

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(valueReadingInstruction_ch, bytesToReplaceLength);

            ReplacedBytes.Add((PlayerPointerId, injectionAddress, originalBytes));
            IntPtr playerPointer = CreateCodeCave(ProcessName, 8); // todo: change the offset to point to the structure start.
            CreatedCaves.Add((PlayerPointerId, (long)playerPointer, 8));
            basePlayerPointer_ch = AddressChain.Absolute(Connector, (long)playerPointer);

            CcLog.Message("Player pointer: " + ((long)playerPointer).ToString("X"));

            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            // Simply hooks to an instruction where the player structure location is read, and stores it.
            byte[] prependedBytes = new byte[]
            {
                0x50, // push rax,
                0x48, 0x8B, 0XC6 } // mov rax, rsi
            .Append(
                0x48, 0xA3).AppendNum((long)playerPointer) // mov [playerPointer], rax. Note: 0x48 means we're using long mode (i.e. 64 bits instead of 32)
            .Append(
                0x58); // pop rax

            byte[] originalWithUnCondJump = AppendUnconditionalJump(originalBytes, injectionAddress + bytesToReplaceLength);
            byte[] originalWithFixedJcc = FixRelativeJccAfterRelocation(originalWithUnCondJump, injectionAddress, 0xE, 0x6);
            byte[] fullCaveContents = prependedBytes.Concat(originalWithFixedJcc).ToArray();

            long cavePointer = CodeCaveInjection(valueReadingInstruction_ch, bytesToReplaceLength, fullCaveContents);
            CreatedCaves.Add((PlayerPointerId, cavePointer, StandardCaveSizeBytes));

            CcLog.Message("Player base injection finished.---------------------------");
        }

        /// <summary>
        /// Injects code that changes the speed of the player and/or the enemies.
        /// </summary>
        /// <param name="boostPlayer"></param>
        /// <param name="boostOthers"></param>
        private bool InjectSpeedMultiplier()
        {
            UndoInjection(SpeedFactorId);
            bool boostPlayer = PlayerSpeedFactor != 1;
            bool boostOthers = OthersSpeedFactor != 1;

            byte[] jumpStatement;
            byte jumpLength = 0x73;
            if (boostPlayer)
            {
                jumpStatement = boostOthers
                    ? new byte[] { 0x90, 0x90 } // nop nop, always boost
                    : new byte[] { 0x75, jumpLength }; // jne,skip enemies
            }
            else
            {
                jumpStatement = boostOthers
                   ? new byte[] { 0x74, jumpLength } // je, skip player
                   : new byte[] { 0xEB, jumpLength }; // jmp, skip everything.
            }
            var speedWritingInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + 0xB35E84 - 0x3); //cmp ebx, -01. I take the cmp to avoid conflicts with my own Jccs.
            int bytesToReplaceLength = 0x11;

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(speedWritingInstr_ch, bytesToReplaceLength);
            ReplacedBytes.Add((SpeedFactorId, injectionAddress, originalBytes));

            // Checks if the current unit is the player or not, and adds the current speed * the speed factor if that type of unit should be boosted.
            int caveDataOffset = 0x130; //0x12F; // Make sure it is divisible by 16 or xmm functions can crash
            byte[] newBytes = new byte[] {
                0x51, // push rcx,
                0x48, 0x8B, 0x8A, 0x8b, 0x09, 0x00, 0x00, // mov rcx,[rdx + 0x98b] ; (0x9a3 /*player discriminator value*/ - 0x18 /*x coord offset)
                0x48, 0x81, 0xf9, 0x3f, 0x00, 0x00, 0x00, // cmp rcx, 0x3f (63 in decimal)
                0x59 } // pop rcx
            .Append(
                jumpStatement) // TODO: these have relative jumps
            .Append(
                0x48, 0x83, 0xEC, 0x10, // sub rsp, 0x10
                0xf3, 0x0f, 0x7f, 0x1C, 0x24, // movdqu [rsp], xmm3 // back up xmm3
                0x48, 0x83, 0xEC, 0x10, // sub rsp, 0x10
                0xf3, 0x0f, 0x7f, 0x14, 0x24, // movdqu [rsp], xmm2// back up xmm2
                0x0f, 0x57, 0xdb, // xorps xmm3, xmm3 (to clear it)
                0x50, // push rax
                0x8b, 0x42, 0x18, // mov eax, [rdx + 18] // save the fourth value on the speeds so we can set it to 0 temporarily
                0xc7, 0x42, 0x18, 0x0, 0x0, 0x0, 0x0, // mov [rdx + 18], 0
                0xf3, 0x0f, 0x6f, 0x5a, 0xC, //movdqu xmm3,[rdx+C]
                0x89, 0x42, 0x18, //mov [rdx+18], eax
                0xE8, 0x0, 0x0, 0x0, 0x0, // call to next instruction. Will put rip on rax
                0x90, //nop
                0x48, 0x8b, 0x04, 0x24, // mov rax, [rsp] ; retrieve rip after call
                0x48, 0x83, 0xc4, 0x08, // add rsp, 0x8; move the stack back to before the call

                // multiply by different factors depending on if the curren tunit is the player
                0x51, // push rcx,
                0x48, 0x8B, 0x8A, 0x8b, 0x09, 0x00, 0x00, // mov rcx,[rdx + 0x98b] ; (0x9a3 /*player discriminator value*/ - 0x18 /*x coord offset)
                0x48, 0x81, 0xf9, 0x3f, 0x00, 0x00, 0x00, // cmp rcx, 0x3f (63 in decimal)
                0x59, // pop rcx
                0x75).AppendRelativePointer("Read non-player factor", 0x8)// jne 8
            .Append(
                0x48, 0x05).AppendNum(caveDataOffset - 0x3f) // add rax, [(offset for speed factors for the player)]
            .Append(
                0xEB).AppendRelativePointer("Skip non-player factor", 0x6)
            .LocalJumpLocation("Read non-player factor").Append(
                0x48, 0x05).AppendNum(caveDataOffset - 0x3f + (2 + 6) + (0x4 * 4)) // add rax, [(offset for speed factors for the non-players)]
            .LocalJumpLocation("Skip non-player factor")

            // Move factor to XMM and multiply the speed by it.
            .Append(
                0xf3, 0x0f, 0x6f, 0x10, // movdqu xmm2, [rax]
                0x58,  // pop rax
                0x0f, 0x59, 0xda, // mulps xmm3, xmm2
                0x0f, 0x58, 0xc3) // addps xmm0, xmm3
            // restore the xmm registers used
            .Append(
                0xf3, 0x0f, 0x6f, 0x14, 0x24, // movdqu xmm2,[rsp]
                0x48, 0x83, 0xc4, 0x10, // add rsp, 0x10
                0xf3, 0x0f, 0x6f, 0x1c, 0x24, // movdqu xmm3,[rsp]
                0x48, 0x83, 0xc4, 0x10); // add rsp, 0x10

            byte[] caveBytes = newBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            long cavePointer = CodeCaveInjection(speedWritingInstr_ch, bytesToReplaceLength, caveBytes);
            CreatedCaves.Add((SpeedFactorId, cavePointer, StandardCaveSizeBytes));

            // Set the in place data
            AddressChain dataPointer = AddressChain.Absolute(Connector, cavePointer + caveDataOffset);
            dataPointer.Offset(0).SetFloat(PlayerSpeedFactor);
            dataPointer.Offset(4).SetFloat(PlayerSpeedFactor);
            dataPointer.Offset(8).SetFloat(PlayerSpeedFactor);
            dataPointer.Offset(12).SetFloat(1);
            dataPointer.Offset(16).SetFloat(OthersSpeedFactor);
            dataPointer.Offset(20).SetFloat(OthersSpeedFactor);
            dataPointer.Offset(24).SetFloat(OthersSpeedFactor);
            dataPointer.Offset(28).SetFloat(1);

            return true;
        }

        /// <summary>
        /// Injects code that makes the player accelerate uncontrollably while on air.
        /// </summary>
        private bool InjectUnstableAirtime()
        {
            UndoInjection(UnstableAirtimeId);
            var speedWritingInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + 0xBB422E); //movsd [rbx+24],xmm0
            int bytesToReplaceLength = 0xE;
            float speedFactor = 1.05f;

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(speedWritingInstr_ch, bytesToReplaceLength);
            ReplacedBytes.Add((UnstableAirtimeId, injectionAddress, originalBytes));

            int caveDataOffset = 0x130;

            // Multiplies the speed in xmm0 by the given factor.
            byte[] newBytes = new byte[] {
                0x48, 0x83, 0xEC, 0x10, // sub rsp, 0x10
                0xf3, 0x0f, 0x7f, 0x14, 0x24, // movdqu [rsp], xmm2 // back up xmm2 on the stack, update rsp to match
                0x0f, 0x57, 0xd2, // xorps xmm2, xmm2 (to clear it)
                0xf3, 0xf, 0x6f, 0x15 }.AppendNum(caveDataOffset - 0x8 - 0x5 - 0x4 - 0x3)
            .Append(
                0x0f, 0x59, 0xc2, //mulps xmm0, xmm2
                0xf3, 0x0f, 0x6f, 0x14, 0x24, // movdqu xmm2,[rsp]
                0x48, 0x83, 0xc4, 0x10); // add rsp, 0x10

            byte[] caveBytes = newBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            long cavePointer = CodeCaveInjection(speedWritingInstr_ch, bytesToReplaceLength, caveBytes);
            CreatedCaves.Add((UnstableAirtimeId, cavePointer, StandardCaveSizeBytes));

            // Set the in place data
            AddressChain dataPointer = AddressChain.Absolute(Connector, cavePointer + caveDataOffset);
            dataPointer.Offset(0).SetFloat(speedFactor);
            dataPointer.Offset(4).SetFloat(speedFactor); // xmm0 has two floats and multiplies at the same time.

            return true;
        }

        /// <summary>
        /// Injects code that applies a factor to the damage received by player and other units.
        /// </summary>
        /// <param name="playerFactor">Factor by which the damage received by the player is multiplied.</param>
        /// <param name="othersFactor">Factor by which the damage received by the other units is multiplied.</param>
        /// <param name="instakillEnemies">If true, enemies receive a massive flat amount of damage when hit.</param>
        private bool InjectConditionalDamageMultiplier()
        {
            UndoInjection(OnDamageConditionalId);

            float playerFactor = PlayerReceivedDamageFactor;
            float othersFactor = OthersReceivedDamageFactor;
            bool instakillEnemies = InstakillEnemies;
            // Shields
            CcLog.Message("Injecting shields factor");
            InjectSpecificConditionalDamageMultiplier(0xba0475, 0x0E,
                0x15, // xmm2
                new byte[] { 0x49, 0x8B, 0xCE }, // mov rcx, r14,
                    0x9a3 - 0xa0,                //a0: shields. 0x9a3: distance to the player discriminator
                    playerFactor, othersFactor,
                    instakillEnemies);
            // Health.
            CcLog.Message("Injecting health factor");
            InjectSpecificConditionalDamageMultiplier(0xb9fdf3, 0x14,
                0x35, // xmm6
                new byte[] { 0x48, 0x8B, 0xCB },// mov rcx, rbx
                0x9a3, playerFactor, othersFactor,
                instakillEnemies);

            return true;
        }

        /// <summary>
        /// Performs the actual injection for <see cref="InjectConditionalDamageMultiplier(float, float, bool)"/>
        /// </summary>
        /// <param name="instructionOffset">Offset of the instruction where the code will be injected.</param>
        /// <param name="bytesToReplaceLength">How many bytes will be replaced at the injection point. Used to avoid breaking the next instructions.</param>
        /// <param name="damageRegister">Byte representing which register contains the damage value.</param>
        /// <param name="movPlayerPointingRegisterToRcxInstruction">
        ///     Instruction that takes the pointer to the player structure from a register,
        ///     and stores in rcx for our code to use. This pointer doesn't always point to the start of the structure, hence why we need
        ///     <paramref name="unitTypeDiscriminatorOffset"></paramref>.
        /// </param>
        /// <param name="unitTypeDiscriminatorOffset">
        ///     The offset in memory between the value used to determine if the unit is the player, and the pointer obtained
        ///     by the injected code.
        /// </param>
        /// <param name="playerFactor">See <see cref="InjectConditionalDamageMultiplier"/>.</param>
        /// <param name="othersFactor">See <see cref="InjectConditionalDamageMultiplier"/>.</param>
        /// <param name="instakillEnemies">See <see cref="InjectConditionalDamageMultiplier"/>.</param>
        private void InjectSpecificConditionalDamageMultiplier(int instructionOffset,
            int bytesToReplaceLength,
            byte damageRegister,
            byte[] movPlayerPointingRegisterToRcxInstruction,
            int unitTypeDiscriminatorOffset,
            float playerFactor,
            float othersFactor,
            bool instakillEnemies)
        {
            int playerDiscriminator = 0x3f; // 63
            int caveDataOffset = 0x130; // Address offset where the data will be stored in the new cave, with respect to its start.
            float ludicrousDamage = 0x7fffffffffffffff;

            // this is the previous instruction, so we can inject the jump without having to avoid overwriting a Jcc.
            var onDamageHealthSubstractionInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + instructionOffset);

            IntPtr unitStructurePointerPointer = CreateCodeCave(ProcessName, 8); // todo: change the offset to point to the structure start.
            CreatedCaves.Add((OnDamageConditionalId, (long)unitStructurePointerPointer, 8));

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength);
            ReplacedBytes.Add((OnDamageConditionalId, injectionAddress, originalBytes));

            // Checks if the current unit is the player or not, and jumps accordingly to apply the corresponding multiplication
            // (or replacing the damage value for instakills)
            byte[] prependedBytes = new byte[] {
                0x51 }// push rcx
            .Append(
                movPlayerPointingRegisterToRcxInstruction)
            .Append(
                0x48, 0x81, 0xc1).AppendNum(unitTypeDiscriminatorOffset) // mov rcx, <discriminator offset>
            .Append(
                0x81, 0x39).AppendNum(playerDiscriminator) // cmp [rcx], <discriminator value>
            .Append(
                0x59, // pop rcx
                0x75).AppendRelativePointer("enemyReceivedDamageMod", 0x0A) // jne to the original code start
            .Append(
                0xf3, 0x0f, 0x59, damageRegister).AppendNum(caveDataOffset - 28) // mulss xmm6, [hardcoded factor in program code]. xmm6 is the damage received here.
            .Append(
                0xEB).AppendRelativePointer("originalCode", 0x08 // Jmp to jump back to game code, so only the player received damage is updated.
            )
            .LocalJumpLocation("enemyReceivedDamageMod")
            .Append(instakillEnemies
            ?
                new byte[] { 0xF3, 0x0f, 0x10, damageRegister }.AppendNum(caveDataOffset - 38 + 8) // movss xmm6 the ludicrous damage value
            :
                new byte[] { 0xf3, 0x0f, 0x59, damageRegister }.AppendNum(caveDataOffset - 38 + 4) // mulss xmm6 by the second factor
            );

            byte[] caveBytes = prependedBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            long cavePointer = CodeCaveInjection(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength, caveBytes);
            CreatedCaves.Add((OnDamageConditionalId, cavePointer, StandardCaveSizeBytes));

            // Set the in place data
            AddressChain dataPointer = AddressChain.Absolute(Connector, cavePointer + caveDataOffset);
            dataPointer.Offset(0).SetFloat(playerFactor);
            dataPointer.Offset(4).SetFloat(othersFactor);
            dataPointer.Offset(8).SetFloat(ludicrousDamage);
        }

#if DEVELOPMENT

        /// <summary>
        /// Debug function, used to extract pointers to units without and storing them in memory, instead of needing a breakpoint.
        /// </summary>
        private void InjectUnitDirectionsExtractor()
        {
            // Parameterize it later.
            int unitTypeDiscriminatorOffset = 0x9a3;
            int playerDiscriminator = 0x3f; // 63

            // this is the previous instruction, so we can inject the jump without having to avoid overwriting a Jcc.
            var onDamageHealthSubstractionInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + 0xb9fdf3);
            int bytesToReplaceLength = 0x14;

            IntPtr unitStructurePointerPointer = CreateCodeCave(ProcessName, 8); // todo: change the offset to point to the structure start.
            CreatedCaves.Add((PlayerPointerId, (long)unitStructurePointerPointer, 8));

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength);
            ReplacedBytes.Add((OnDamageConditionalId, injectionAddress, originalBytes));
            CcLog.Message($"Pointer to unit: {((long)unitStructurePointerPointer).ToString("X")}");

            // Hooks to a place (a damage receiving function) that reads the pointer to the unit structure and stores it, unless it is the player's.
            byte[] prependedBytes = new byte[]
            {
                0x51, // push rcx
                0x48, 0x8b, 0xcb, // mov rcx, rbx
                0x48, 0x81, 0xc1 }.AppendNum(unitTypeDiscriminatorOffset) // mov rcx, <discriminator offset>
            .Append(
                0x81, 0x39).AppendNum(playerDiscriminator) // cmp [rcx], <discriminator value>
            .Append(
                0x74).AppendRelativePointer("endOfPrependedBytes", 0x0F).Append( // je to original bytes
                0x50, // push rax,
                0x48, 0x8b, 0xc1, // mov rax, rcx
                0x48, 0xA3).AppendNum((long)unitStructurePointerPointer) // mov rax to pointer location
            .Append(
                0x58, // pop rax
                0x59  // pop rcx
            );

            byte[] caveBytes = prependedBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            long cavePointer = CodeCaveInjection(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength, caveBytes);
            CreatedCaves.Add((OnDamageConditionalId, cavePointer, StandardCaveSizeBytes));
        }

#endif

        #endregion Injecters

        #region Instruction Manipulators and Generators

        /// <summary>
        /// Utility function to insert a byte array inside another byte array.
        /// </summary>
        /// <param name="bytes">Bytes where the new ones are inserted.</param>/param>
        /// <param name="bytesToInsert">Bytes to be inserted.</param>
        /// <param name="insertionPoint">Offset in <paramref name="bytes"/> where the insertion will be done.</param>
        /// <returns>The combined byte array.</returns>
        private IEnumerable<byte> SpliceBytes(IEnumerable<byte> bytes, IEnumerable<byte> bytesToInsert, int insertionPoint)
        {
            byte[] prependedBytes = bytes.Take(insertionPoint).ToArray();
            byte[] pospendedBytes = bytes.Skip(insertionPoint).ToArray();

            return prependedBytes.Concat(bytesToInsert).Concat(pospendedBytes);
        }

        /// <summary>
        /// Appends instructions at the end of <paramref name="bytes"/> to unconditionally jump to the given absolute address. Used
        /// mostly to go back to the injection point and resume execution.
        /// </summary>
        /// <returns>The byte array with the added jump.</returns>
        private byte[] AppendUnconditionalJump(byte[] bytes, long jumpDestionationAddress)
        {
            return bytes.Concat(GenerateJumpBytes(jumpDestionationAddress)).ToArray();
        }

        /// <summary>
        ///     Relative jump instructions are problematic on code caves. If their destination instruction is not also in the cave,
        ///     the relative jump must be replaced by an absolute jump. This function does that.
        /// </summary>
        /// <param name="bytes">Bytes containing the jump.</param>
        /// <param name="originalBytesStartingAddress">Absolute address of the original bytes, before any modification.</param>
        /// <param name="jccInstructionOriginalOffset">Offest of the jump instruction before any modification.</param>
        /// <param name="instructionLength">Length in bytes of the jump instruction.</param>
        /// <param name="opCodeLength">Length in bytes of the operatio ncode of the jump instrunction.</param>
        /// <param name="additionalOffsetAddedWhenModifyingBytes">
        ///     If the offset from the start is different to <paramref name="jccInstructionOriginalOffset"/> because
        ///     <see cref="bytes"/> was modified, this is that offset difference.
        /// </param>
        /// <returns>The byte array with the updated jump instruction.</returns>
        /// <exception cref="NotImplementedException">Thrown when the operation code has an unexpected length.</exception>
        private byte[] FixRelativeJccAfterRelocation(
            byte[] bytes,
            long originalBytesStartingAddress,
            int jccInstructionOriginalOffset,
            int instructionLength,
            int opCodeLength = 2,
            int additionalOffsetAddedWhenModifyingBytes = 0) // offsets added when modifying the bytes before the Jcc instruction
        {
            // This assumes that these "original bytes" already had the unconditional jump appended.
            byte[] jccInstructionBytes = bytes
                .Skip(jccInstructionOriginalOffset + additionalOffsetAddedWhenModifyingBytes)
                .Take(instructionLength).ToArray();

            byte[] jccOpCodeBytes = jccInstructionBytes.Take(opCodeLength).ToArray();
            var jumpAddressLength = instructionLength - opCodeLength;
            int jccRelativeJump = jumpAddressLength switch
            {
                1 => (int)jccInstructionBytes[opCodeLength],
                4 => BitConverter.ToInt32(jccInstructionBytes, opCodeLength),
                _ => throw new NotImplementedException("Relative jump fixing is not implemented for relative jump length " + jumpAddressLength)
            };

            // Calculate the address to the new position. Keep in mind this is calculated from the start of the NEXT instruction to the Jcc.
            long nextInstructionStart = originalBytesStartingAddress + jccInstructionOriginalOffset + instructionLength;
            int newRelativeJumpAddress = bytes.Length -
                (jccInstructionOriginalOffset + instructionLength + additionalOffsetAddedWhenModifyingBytes); // Appends at the end of the given bytes.
            long newAbsoluteJumpAddress = nextInstructionStart + jccRelativeJump;
            byte[] newAbsoluteJumpBytes = GenerateJumpBytes(newAbsoluteJumpAddress);

            byte[] fullOriginalBytesWithNewJump = bytes
                .Take(jccInstructionOriginalOffset + additionalOffsetAddedWhenModifyingBytes)
                .Concat(jccOpCodeBytes)
                .Concat(BitConverter.GetBytes(newRelativeJumpAddress).Take(jumpAddressLength))
                .Concat(bytes.Skip(jccInstructionOriginalOffset + instructionLength + additionalOffsetAddedWhenModifyingBytes))
                .Concat(newAbsoluteJumpBytes)
                .ToArray();

            return fullOriginalBytesWithNewJump;
        }

        /// <summary>
        /// Similarly to jump instructions, call instructions moved to a cave need to be changed to use absolute address.
        /// </summary>
        /// <param name="bytes">Byte array containing the call instruction.</param>
        /// <param name="callInstructionOffset">Offset of the call instruction.</param>
        /// <param name="callInstructionLength">Length of the call instruction.</param>
        /// <param name="bytesStartingAddress">Original absolute address of <paramref name="bytes"/>.</param>
        /// <returns>The byte array with the updated call instruction.</returns>
        /// <remarks>This function uses the R9 register. If it is used during the call, this function needs to be modified.</remarks>
        private (byte[] transformedBytes, int newAbsoluteCallLength)
            TransformRelativeCallToAbsoluteCall(byte[] bytes, int callInstructionOffset, int callInstructionLength, long bytesStartingAddress)
        {
            // using r9 as a register. Make sure it is not used during the call.
            byte[] callInstruction = bytes.Skip(callInstructionOffset).Take(callInstructionLength).ToArray();
            int relativeAddress = BitConverter.ToInt32(callInstruction, 1); // The call operand is always 1 byte
            long absoluteAddress = bytesStartingAddress + callInstructionOffset + callInstructionLength + relativeAddress;
            byte[] absoluteCall = new byte[]
            {
                0x41, 0x51, // push r9
                0x49, 0xb9 }.AppendNum(absoluteAddress) // mov r9, <absolute address>
            .Append(
                0x41, 0xff, 0xd1, // call r9,
                0x41, 0x59 // pop r9
            );

            byte[] transformedBytes = bytes.Take(callInstructionOffset).Concat(absoluteCall).Concat(bytes.Skip(callInstructionOffset + callInstructionLength)).ToArray();

            return (transformedBytes, absoluteCall.Length);
        }

        /// <summary>
        /// Generate the instructions for an unconditional jump to the given absolute address.
        /// </summary>
        /// <param name="jumpAddress">Absolute address to jump to.</param>
        /// <param name="replacedBytesLength">How many bytes in the original code are replaced, to verify there's enough space.</param>
        /// <returns>The generated instructions.</returns>
        /// <exception cref="Exception">Thrown if there's not enough space in the injection point for the jump instructions.</exception>
        private byte[] GenerateJumpBytes(long jumpAddress, int replacedBytesLength = 0)
        {
            // The minimum bytes required are 14 (aka 0xE)
            byte[] pushOpBytes = { 0x68 };
            byte[] movToStackPointerPlus04Bytes = { 0xC7, 0x44, 0x24, 0x04 };
            byte[] retOpBytes = { 0xC3 };
            byte[] caveAddressBytes = BitConverter.GetBytes(jumpAddress);
            byte[] caveAddressUpperBytes = caveAddressBytes.Skip(4).ToArray();
            byte[] caveAddressLowerBytes = caveAddressBytes.Take(4).ToArray();
            byte[] absoluteJumpBytes = pushOpBytes.Concat(caveAddressLowerBytes)
                .Concat(movToStackPointerPlus04Bytes).Concat(caveAddressUpperBytes)
                .Concat(retOpBytes).ToArray();

            if (replacedBytesLength != 0)
            {
                if (absoluteJumpBytes.Length > replacedBytesLength)
                {
                    throw new Exception("Jump bytes are longer than specified replaced bytes length");
                }

                absoluteJumpBytes = PadWithNops(absoluteJumpBytes, replacedBytesLength - absoluteJumpBytes.Length);
            }

            return absoluteJumpBytes;
        }

        /// <summary>
        /// Appends NOP instructions to fill a given size. This is necessary if the injected code
        /// has a different lenght to the replaced instructions, which would break the next instructions.
        /// </summary>
        /// <returns>The bytes with the added padding.</returns>
        private byte[] PadWithNops(byte[] bytes, int paddingLengthInBytes)
        {
            if (paddingLengthInBytes <= 0)
            {
                return bytes;
            }

            byte[] nopBytes = { 0x90 };
            IEnumerable<byte> paddedBytes = bytes.AsEnumerable();
            for (int i = 0; i < paddingLengthInBytes; i++)
            {
                paddedBytes = paddedBytes.Concat(nopBytes);
            }

            return paddedBytes.ToArray();
        }

        #endregion Instruction Manipulators and Generators

        #region Injection Helpers

        /// <summary>
        /// Restores the original code and clears the created caves for the task identified by the <paramref name="identifier"/>.
        /// </summary>
        /// <param name="identifier"></param>
        private void UndoInjection(string identifier)
        {
            foreach ((_, long injectionAddress, byte[] originalBytes) in ReplacedBytes.Where(x => x.Identifier == identifier))
            {
                CcLog.Message("Undoing injection");
                AddressChain.Absolute(Connector, injectionAddress).SetBytes(originalBytes);
            }

            ReplacedBytes = ReplacedBytes.Where(x => x.Identifier != identifier).ToList();

            foreach ((_, long caveAddress, int size) in CreatedCaves.Where(x => x.Identifier == identifier))
            {
                CcLog.Message("Removing cave");
                AddressChain.Absolute(Connector, caveAddress).SetBytes(Enumerable.Repeat((byte)0x00, size).ToArray());
                FreeCave(ProcessName, new IntPtr(caveAddress), size);
            }

            CreatedCaves = CreatedCaves.Where(x => x.Identifier != identifier).ToList();

            CcLog.Message("Undo complete");
        }

        /// <summary>
        /// Given a pointer and a byte length, take the byte array of that length from the pointer, and return it along with the
        /// absolute address of the pointer.
        /// </summary>
        /// <param name="injectionPoint_ch">The pointer.</param>
        /// <param name="bytesToReplaceLength">The byte length.</param>
        /// <returns>A tuple with the absolute address of the pointer and the retrieved original bytes.</returns>
        /// <exception cref="Exception"></exception>
        private (long address, byte[] originalBytes) GetOriginalBytes(AddressChain injectionPoint_ch, int bytesToReplaceLength)
        {
            if (!injectionPoint_ch.Calculate(out long injectionPointAddress))
            {
                throw new Exception("Injection point could not be calculated.");
            }

            byte[] originalBytes = injectionPoint_ch.GetBytes(bytesToReplaceLength);

            return (injectionPointAddress, originalBytes);
        }

        /// <summary>
        /// Given a pointer and a byte array, create a code cave, replace the bytes at the pointer with a jump to the cave and
        /// insert the byte array in the cave followed by a jump back to the code righ after the injected jump.
        /// </summary>
        /// <param name="injectionPoint_ch">The pointer.</param>
        /// <param name="bytesToReplaceLength">How many bytes to replace, to know if NOP padding is needed.</param>
        /// <param name="caveContents">Contents to insert in the code cave.</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private long CodeCaveInjection(AddressChain injectionPoint_ch, int bytesToReplaceLength, byte[] caveContents)
        {
            if (caveContents.Length > StandardCaveSizeBytes)
            {
                throw new Exception("Cave bytes are longer than standard allocation.");
            }

            IntPtr cavePointer = CreateCodeCave(ProcessName, StandardCaveSizeBytes);

            CcLog.Message("Cave location: " + ((long)cavePointer).ToString("X"));

            AddressChain codeCave_ch = AddressChain.Absolute(Connector, (long)cavePointer);

            codeCave_ch.SetBytes(caveContents);
            byte[] replacementBytes = GenerateJumpBytes((long)cavePointer, bytesToReplaceLength);

            if (!DONT_OVERWRITE)
            {
                injectionPoint_ch.SetBytes(replacementBytes);
            }

            return (long)cavePointer;
        }

        // Reserves a new space in memory in process of size cavesize, and returns a pointer to it.
        private IntPtr CreateCodeCave(string process, int cavesize)
        {
            // Near address does not seem to work, but I don't really need it I think.
            var proc = Process.GetProcessesByName(process)[0];

            if (proc == null)
            {
                throw new AccessViolationException("Process \"" + process + "\" not found.");
            }

            // https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights
            // PROCESS_VM_OPERATION, PROCESS_VM_WRITE
            var hndProc = OpenProcess(0x0008 | 0x0020, 1, proc.Id);

            // https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualallocex
            // Allocation type: MEM_COMMIT | MEM_RESERVE.
            // Protection is PAGE_EXECUTE_READWRITE
            IntPtr caveAddress;
            try
            {
                caveAddress = VirtualAllocEx(hndProc, (IntPtr)null, cavesize, 0x1000 | 0x2000, 0x40);
            }
            catch (Exception ex)
            {
                CcLog.Error("Something went wrong with the cave creation");
                return (IntPtr)null;
            }
            finally
            {
                CloseHandle(hndProc);
            }

            return caveAddress;
        }

        // Writes data to a cave.
        private int WriteToCave(string process, IntPtr caveAddress, byte[] code)
        {
            var proc = Process.GetProcessesByName(process)[0];

            var hndProc = OpenProcess(0x0008 | 0x0020, 1, proc.Id);

            return WriteProcessMemory(hndProc, caveAddress, code, code.Length, 0);
        }

        // Frees the memory used by a cave.
        private int FreeCave(string process, IntPtr caveAddress, int sizeInBytes)
        {
            var proc = Process.GetProcessesByName(process)[0];

            var hndProc = OpenProcess(0x0008, 1, proc.Id);

            var rel = VirtualFreeEx(hndProc, caveAddress, sizeInBytes, 0x00008000); // MEM_RELEASE

            if (rel) { return 1; } else { return 0; } // return 1 if succeeds, 0 if fails.
        }

        #endregion Injection Helpers

        #region Debug Helpers

        /// <summary>
        /// Writes a byte array as a hexadecimal string.
        /// </summary>
        /// <param name="bytes">Byte array to write.</param>
        /// <param name="header">Tooltip printed before the array.</param>
        private void WriteByteArray(byte[] bytes, string header = null)
        {
            StringBuilder s = new StringBuilder();
            if (header != null)
            {
                s.Append(header + ": ");
            }

            foreach (byte b in bytes)
            {
                s.Append(b.ToString("X2") + " ");
            }

            s.AppendLine();
            CcLog.Message($"{s}");
        }

        #endregion Debug Helpers

        // TODO: Actually verify all is set.
        protected override bool IsReady(EffectRequest request)
        {
            if (halo1BaseAddress != null)
            {
                CcLog.Message("Ready!");
                if (IsInGameplay())
                {
                    return true;
                }
            }

            CcLog.Message("Not ready.");
            return false;
        }

        /// <summary>
        /// Disables periodic injection checks, and undoes all current injections.
        /// </summary>
        private void AbortAllInjection()
        {
            CcLog.Message("Disabling periodic injection check, restoring memory and freeing caves.");
            injectionCheckerTimer.Enabled = false;
            foreach (var code in ReplacedBytes.Select(x => x.Identifier).Distinct())
            {
                UndoInjection(code);
            }
            // This second loop should be redundant, but just in case there's a cave not related to a replacement.
            foreach (var code in CreatedCaves.Select(x => x.Identifier).Distinct())
            {
                UndoInjection(code);
            }
        }

        private void HandleInvalidRequest(EffectRequest request)
        {
            CcLog.Message($"Invalid request: {FinalCode(request)}");
        }

        private bool IsInGameplay()
        {
            // TODO. Find a way to know if a player is paused, in the main menu, or a loading screen.
            return true;
        }

        protected override void StartEffect(EffectRequest request)
        {
            CcLog.Message("StartEffect started");
            CcLog.Message(FinalCode(request));
            var code = FinalCode(request).Split('_');

            switch (code[0])
            {
                #region Health and shields

                case "shield":
                    {
                        int offset = 0xA0;
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }

                        switch (code[1])
                        {
                            case "plus1":
                            case "minus1":
                                bool give = code[1] == "plus1";
                                TryEffect(request, IsInGameplay,
                                    () => TrySetIndirectFloat(give ? 1 : -1, basePlayerPointer_ch, offset, true),
                                    () => Connector.SendMessage($"{request.DisplayViewer} {(give ? "boosted" : "weakened")} your shield")); break;
                            case "break":
                                TryEffect(request, IsInGameplay,
                                    () => TrySetIndirectFloat(0, basePlayerPointer_ch, offset, false),
                                    () => Connector.SendMessage($"{request.DisplayViewer} broke your shield.")); break;
                            default:
                                HandleInvalidRequest(request); return;
                        }
                        break;
                    }
                case "shieldRegen":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        int offset = 0xC0;
                        switch (code[1])
                        {
                            case "no":
                            case "instant":
                                bool hinder = code[1] == "no";
                                RepeatAction(request,
                                    () => true,
                                    () => Connector.SendMessage($"{request.DisplayViewer}" +
                                        $" {(hinder ? "prevented your shield from recharging" : "gave you a fast regenerating shield.")}"),
                                    TimeSpan.FromSeconds(1),
                                    IsInGameplay,
                                    TimeSpan.FromMilliseconds(500),
                                    () => TrySetIndirectShort(hinder ? Int16.MaxValue : (short)0, basePlayerPointer_ch, offset, false),
                                    TimeSpan.FromMilliseconds(500),
                                    false,
                                    "shieldRegen").WhenCompleted.Then(_ =>
                                    {
                                        TrySetIndirectShort(0, basePlayerPointer_ch, offset, false);
                                        Connector.SendMessage("Shields are back to normal.");
                                    });
                                break;

                            default:
                                HandleInvalidRequest(request); return;
                        }
                        break;
                    }
                case "health":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        int offset = 0x9C;
                        bool heal = code[1] == "1";
                        TryEffect(request, IsInGameplay,
                                    () => TrySetIndirectFloat(heal ? 1 : 0.01f, basePlayerPointer_ch, offset, false),
                                    () => Connector.SendMessage($"{request.DisplayViewer} {(heal ? "healed you." : "left you on your last legs.")}")); break;
                    }
                case "healthRegen":
                    {
                        int offset = 0x9C;
                        RepeatAction(request,
                                    () => true,
                                    () => Connector.SendMessage($"{request.DisplayViewer} gave you health regeneration."),
                                    TimeSpan.FromSeconds(1),
                                    IsInGameplay,
                                    TimeSpan.FromMilliseconds(500),
                                    () => TrySetIndirectFloat(0.2f, basePlayerPointer_ch, offset, true),
                                    TimeSpan.FromMilliseconds(1000),
                                    false,
                                    "shieldRegen").WhenCompleted.Then(_ =>
                                    {
                                        Connector.SendMessage("Health regeneration ended.");
                                    });
                        break;
                    }

                #endregion Health and shields

                #region Movement speed changes

                case "playerspeed":
                    {
                        float speed;
                        string message;
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        if (code[1] == "brisk")
                        {
                            speed = 1.4f;
                            message = "put some spring in your step.";
                        }
                        else if (code[1] == "ludicrous")
                        {
                            speed = 6f;
                            message = "made you ludicrously fast.";
                        }
                        else if (code[1] == "slow")
                        {
                            speed = -0.5f;
                            message = "is grabbing your feet and you feel slow.";
                        }
                        else if (code[1] == "reversed")
                        {
                            speed = -2;
                            message = "made your legs very confused.";
                        }
                        else if (code[1] == "anchored")
                        {
                            speed = -1;
                            message = "anchored you in place.";
                        }
                        else
                        {
                            HandleInvalidRequest(request); return;
                        }
                        StartTimed(request, IsInGameplay,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} {message}");
                                PlayerSpeedFactor = speed;
                                return InjectSpeedMultiplier();
                            },
                            "playerSpeed")
                        .WhenCompleted.Then(_ =>
                        {
                            Connector.SendMessage($"Player speed back to normal.");

                            PlayerSpeedFactor = 1;
                            if (OthersSpeedFactor != 1)
                            {
                                InjectSpeedMultiplier();
                            }
                            else
                            {
                                UndoInjection(SpeedFactorId);
                            }
                        });

                        break;
                    }
                case "enemyspeed":
                    {
                        float speed;
                        string message;
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        if (code[1] == "ludicrous")
                        {
                            speed = 6f;
                            message = "made your enemies olympic sprinters.";
                        }
                        else if (code[1] == "reversed")
                        {
                            speed = -2f;
                            message = "made your enemies moonwalk.";
                        }
                        else if (code[1] == "anchored")
                        {
                            speed = -1f;
                            message = "anchored your enemies.";
                        }
                        else
                        {
                            HandleInvalidRequest(request); return;
                        }
                        StartTimed(request, IsInGameplay,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} {message}");
                                OthersSpeedFactor = speed;
                                return InjectSpeedMultiplier();
                            },
                            "othersSpeed")
                        .WhenCompleted.Then(_ =>
                        {
                            OthersSpeedFactor = 1;
                            if (PlayerSpeedFactor != 1)
                            {
                                InjectSpeedMultiplier();
                            }
                            else
                            {
                                UndoInjection(SpeedFactorId);
                            }
                            Connector.SendMessage($"Enemy speed back to normal.");
                        });

                        break;
                    }
                case "unstableairtime":
                    {
                        StartTimed(request, IsInGameplay,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} aggressively suggest you stay grounded.");
                                return InjectUnstableAirtime();
                            },
                            "playerSpeed")
                        .WhenCompleted.Then(_ =>
                        {
                            Connector.SendMessage($"You can jump safely again.");
                            UndoInjection(UnstableAirtimeId);
                        });
                        break;
                    }

                #endregion Movement speed changes

                #region Damage received modfiers

                case "enemyreceiveddamage":
                    {
                        float damageFactor;
                        bool instakill = false;
                        string message;
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        if (code[1] == "quad")
                        {
                            damageFactor = 4f;
                            message = "granted you QUAD DAMAGE. RIP AND TEAR.";
                        }
                        else if (code[1] == "ludicrous")
                        {
                            damageFactor = 99999f;
                            instakill = true;
                            message = "granted you the might to crush your enemies in one blow.";
                        }
                        else if (code[1] == "half")
                        {
                            damageFactor = 0.5f;
                            message = "gave your enemies twice the health and shields. The rascal!";
                        }
                        else if (code[1] == "reversed")
                        {
                            damageFactor = -1f;
                            message = "made your enemies get healed from any damage.";
                        }
                        else
                        {
                            HandleInvalidRequest(request); return;
                        }
                        StartTimed(request, IsInGameplay,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} {message}");
                                OthersReceivedDamageFactor = damageFactor;
                                InstakillEnemies = instakill;
                                return InjectConditionalDamageMultiplier();
                            },
                            "othersDamageReceived")
                        .WhenCompleted.Then(_ =>
                        {
                            OthersReceivedDamageFactor = 1;
                            InstakillEnemies = false;
                            if (PlayerReceivedDamageFactor != 1)
                            {
                                InjectConditionalDamageMultiplier();
                            }
                            else
                            {
                                UndoInjection(OnDamageConditionalId);
                            }
                            Connector.SendMessage($"Your damage is back to normal.");
                        });

                        break;
                    }
                case "playerreceiveddamage":
                    {
                        float damageFactor;
                        string message;
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        if (code[1] == "tenth")
                        {
                            damageFactor = 0.1f;
                            message = "made you almost bullet proof.";
                        }
                        else if (code[1] == "instadeath")
                        {
                            damageFactor = 99999f;
                            message = "made your enemies be able to blow you or your shields up in one hit. Good luck.";
                        }
                        else if (code[1] == "invulnerable")
                        {
                            damageFactor = 0f;
                            message = "made you IMMORTAL.";
                        }
                        else
                        {
                            HandleInvalidRequest(request); return;
                        }
                        StartTimed(request, IsInGameplay,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} {message}");
                                PlayerReceivedDamageFactor = damageFactor;
                                return InjectConditionalDamageMultiplier();
                            },
                            "playerDamageReceived")
                        .WhenCompleted.Then(_ =>
                        {
                            PlayerReceivedDamageFactor = 1;
                            if (OthersReceivedDamageFactor != 1 || InstakillEnemies == true)
                            {
                                InjectConditionalDamageMultiplier();
                            }
                            else
                            {
                                UndoInjection(OnDamageConditionalId);
                            }
                            Connector.SendMessage($"Enemy damage is back to normal.");
                        });

                        break;
                    }
                case "allreceiveddamage":
                    {
                        float playerDamageFactor;
                        float enemyDamageFactor;
                        bool instakillEnemies = false;
                        string message;
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        if (code[1] == "instadeath")
                        {
                            playerDamageFactor = 99999f;
                            enemyDamageFactor = 99999f;
                            instakillEnemies = true;
                            message = "made everyone fragile as glass. One hit kills anyone, including you. Keep your shields up!";
                        }
                        else if (code[1] == "invulnerable")
                        {
                            playerDamageFactor = 0f;
                            enemyDamageFactor = 0f;
                            message = "made everyone immortal. This is awkward.";
                        }
                        else if (code[1] == "glass")
                        {
                            playerDamageFactor = 3f;
                            enemyDamageFactor = 3f;
                            message = "made you do triple damage, but also take it.";
                        }
                        else
                        {
                            HandleInvalidRequest(request); return;
                        }
                        StartTimed(request,
                            () => { return IsInGameplay() && PlayerReceivedDamageFactor == 1 && OthersReceivedDamageFactor == 1 && !InstakillEnemies; },
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} {message}");
                                PlayerReceivedDamageFactor = playerDamageFactor;
                                OthersReceivedDamageFactor = enemyDamageFactor;
                                InstakillEnemies = instakillEnemies;

                                return InjectConditionalDamageMultiplier();
                            },
                            new string[] { "playerDamageReceived", "othersDamageReceived" })
                        .WhenCompleted.Then(_ =>
                        {
                            PlayerReceivedDamageFactor = 1;
                            OthersReceivedDamageFactor = 1;
                            InstakillEnemies = false;
                            UndoInjection(OnDamageConditionalId);

                            Connector.SendMessage($"All damage is back to normal.");
                        });

                        break;
                    }

                #endregion Damage received modfiers

                case "oneshotscripteffect":
                    {
                        if (code.Length < 2 || !int.TryParse(code[1], out int slot))
                        {
                            HandleInvalidRequest(request); return;
                        }

                        if (slot == 10)
                        {
                            // Replace it by one of the Malfunction codes.
                            List<int> nonDeactivatedParts = HudParts.Except(DisabledHubParts).ToList();
                            if (nonDeactivatedParts.Count == 0)
                            {
                                // TODO: refund.
                                return;
                            }
                            slot = nonDeactivatedParts[new Random().Next(nonDeactivatedParts.Count)];
                            DisabledHubParts.Add(slot);

                            // TODO: Make it reset on level load.
                        }

                        string message = slot switch
                        {
                            1 => "killed you in cold blood.",
                            2 => "crashed the game!",
                            3 => "reset you to the last chceckpoint.",
                            4 => "made you restart the level!",
                            5 => "dropped all vehicles on your head.",
                            6 => "made you go sneaky beaky like.",
                            7 => "had some good things on sale, stranger!",
                            8 => "beat this level for you!",
                            9 => "deployed the mother of all OOFs.",
                            10 => "disabled your crosshair.",// 10, 11, 12 and 13 are reserved for Malfunction
                            11 => "disabled your health meter.",
                            12 => "disabled your motion sensor.",
                            13 => "disabled your shields meter.",
                            14 => "repaired your HUD.",
                        };

                        string? mutex = slot switch
                        {
                            _ => null,
                        };

                        Action additionalStartAction = slot switch
                        {
                            14 => () => RepairHUD(),
                            _ => () => { }
                            ,
                        };

                        TryEffect(request, IsInGameplay,
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} {message}");
                                additionalStartAction();
                                return SetScriptOneShotEffectVariable(slot);
                            },
                            true,
                            mutex);

                        break;
                    }
                case "continuouseffect":
                    {
                        if (code.Length < 2 || !int.TryParse(code[1], out int slot) || slot < 0 || slot > 31)
                        {
                            HandleInvalidRequest(request); return;
                        }
                        string startMessage = slot switch
                        {
                            0 => "locked your armor.",
                            1 => "locked your armor, but \"forgot\" to include a shield.",
                            2 => "inverted your viewing controls.",
                            3 => "carefully delivered vehicles.",
                            4 => "told the AI to chill for a second",
                            5 => "gave you a jetpack.",
                            6 => "increased gravity.",
                            7 => "decreased gravity.",
                            8 => "boosted your jumps. Remember to roll!",
                            9 => "made you big.",
                            10 => "made you tiny",
                            11 => "granted you possession of whoever you touch.",
                            12 => "started an awkward moment",
                            13 => "turned you into a gorgon.",
                            14 => "gave you truly infinite ammo.",
                            15 => "commenced your ascension.",
                            16 => "turned off the lights.",
                            17 => "made all NPCs OSHA compliant.",
                            18 => "made all NPCs harder to see.",
                            19 => "brought out the popcorn.",
                            20 => "triggered something. Probably something bad.",
                            21 => "disabled your HUD.",
                            22 => "disabled your crosshair.",
                            23 => "blew up your eardrums.",
                            24 => "granted you the power to smite your foes in one blow.",
                            25 => "says you will die when they say, not before",
                        };

                        string endMessage = slot switch
                        {
                            0 => "Armor lock ended.",
                            1 => "Armor lock ended.",
                            2 => "Viewing controls back to normal",
                            3 => "Delivery complete.",
                            4 => "AI reactivated",
                            5 => "Jetpack removed. Hope you were not far from the ground.",
                            6 => "Gravity is back to normal.",
                            7 => "Gravity is back to normal.",
                            8 => "Jumps are back to normal",
                            9 => "You are a regular sized spartan once more.",
                            10 => "You are a regular sized spartan once more.",
                            11 => "It is now safe to touch people again.",
                            12 => "Well, the moment has passed. Back to work.",
                            13 => "It is now safe to gaze into thy eyes again",
                            14 => "Ammo is limited again",
                            15 => "Nevermind you're a total sinner.",
                            16 => "Let there be light once more.",
                            17 => "NPC visibility back to normal",
                            18 => "NPC visibility back to normal",
                            19 => "Fin.",
                            20 => "You regain confidence.",
                            21 => "HUD reactivated.",
                            22 => "Crosshair reactivated.",
                            23 => "Sound restored.",
                            24 => "Your damage is back to normal.",
                            25 => "You are mortal once more.",
                        };

                        string[]? mutex = slot switch
                        {
                            0 => new string[] { "armor lock", "playerDamageReceived" },
                            1 => new string[] { "armor lock" },
                            2 => new string[] { "viewing controls" },
                            4 => new string[] { "playerDamageReceived" },
                            5 => new string[] { "ai" },
                            6 => new string[] { "gravity" },
                            7 => new string[] { "gravity" },
                            9 => new string[] { "size" },
                            10 => new string[] { "size" },
                            12 => new string[] { "armor lock", "ai" },
                            14 => new string[] { "ammo" },
                            15 => new string[] { "gravity" },
                            17 => new string[] { "object_light_scale" },
                            18 => new string[] { "object_light_scale" },
                            _ => null,
                        };

                        Action additionalStartAction = slot switch
                        {
                            0 => () =>
                            {
                                PlayerReceivedDamageFactor = 0f;
                                InjectConditionalDamageMultiplier();
                            }
                            ,
                            3 => () =>
                            {
                                PlayerReceivedDamageFactor = 0f;
                                InjectConditionalDamageMultiplier();
                            }
                            ,
                            22 => () =>
                            {
                                DisabledHubParts.Add(Crosshair);
                                DisabledHubParts = DisabledHubParts.Distinct().ToList();
                            }
                            ,
                            _ => () => { }
                            ,
                        }; ;
                        Action additionalEndAction = slot switch
                        {
                            0 => () =>
                            {
                                PlayerReceivedDamageFactor = 1f;
                                InjectConditionalDamageMultiplier();
                            }
                            ,
                            3 => () =>
                            {
                                PlayerReceivedDamageFactor = 1f;
                                InjectConditionalDamageMultiplier();
                            }
                            ,
                            22 => () => RepairHUD(Crosshair)
                            ,
                            _ => () => { }
                            ,
                        };

                        var act = StartTimed(request,
                            () => true,// IsReady(request),
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} {startMessage}");
                                additionalStartAction();
                                return TrySetIndirectTimedEffectFlag(slot, 1);
                            },
                            mutex);

                        act.WhenCompleted.Then(_ =>
                        {
                            additionalEndAction();
                            TrySetIndirectTimedEffectFlag(slot, 0);
                            Connector.SendMessage(endMessage);
                        });

                        break;
                    }
#if DEVELOPMENT

                #region Manual injection

                case "abortallinjection":
                    {
                        AbortAllInjection();
                        break;
                    }
                case "speedfactorinjection":
                    {
                        InjectSpeedMultiplier();
                        break;
                    }
                case "toggleinjectioncheck":
                    {
                        injectionCheckerTimer.Enabled = !injectionCheckerTimer.Enabled;
                        CcLog.Message("Timer is now " + (injectionCheckerTimer.Enabled ? "Enabled" : "Disabled"));
                        break;
                    }
                case "injectscripthook":
                    {
                        InjectScriptHook();
                        break;
                    }
                case "undoscripthook":
                    {
                        UndoInjection(ScriptVarPointerId);
                        break;
                    }
                case "playerpointerinjection":
                    {
                        InjectPlayerBaseReader();
                        break;
                    }
                case "undoplayerpointerinjection":
                    {
                        UndoInjection(PlayerPointerId);
                        break;
                    }
                case "damageconditionalinjection":
                    {
                        InjectConditionalDamageMultiplier();
                        break;
                    }
                case "undodamageconditionalinjection":
                    {
                        UndoInjection(OnDamageConditionalId);
                        break;
                    }
                case "unitstructurelocatoractivate":
                    {
                        InjectUnitDirectionsExtractor();
                        break;
                    }

                #endregion Manual injection

                #region Player

                case "hyperovershield":
                    {
                        CcLog.Message("Triggered hyperovershield");
                        TrySetIndirectFloat(3.14f, basePlayerPointer_ch, 0xA0, false);
                        break;
                    }

                case "noshield":
                    {
                        CcLog.Message("Triggered minimum shield");
                        TrySetIndirectFloat(0.01f, basePlayerPointer_ch, 0xA0, false);
                        break;
                    }

                case "noshieldrecharge":
                    {
                        // Note: this value gets reset when receiving damage.
                        CcLog.Message("Triggered no shield recharge.");
                        TrySetIndirectShort(Int16.MaxValue, basePlayerPointer_ch, 0xC0, false);
                        break;
                    }
                case "setto1hp":
                    {
                        CcLog.Message("Pain inbound");
                        TrySetIndirectFloat(0.01f, basePlayerPointer_ch, 0x9C, false);

                        break;
                    }
                case "heal":
                    {
                        CcLog.Message("Healthy spartan");
                        TrySetIndirectFloat(1f, basePlayerPointer_ch, 0x9C, false);

                        break;
                    }

                #endregion Player

                #region Scripting

                case "oneshotscripteffectgeneric":
                    {
                        if (code.Length < 2 || !int.TryParse(code[1], out int slot))
                        {
                            CcLog.Error("Missing or incorrect slot number for one-shot script effect");
                            break;
                        }

                        Connector.SendMessage($"{request.DisplayViewer} triggered one-shot effect {slot}.");
                        SetScriptOneShotEffectVariable(slot);
                        break;
                    }
                case "resetscriptcommvar":
                    {
                        SetScriptOneShotEffectVariable(789); break;
                    }
                case "continuouseffectgeneric":
                    {
                        if (code.Length < 2 || !int.TryParse(code[1], out int slot))
                        {
                            CcLog.Error("Missing or incorrect slot number for continuous script effect");
                            break;
                        }

                        CcLog.Message(request);
                        CcLog.Message(request.Duration);
                        CcLog.Message(request.Duration.TotalMilliseconds);
                        CcLog.Message(request.DisplayViewer);
                        var act = StartTimed(request,
                            () => true,// IsReady(request),
                            () =>
                            {
                                //Connector.SendMessage($"{request.DisplayViewer} triggered continous effect {slot}.");
                                return TrySetIndirectTimedEffectFlag(slot, 1);
                            },
                            Guid.NewGuid().ToString());

                        act.WhenCompleted.Then(_ =>
                        {
                            TrySetIndirectTimedEffectFlag(slot, 0);
                            //Connector.SendMessage($"Continous effect {slot} ended.");
                        });
                        break;
                    }

                #endregion Scripting

#endif

                default:
                    CcLog.Message("Triggered nothing");
                    break;
            }
        }

        #region Indirect Pointer Helpers

        /// <summary>
        /// Tries to set or unset a flag for the timed H1 script effects.
        /// </summary>
        /// <param name="bitOffset">Offset of the flag.</param>
        /// <param name="bitValue">1 or 0.</param>
        /// <returns>True if the flag was set correctly.</returns>
        /// <exception cref="ArgumentOutOfRangeException">On invalid offset or bit value.</exception>
        private bool TrySetIndirectTimedEffectFlag(int bitOffset, int bitValue)
        {
            if (bitValue < 0 || bitValue > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bitValue));
            }

            if (bitOffset < 0 || bitOffset > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(bitValue));
            }

            if (VerifyIndirectPointer(scriptVarTimedEffectsPointerPointer, out AddressChain? valueRealPointer_ch))
            {
                if (!valueRealPointer_ch.TryGetInt(out int oldValue))
                {
                    CcLog.Error($"Could not read flags value in address {valueRealPointer_ch.GetLong()}, memory may have been destroyed.");
                    return false;
                }

                //CcLog.Message("Value read: " + oldValue);

                int newFlagsValue = SetBit(bitValue, bitOffset, oldValue);

                if (!valueRealPointer_ch.TrySetInt(newFlagsValue))
                {
                    return TroubleShootDirectPointerWrite(valueRealPointer_ch, newFlagsValue);
                }
            }

            return true;
        }

        /// <summary>
        /// Set a bit on a number.
        /// </summary>
        /// <returns>The updated number.</returns>
        private int SetBit(int bitValue, int bitOffset, int number)
        {
            int newFlagsValue = number;
            int mask = 1 << bitOffset;

            if (bitValue == 1)
            {
                CcLog.Message("Setting bit with offset " + bitOffset);
                newFlagsValue |= mask; // Set bit.
            }
            else
            {
                CcLog.Message("Clearing bit with offset " + bitOffset);
                newFlagsValue &= ~mask; // Clear bit.
            }

            CcLog.Message("New value: " + newFlagsValue.ToString("X"));

            return newFlagsValue;
        }

        /// <summary>
        /// Float wrapper of <see cref="TrySetIndirectValue"/>.
        /// </summary>
        private bool TrySetIndirectFloat(float newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            Func<AddressChain, (bool, float)> tryGetter = (pointerPointer_ch) => (pointerPointer_ch.TryGetFloat(out float oldValue), oldValue); ;
            Func<AddressChain, float, bool> trySetter = (pointerPointer_ch, newValue) => pointerPointer_ch.TrySetFloat(newValue);
            Func<float, float, float> adder = (a, b) => a + b;

            return TrySetIndirectValue<float>(tryGetter, trySetter, adder, newValue, valuePointerPointer_ch, offset, isRelative);
        }

        /// <summary>
        /// Int32 wrapper of <see cref="TrySetIndirectValue"/>.
        /// </summary>
        private bool TrySetIndirectInt32(int newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            Func<AddressChain, (bool, int)> tryGetter = (pointerPointer_ch) => (pointerPointer_ch.TryGetInt(out int oldValue), oldValue);
            Func<AddressChain, int, bool> trySetter = (pointerPointer_ch, newValue) => pointerPointer_ch.TrySetInt(newValue);
            Func<int, int, int> adder = (a, b) => a + b;

            return TrySetIndirectValue<int>(tryGetter, trySetter, adder, newValue, valuePointerPointer_ch, offset, isRelative);
        }

        /// <summary>
        /// Short wrapper of <see cref="TrySetIndirectValue"/>.
        /// </summary>
        private bool TrySetIndirectShort(short newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            Func<AddressChain, (bool, short)> tryGetter = (pointerPointer_ch) => (pointerPointer_ch.TryGetShort(out short oldValue), oldValue);
            Func<AddressChain, short, bool> trySetter = (pointerPointer_ch, newValue) => pointerPointer_ch.TrySetShort(newValue);
            Func<short, short, short> adder = (a, b) => (short)(a + b);

            return TrySetIndirectValue<short>(tryGetter, trySetter, adder, newValue, valuePointerPointer_ch, offset, isRelative);
        }

        /// <summary>
        /// Given a pointer to an absolute memory address, try to set the value pointed by that memory address.
        /// </summary>
        /// <typeparam name="T">Type of value.</typeparam>
        /// <param name="tryGetter">Function to get the value from the pointer.</param>
        /// <param name="trySetter">Function to set the value from the pointer.</param>
        /// <param name="adder">Function to add the new value with the old one if needed.</param>
        /// <param name="newValue">Value to set.</param>
        /// <param name="valuePointerPointer_ch">Pointer to the pointer to the value.</param>
        /// <param name="offset">Offset to add to the address referred by <paramref name="valuePointerPointer_ch"/>.</param>
        /// <param name="isRelative">If true, the new value will be the old value + the new one.</param>
        /// <returns>True if successful, false otherwise.</returns>
        private bool TrySetIndirectValue<T>(Func<AddressChain, (bool, T)> tryGetter,
            Func<AddressChain, T, bool> trySetter,
            Func<T, T, T> adder,
            T newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            if (VerifyIndirectPointer(valuePointerPointer_ch, out AddressChain? valueRealPointer_ch))
            {
                valueRealPointer_ch = valueRealPointer_ch!.Offset(offset);

                (bool success, T oldValue) = tryGetter(valueRealPointer_ch);
                if (!success)
                {
                    if (!valueRealPointer_ch.Calculate(out long address))
                    {
                        CcLog.Error("Real pointer from indirect pointer can't be calculated.");
                    }

                    CcLog.Error($"Could not read value in address {address}, memory may have been destroyed.");
                    return false;
                }

                //CcLog.Message("Value read: " + oldValue);

                if (isRelative)
                {
                    newValue = adder(newValue, oldValue);
                }

                if (!trySetter(valueRealPointer_ch, newValue))
                {
                    return TroubleShootDirectPointerWrite(valueRealPointer_ch, newValue);
                }
            }

            return true;
        }

        /// <summary>
        /// Check and report why a direct pointer could not set a value.
        /// </summary>
        /// <typeparam name="T">Type of value that the pointer tried to set.</typeparam>
        /// <param name="directPointer">Pointer.</param>
        /// <param name="writtenValue">Value that was attempted to set, to help debugging.</param>
        /// <returns>True if the pointer itself is valid.</returns>
        private bool TroubleShootDirectPointerWrite<T>(AddressChain directPointer, T writtenValue)
        {
            CcLog.Error($"Value could not be set to value {writtenValue}");

            bool success = directPointer.Calculate(out long address);
            if (!success)
            {
                CcLog.Error($"Real pointer can't even be reached");
            }

            return false;
        }

        /// <summary>
        /// Verifies that an indirect pointer is valid, and calculates the direct pointer from it.
        /// </summary>
        /// <param name="valuePointerPointer_ch">Indirect pointer.</param>
        /// <param name="valueRealPointer_ch">Output variable for the direct pointer.</param>
        /// <returns>True if all is correct.</returns>
        private bool VerifyIndirectPointer(AddressChain valuePointerPointer_ch, out AddressChain? valueRealPointer_ch)
        {
            valueRealPointer_ch = null;
            if (valuePointerPointer_ch == null)
            {
                CcLog.Error("Indirect pointer for this value is still not set.");
                return false;
            }
            if (!TryGetRealValuePointer(valuePointerPointer_ch, out valueRealPointer_ch, 0))
            {
                CcLog.Error("Could not get pointer to value.");
                return false;
            }
            if (valueRealPointer_ch == null)
            {
                CcLog.Error("Direct pointer to value was null");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get a direct pointer from an indirect one.
        /// </summary>
        /// <param name="valuePointerPointer_ch">Indirect pointer.</param>
        /// <param name="valuePointer_ch">(output) Direct pointer.</param>
        /// <param name="offset">Offset to apply to the direct pointer.</param>
        /// <returns>True if everything is correct.</returns>
        private bool TryGetRealValuePointer(AddressChain valuePointerPointer_ch, out AddressChain? valuePointer_ch, int offset)
        {
            valuePointer_ch = null;
            if (!valuePointerPointer_ch.TryGetBytes(8, out byte[] valueAddressBytes))
            {
                CcLog.Error("Indirect pointer had no proper value.");

                return false;
            }

            long address = BitConverter.ToInt64(valueAddressBytes);

            //CcLog.Message("Value address: " + address.ToString("X"));
            valuePointer_ch = AddressChain.Absolute(Connector, address).Offset(offset);

            return true;
        }

        /// <summary>
        /// Sets the script variable for one-shot effects to a value that indicates the script to run a specific effect.
        /// </summary>
        /// <param name="scriptIndex">A value from 0 to 788 representing the effect to run.</param>
        private bool SetScriptOneShotEffectVariable(int scriptIndex)
        {
            return TrySetIndirectInt32(123456000 + scriptIndex, scriptVarInstantEffectsPointerPointer_ch, 0, false);
        }

        #endregion Indirect Pointer Helpers

        #region Imports

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, int bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern int CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddres, int dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        private static extern int WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer, int size, int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, int dwFreeType);

        #endregion Imports
    }

    /* Notes:
     * When using data in the cave, the jump is calculated by the cave data offset - the offset of the next instruction.
    */
}