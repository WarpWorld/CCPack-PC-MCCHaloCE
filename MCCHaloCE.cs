//ccpragma { "include" : [ "Effects/ContinuousEffect.cs","Effects/OneShotEffect.cs","Effects/Implementations/ComplexEffects.cs","Effects/Implementations/Ammo.cs", "Effects/Implementations/MouseOverride.cs", "Effects/Implementations/KeyOverride.cs", "Effects/Implementations/ReceivedDamage.cs", "Effects/Implementations/H1ScriptEffects.cs", "Effects/Implementations/ApplyForces.cs", "Effects/Implementations/MovementSpeed.cs", "Effects/Implementations/UnstableAirtime.cs", "Effects/Implementations/PlayerPointerBased.cs", "Effects/EffectMutex.cs", "Effects/CursedHaloEffectList.cs", "DllImports.cs", "Utilities/IndirectPointers.cs", "Utilities/InjectionManagement.cs", "LifeCycle/BaseHaloAddressResult.cs", "LifeCycle/IntegrityControl.cs", "Utilities/Debug.cs", "Utilities/ByteArrayBuilding/ByteArrayExtensions.cs", "Utilities/ByteArrayBuilding/InstructionManipulation.cs", "Utilities/InputEmulation/KeyManager.cs", "Utilities/InputEmulation/KeybindData.cs","Utilities/InputEmulation/GameAction.cs", "Utilities/InputEmulation/User32Imports/InputStructs.cs", "Utilities/InputEmulation/User32Imports/MouseEventFlags.cs","Injections/Player.cs", "Injections/DamageModifier.cs", "Injections/ScriptHooks.cs", "Injections/MovementSpeed.cs", "Injections/UnstableAirtime.cs", "Injections/GameplayPolling.cs", "Injections/LevelSkipper.cs", "Injections/Weapon.cs"] }
#define DEVELOPMENT

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

        // Makes the game take focus.
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
            InitializeOneShotEffectQueueing();
        }

        private void DeinitGame()
        {
            CcLog.Message("DEINIT");
            AbortAllInjection(true);
        }

        /// <summary>
        /// Checks if the game is in a state where effects should be applied.
        /// </summary>
        /// <param name="request"></param>
        /// <returns>True if the game is not paused or in a menu, and the base injected pointers are properly set. </returns>
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

        // Return true if the pointer points to a non-null, non-zero value.
        private bool VerifyIndirectPointerIsReady(AddressChain pointer)
        {
            return pointer != null
                && pointer.TryGetLong(out long value)
                && value != 0;
        }

        // Called when a request dooes not have the expected data.
        private void HandleInvalidRequest(EffectRequest request)
        {
            CcLog.Message($"Invalid request: {FinalCode(request)}");
        }

        // Returns true if the game is not closed, on a menu, or paused. Returns true on cutscenes.
        private bool IsInGameplay()
        {
            if (keyManager.InForcedPause)
            {
                CcLog.Message("Actually in gameplay, it is a forced pause.");
                return true;
            }
            return currentlyInGameplay;
        }

        protected override void StartEffect(EffectRequest request)
        {
            CcLog.Message("StartEffect started");
            CcLog.Message(FinalCode(request));
            var code = FinalCode(request).Split('_');

            switch (code[0])
            {
                case "thunderstorm": Thunderstorm((int)request.Duration.TotalMilliseconds, 10 * 33, 90 * 33, 5 * 33, 90 * 33); break;
                case "paranoia": Paranoia((int)request.Duration.TotalMilliseconds, 300); break;
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
                            case "quad": SetDamageFactors(request, null, 4, null, "granted you QUAD DAMAGE. RIP AND TEAR.", OneShotEffect.QuadDamage); break;
                            case "ludicrous": SetDamageFactors(request, null, 99999f, true, "granted you the might to crush your enemies in one blow."); break;
                            case "half": SetDamageFactors(request, null, 0.5f, null, "gave your enemies twice the health and shields. The rascal!"); break;
                            case "reversed": SetDamageFactors(request, null, -1f, null, "made all NPC get healed from any damage.", OneShotEffect.HealingBullets); break;
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
                            case "instadeath": SetDamageFactors(request, 99999f, 99999f, true, "made everyone fragile as glass. One hit kills anyone, including you. Keep your shields up!", OneShotEffect.HeavenOrHell); break;
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

                        QueueOneShotEffect(request, (OneShotEffect)slot);
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
                case "flappyspartan": FlappySpartan(request); break;
                case "forcefire": CeaselessDischarge(request); break;
                case "forcegrenades": ForceGrenades(request); break;
                case "preventattacking": Pacifist(request); break;
                case "reversemovement": ReverseMovementKeys(request); break;
                case "randomizecontrols": RandomizeControls(request); break;
                case "turretmode": TurretMode(request); break;
                case "forcecrouch": ForceCrouch(request); break;
                case "berserker": Berserker(request); break;
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
                                int dx = rng.Next(-15, 15);
                                int dy = rng.Next(-15, 15);

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
                                QueueOneShotEffect((short)OneShotEffect.SkipLevel, 0); // Slipspace jump.
                                return true;
                            },
                            true,
                            EffectMutex.LevelChangeOrRestart);

                        break;
                    }
                //#if DEVELOPMENT
                //                case "testdurationparam":
                //                    {
                //                        TryEffect(request, () => IsReady(request),
                //                        () =>
                //                        {
                //                            QueueOneShotEffect(2, (int)request.Duration.TotalMilliseconds);
                //                            return true;
                //                        },
                //                    false,
                //                    null);
                //                        break;
                //                    }
                //                case "testmultieffect":
                //                    {
                //                        QueueOneShotEffect(10, 0);
                //                        QueueOneShotEffect(11, 0);
                //                        QueueOneShotEffect(12, 0);
                //                        QueueOneShotEffect(13, 0);
                //                        //QueueOneShotEffect(5);
                //                        QueueOneShotEffect(6, 0);
                //                        QueueOneShotEffect(7, 0);

                //                        break;
                //                    }
                //                case "abortallinjection":
                //                    {
                //                        AbortAllInjection(true);
                //                        break;
                //                    }
                //#endif
                default:
                    HandleInvalidRequest(request);
                    CcLog.Message("Triggered nothing");
                    break;
            }
        }
    }
}