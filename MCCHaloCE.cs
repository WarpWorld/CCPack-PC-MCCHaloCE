//ccpragma { "include" : [ "Effects/Implementations/Ammo.cs", "Effects/Implementations/MouseOverride.cs", "Effects/Implementations/KeyOverride.cs", "Effects/Implementations/ReceivedDamage.cs", "Effects/Implementations/H1ScriptEffects.cs", "Effects/Implementations/ApplyForces.cs", "Effects/Implementations/MovementSpeed.cs", "Effects/Implementations/UnstableAirtime.cs", "Effects/Implementations/PlayerPointerBased.cs", "Effects/EffectMutex.cs", "Effects/CursedHaloEffectList.cs", "DllImports.cs", "Utilities/IndirectPointers.cs", "Utilities/InjectionManagement.cs", "LifeCycle/BaseHaloAddressResult.cs", "LifeCycle/IntegrityControl.cs", "Utilities/Debug.cs", "Utilities/ByteArrayBuilding/ByteArrayExtensions.cs", "Utilities/ByteArrayBuilding/InstructionManipulation.cs", "Utilities/InputEmulation/KeyManager.cs", "Utilities/InputEmulation/KeybindData.cs","Utilities/InputEmulation/GameAction.cs", "Utilities/InputEmulation/User32Imports/InputStructs.cs", "Utilities/InputEmulation/User32Imports/MouseEventFlags.cs","Injections/Player.cs", "Injections/DamageModifier.cs", "Injections/ScriptHooks.cs", "Injections/MovementSpeed.cs", "Injections/UnstableAirtime.cs", "Injections/GameplayPolling.cs", "Injections/LevelSkipper.cs", "Injections/Weapon.cs"] }
//#define DEVELOPMENT

using ConnectorLib;
using ConnectorLib.Inject.AddressChaining;
using ConnectorLib.Inject.VersionProfiles;
using CrowdControl.Common;
using CrowdControl.Games.Packs.Effects;
using System;
using System.Collections.Generic;
using CcLog = CrowdControl.Common.Log;
using ConnectorType = CrowdControl.Common.ConnectorType;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE : InjectEffectPack
    {
        private const string ProcessName = "MCC-Win64-Shipping";

        private void BringGameToForeground()
        {
            _ = SetForegroundWindow(mccProcess.MainWindowHandle);
        }

        public override Game Game { get; } = new("MCC Halo Combat Evolved", "MCCHaloCE", "PC", ConnectorType.PCConnector);

        private KeyManager keyManager;

        public override EffectList Effects => CursedHaloEffectList.Effects;

        public MCCHaloCE(UserRecord player, Func<CrowdControlBlock, bool> responseHandler, Action<object> statusUpdateHandler)
            : base(player, responseHandler, statusUpdateHandler)
        {
            VersionProfiles = new List<VersionProfile>
            {
                new(ProcessName, InitGame, DeinitGame, null, ConnectorLib.Inject.Payload.DirectX.Direct3DVersion.Direct3D9 ),
            };

            instance = this;
            keyManager = new KeyManager();
        }

        private void InitGame()
        {
            CcLog.Message("INIT");
            this.keyManager.connector = Connector;
            var hidConnector = new HIDConnector();
            hidConnector.Connect(3, TimeSpan.FromSeconds(5));
            this.keyManager.hidConnector = hidConnector;
            InitIntegrityControl();
        }

        private void DeinitGame()
        {
            CcLog.Message("DEINIT");
            AbortAllInjection(true);
        }

        // TODO: Actually verify all is set.
        protected override bool IsReady(EffectRequest request)
        {
            if (!IsInGameplay())
            {
                CcLog.Message("Not in gameplay");

                return false;
            }

            var code = FinalCode(request).Split('_');

            if (code[0] == "continuouseffect" && !VerifyIndirectPointerIsReady(scriptVarTimedEffectsPointerPointer_ch))
            {
                CcLog.Message("No timed script pointer found");
            }

            if (code[0] == "oneshotscripteffect" && !VerifyIndirectPointerIsReady(scriptVarInstantEffectsPointerPointer_ch))
            {
                CcLog.Message("No one-shot script pointer found");
                return false;
            }

            if (!VerifyIndirectPointerIsReady(basePlayerPointer_ch))
            {
                CcLog.Message("No player pointer found");
                return false;
            }

            return IsProcessReady;
        }

        private bool VerifyIndirectPointerIsReady(AddressChain pointer)
        {
            return pointer != null
                && pointer.TryGetLong(out long value)
                && value != 0;
        }

        private void HandleInvalidRequest(EffectRequest request)
        {
            CcLog.Message($"Invalid request: {FinalCode(request)}");
        }

        private bool IsInGameplay()
        {
            return currentlyInGameplay;
        }

        protected override void StartEffect(EffectRequest request)
        {
            CcLog.Message("StartEffect started");
            CcLog.Message(FinalCode(request));
            var code = FinalCode(request).Split('_');

            switch (code[0])
            {
                case "takeammo":
                    if (code.Length < 2) { HandleInvalidRequest(request); return; }

                    switch (code[1])
                    {
                        case "half": TakeAwayAmmoFromCurrentWeapon(0.5f); return;
                        case "all": TakeAwayAmmoFromCurrentWeapon(1f); return;
                        case "duplicate": TakeAwayAmmoFromCurrentWeapon(-1); return;
                    }
                    break;

                case "fullauto":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }

                        switch (code[1])
                        {
                            case "limitedammo": FullAuto(request, false); return;
                            case "unlimitedammo": FullAuto(request, true); return;
                        }
                        break;
                    }
                case "shield":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }

                        switch (code[1])
                        {
                            case "plus1":
                                AddShield(request, 1, "boosted"); break;
                            case "minus1":
                                AddShield(request, -1, "weakened"); break;
                            case "break":
                                SetShield(request, 0); break;
                            default:
                                HandleInvalidRequest(request); return;
                        }
                        break;
                    }
                case "shieldRegen":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "no":
                                SetShieldRegen(request, ShieldRegenEffectType.No); break;
                            case "instant":
                                SetShieldRegen(request, ShieldRegenEffectType.Instant); break;
                            default:
                                HandleInvalidRequest(request); return;
                        }
                        break;
                    }
                case "health":
                    {
                        switch (code[1])
                        {
                            case "1": SetHealth(request, 1, "healed you."); break;
                            case "min": SetHealth(request, 0.01f, "left you on your last legs."); break;
                            default: HandleInvalidRequest(request); return;
                        }
                        break;
                    }
                case "criticalhealth": OneHealthAndADream(request); break;
                case "healthRegen":
                    {
                        GiveHealthRegen(request, 0.2f, 1000); break;
                    }
                case "grenades":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "give": GiveGrenades(request, 6, false, "gave you"); break;
                            case "take": GiveGrenades(request, -6, false, "took away"); break;
                            default: HandleInvalidRequest(request); return;
                        }
                        break;
                    }
                case "playerspeed":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "brisk": SetPlayerMovementSpeed(request, 1.4f, "\"put some spring in your step.\""); break;
                            case "ludicrous": SetPlayerMovementSpeed(request, 6f, "made you ludicrously fast."); break;
                            case "slow": SetPlayerMovementSpeed(request, -0.5f, "is grabbing your feet and you feel slow."); break;
                            case "reversed": SetPlayerMovementSpeed(request, -2f, "made your legs very confused."); break;
                            case "anchored": SetPlayerMovementSpeed(request, -1f, "anchored you in place."); break;
                        }
                        break;
                    }
                case "enemyspeed":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "ludicrous": SetNPCMovementSpeed(request, 6f, "made your enemies olympic sprinters."); break;
                            case "reversed": SetNPCMovementSpeed(request, -2f, "made your enemies moonwalk."); break;
                            case "anchored": SetNPCMovementSpeed(request, -1f, "anchored your enemies."); break;
                        }
                        break;
                    }
                case "unstableairtime": ActivateUnstableAirtime(request); break;
                case "enemyreceiveddamage":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "quad": SetDamageFactors(request, null, 4, null, "granted you QUAD DAMAGE. RIP AND TEAR."); break;
                            case "ludicrous": SetDamageFactors(request, null, 99999f, true, "granted you the might to crush your enemies in one blow."); break;
                            case "half": SetDamageFactors(request, null, 0.5f, null, "gave your enemies twice the health and shields. The rascal!"); break;
                            case "reversed": SetDamageFactors(request, null, -1f, null, "made all NPC get healed from any damage."); break;
                            case "immortal": SetDamageFactors(request, null, 0, false, "made all NPCs immortal."); break;
                        }
                        break;
                    }
                case "playerreceiveddamage":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "tenth": SetDamageFactors(request, 0.1f, null, null, "made you almost bullet proof."); break;
                            case "instadeath": SetDamageFactors(request, 9999f, null, null, "made your enemies be able to blow you or your shields up in one hit. Good luck."); break;
                            case "invulnerable": SetDamageFactors(request, 0f, null, null, "made you IMMORTAL."); break;
                        }
                        break;
                    }
                case "allreceiveddamage":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "instadeath": SetDamageFactors(request, 99999f, 99999f, true, "made everyone fragile as glass. One hit kills anyone, including you. Keep your shields up!"); break;
                            case "invulnerable": SetDamageFactors(request, 0, 0, null, "made everyone immortal. This is awkward."); break;
                            case "glass": SetDamageFactors(request, 3f, 3f, null, "made you do triple damage, but also take it."); break;
                        }
                        break;
                    }
                case "addspeed":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "shove1": ApplyRandomForce(0.5f, 0.5f, 0.15f); break;
                            case "shake": ShakePlayer(request, 0.4f, 35, "is shaking you.", "The shakes are over"); break;
                            case "drunk": ShakePlayer(request, 0.15f, 800, "gave you one too many drinks.", "Drunkness over, enjoy the hangover."); break;
                        }
                        break;
                    }
                case "oneshotscripteffect":
                    {
                        if (code.Length < 2 || !int.TryParse(code[1], out int slot))
                        {
                            HandleInvalidRequest(request); return;
                        }

                        // TODO: FIND A WAY TO FIX MALFUNCTION NOT WORKING ON LEVEL CHANGE.
                        ApplyOneShotEffect(request, slot);
                        break;
                    }
                case "continuouseffect":
                    {
                        if (code.Length < 2 || !int.TryParse(code[1], out int slot) || slot < 0 || slot > 31)
                        {
                            HandleInvalidRequest(request); return;
                        }

                        ApplyContinuousEffect(request, slot);
                        break;
                    }
                case "crabrave": CrabRave(request); break;
                case "moonwalk": Moonwalk(request); break;
                case "forcerepeatedjump": BunnyHop(request); break;
                case "forcefire": CeaselessDischarge(request); break;
                case "forcegrenades": ForceGrenades(request); break;
                case "preventattacking": Pacifist(request); break;
                case "reversemovement": ReverseMovementKeys(request); break;
                case "randomizecontrols": RandomizeControls(request); break;
                case "turretmode": TurretMode(request); break;
                case "forcecrouch": ForceCrouch(request); break;
                case "berserker":
                    {
                        // TODO: once fully defined, move to wherever it goes.
                        int deathlessSlot = 25;
                        int oneShotOneKillSlot = 24;

                        RepeatAction(request,
                            startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
                            startAction: () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} told you to RIP AND TEAR.");
                                // Deathless effect
                                TrySetIndirectTimedEffectFlag(25, 1);
                                // Omnipotent effect
                                TrySetIndirectTimedEffectFlag(24, 1);

                                // Movement speed
                                SetPlayerMovementSpeed(request, 1.5f, "made you fast.");

                                // Keybinds
                                keyManager.SetAlernativeBindingToOTherActions(GameAction.Melee, GameAction.Fire);
                                keyManager.DisableAction(GameAction.Fire);
                                keyManager.DisableAction(GameAction.ThrowGrenade);
                                keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                                BringGameToForeground();
                                keyManager.ForceShortPause();
                                return true;
                            },
                            startRetry: TimeSpan.FromSeconds(1),
                            refreshCondition: () => IsInGameplay(),
                            refreshRetry: TimeSpan.FromMilliseconds(1000),
                            refreshAction: () =>
                            {
                                // Deathless effect
                                TrySetIndirectTimedEffectFlag(25, 1);
                                // Omnipotent effect
                                TrySetIndirectTimedEffectFlag(24, 1);
                                return true;
                            },
                            refreshInterval: TimeSpan.FromMilliseconds(33),
                            extendOnFail: false,
                            mutex: new string[] { EffectMutex.KeyDisable, EffectMutex.PlayerSpeed, EffectMutex.PlayerReceivedDamage, EffectMutex.Ammo, EffectMutex.NPCReceivedDamage })
                            .WhenCompleted.Then((task) =>
                            {
                                // Repair health and shields.
                                SetHealth(request, 1, "replenished your health.");
                                SetShield(request, 1);

                                // Deathless remove
                                TrySetIndirectTimedEffectFlag(25, 0);
                                // Omnipotent effect
                                TrySetIndirectTimedEffectFlag(24, 0);

                                // Keybinds
                                keyManager.RestoreAllKeyBinds();
                                keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                                keyManager.ResetAlternativeBindingForAction(GameAction.Melee, halo1BaseAddress);
                                BringGameToForeground();
                                keyManager.ForceShortPause();
                                Connector.SendMessage($"You can calm down now.");
                            });
                        break;
                    }
                case "forcemouse":
                    {
                        if (code.Length < 2) { HandleInvalidRequest(request); return; }
                        switch (code[1])
                        {
                            case "down": ApplyMovementEveryFrame(request, 0, 130, "made your feet quite interesting.", "Foot fetish erradicated."); break;
                            case "up": ApplyMovementEveryFrame(request, 0, -130, "put your hands up to the sky.", "Your arms are too tired for this."); break;
                            case "spin": ApplyMovementEveryFrame(request, 130, 0, "started the S.P.E.E.N. protocol.", "S.P.E.E.N. protocol completed"); break;
                            case "drift":
                                Random rng = new Random();
                                int dx = rng.Next(-40, 40);
                                int dy = rng.Next(-40, 40);

                                ApplyMovementEveryFrame(request, dx, dy, "made your joycon drift. Yes, on keyboard and mouse.", "fixed your joycon.");
                                break;
                        }
                        break;
                    }
                case "forcemouseshake": ForceMouseShake(request, 120, 0.8f, 3); break;
                case "movetohalo":
                    {
                        TryEffect(request, () => IsReady(request),
                            () =>
                            {
                                Connector.SendMessage($"{request.DisplayViewer} sent you to Halo.");
                                SetNextMap(2);
                                return SetScriptOneShotEffectVariable(8); // Slipspace jump.
                            },
                            true,
                            EffectMutex.LevelChangeOrRestart);

                        break;
                    }
#if DEVELOPMENT
                case "abortallinjection":
                    {
                        AbortAllInjection(true);
                        break;
                    }
#endif
                default:
                    CcLog.Message("Triggered nothing");
                    break;
            }
        }
    }

    /* Notes:
     * When using data in the cave, the jump is calculated by the cave data offset - the offset of the next instruction.
    */
}