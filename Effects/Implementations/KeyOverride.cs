using ConnectorLib;
using CrowdControl.Common;
using CrowdControl.Games.Packs.Effects;
using System;
using System.Threading.Tasks;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        // Forces sideways movement.
        public void CrabRave(EffectRequest request)
        {
            KeyManipulationPerFrameEffect(request,
                 () =>
                 {
                     Connector.SendMessage($"{request.DisplayViewer} made you walk like a crab.");
                     keyManager.DisableAction(GameAction.RunForward);
                     keyManager.DisableAction(GameAction.RunBackwards);
                     keyManager.DisableAction(GameAction.StrafeRight);
                     keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);

                     BringGameToForeground();
                     keyManager.ForceShortPause();
                     return true;
                 },
                 (task) =>
                 {
                     keyManager.RestoreAllKeyBinds();
                     keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                     BringGameToForeground();
                     keyManager.SendAction(GameAction.StrafeLeft, true);
                     keyManager.ForceShortPause();
                     Connector.SendMessage($"Crabness expunged.");
                 },
                 () =>
                 {
                     BringGameToForeground();
                     return keyManager.SendAction(GameAction.StrafeLeft, false);
                 });
        }

        // Forces backwards movement.
        public void Moonwalk(EffectRequest request)
        {
            KeyManipulationPerFrameEffect(request,
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} made your pronouns \"he/hee\".");
                    keyManager.DisableAction(GameAction.RunForward);
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);

                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    return true;
                },
                (task) =>
                {
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.SendAction(GameAction.RunBackwards, true);
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"Forward movement is now allowed.");
                },
                () =>
                {
                    BringGameToForeground();
                    return keyManager.SendAction(GameAction.RunBackwards, false);
                });
        }

        // Forces jumping.
        public void BunnyHop(EffectRequest request)
        {
            KeyManipulationPerFrameEffect(request,
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} put some literal spring on your step.");
                    return true;
                },
                (task) =>
                {
                    keyManager.SendAction(GameAction.Jump, true);
                    Connector.SendMessage($"Floor is no longer lava.");
                },
                () =>
                {
                    BringGameToForeground();
                    return keyManager.SendAction(GameAction.Jump, false);
                });
        }

        // Enables double jump and makes you flap.
        public void FlappySpartan(EffectRequest request)
        {
            bool keyUp = true; // Used to alternate events on each frame.
            RepeatAction(request,
                startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
                startAction: () =>
                {
                    QueueOneShotEffect((short)OneShotEffect.UnlockCursedJump, 0);
                    QueueOneShotEffect((short)OneShotEffect.FlappySpartan_Start, 0);
                    QueueOneShotEffect((short)OneShotEffect.NoCursedPowerupsCooldown, (int)request.Duration.TotalMilliseconds);

                    Connector.SendMessage($"{request.DisplayViewer} is playing Flappy Spartan.");
                    return true;
                },
                startRetry: TimeSpan.FromSeconds(1),
                refreshCondition: () => IsInGameplay(),
                refreshRetry: TimeSpan.FromMilliseconds(500),
                refreshAction: () =>
                {
                    BringGameToForeground();
                    keyUp = !keyUp;

                    return keyManager.SendAction(GameAction.Jump, keyUp);
                },
                refreshInterval: TimeSpan.FromMilliseconds(150),
                extendOnFail: false,
                mutex: new string[] { EffectMutex.KeyPress, EffectMutex.KeyDisable, EffectMutex.Ammo }).WhenCompleted.Then((task) =>
                {
                    Connector.SendMessage($"You hit a pipe.");
                    keyManager.SendAction(GameAction.Jump, true);
                    QueueOneShotEffect((short)OneShotEffect.FlappySpartan_End, 0);
                });
        }

        // Forces constant crouching.
        public void ForceCrouch(EffectRequest request)
        {
            KeyManipulationPerFrameEffect(request,
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} broke your ankles.");
                    if (!keyManager.SwapActionWithArbitraryKeyCode(GameAction.Crouch, HIDConnector.VirtualKeyCode.NUMPAD1))
                    {
                        Connector.SendMessage("Could not swap crouch to an unused key.");
                    }

                    keyManager.DisableAction(GameAction.Jump);
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    return true;
                },
                (task) =>
                {
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.SendAction(GameAction.Crouch, true);
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"You can move normally again.");
                },
                () =>
                {
                    BringGameToForeground();
                    return keyManager.SendAction(GameAction.Crouch, false);
                });
        }

        // Forces constant grenadet throws.
        public void ForceGrenades(EffectRequest request)
        {
            bool keyUp = true;
            RepeatAction(request,
                startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
                startAction: () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} told you to get rid of your grenades.");
                    return true;
                },
                startRetry: TimeSpan.FromSeconds(1),
                refreshCondition: () => IsInGameplay(),
                refreshRetry: TimeSpan.FromMilliseconds(500),
                refreshAction: () =>
                {
                    BringGameToForeground();
                    keyUp = !keyUp;
                    return keyManager.SendAction(GameAction.ThrowGrenade, keyUp);
                },
                refreshInterval: TimeSpan.FromMilliseconds(33),
                extendOnFail: false,
                mutex: new string[] { EffectMutex.KeyPress, EffectMutex.KeyDisable }).WhenCompleted.Then((task) =>
                {
                    keyManager.SendAction(GameAction.ThrowGrenade, true);
                    Connector.SendMessage($"Enough grenading, soldier!.");
                });
        }

        // Forces firing. (Will likely be replaced by fullerauto and fullestauto)
        public void CeaselessDischarge(EffectRequest request)
        {
            // TODO: New implementation using the weapon pointers pending.

            // This implementation is a bit underwhelming with the battery rifle, but hilarious with the plasma pistol.
            // Note: can't send mousedown events with hidConnector. But I can rebind the fire button temporarily.
            bool keyUp = true; // Used to alternate events on each frame.
            RepeatAction(request,
                startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
                startAction: () =>
                {
                    if (!keyManager.SwapActionWithArbitraryKeyCode(GameAction.Fire, HIDConnector.VirtualKeyCode.NUMPAD0))
                    {
                        Connector.SendMessage("Could not swap fire to an unused key.");
                    }

                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"{request.DisplayViewer} ordered to fire at will.");
                    return true;
                },
                startRetry: TimeSpan.FromSeconds(1),
                refreshCondition: () => IsInGameplay(),
                refreshRetry: TimeSpan.FromMilliseconds(500),
                refreshAction: () =>
                {
                    BringGameToForeground();
                    keyUp = !keyUp;
                    return keyManager.SendAction(GameAction.Fire, keyUp);
                },
                refreshInterval: TimeSpan.FromMilliseconds(33),
                extendOnFail: false,
                mutex: new string[] { EffectMutex.KeyPress, EffectMutex.KeyDisable }).WhenCompleted.Then((task) =>
                {
                    BringGameToForeground();
                    keyManager.SendAction(GameAction.Fire, true);
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"Trigger discipline is now available once more.");
                });
        }

        // Prevents firing, melee and grenades.
        public void Pacifist(EffectRequest request)
        {
            KeyManipulationEffect(request,
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} tells you to take it easy, man.");
                    keyManager.DisableAction(GameAction.Melee);
                    keyManager.DisableAction(GameAction.Fire);
                    keyManager.DisableAction(GameAction.ThrowGrenade);
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    return true;
                },
                (task) =>
                {
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"Violence is allowed again.");
                });
        }

        // Prevents usage of movement keys.
        public void TurretMode(EffectRequest request)
        {
            KeyManipulationEffect(request,
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} ordered you to stay put.");
                    TrySetIndirectTimedEffectFlag(ContinuousEffect.TrulyInfiniteAmmo, true);
                    keyManager.DisableAction(GameAction.RunBackwards);
                    keyManager.DisableAction(GameAction.RunForward);
                    keyManager.DisableAction(GameAction.StrafeLeft);
                    keyManager.DisableAction(GameAction.StrafeRight);
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    return true;
                },
                (task) =>
                {
                    TrySetIndirectTimedEffectFlag(ContinuousEffect.TrulyInfiniteAmmo, false);
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"You can move again.");
                });
        }

        // Swaps A with D, and W with S
        public void ReverseMovementKeys(EffectRequest request)
        {
            KeyManipulationEffect(request,
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} confused your legs.");
                    keyManager.ReverseMovementKeys(halo1BaseAddress);
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    return true;
                },
                (task) =>
                {
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"Legs are fine again.");
                },
                new string[] { EffectMutex.KeyChange });
        }

        // Randomize the current key bindings among each other, excluding WASD.
        public void RandomizeControls(EffectRequest request)
        {
            KeyManipulationEffect(request,
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} randomized your controls.");
                    keyManager.RandomizeNonRunningKeys(halo1BaseAddress);
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    return true;
                },
                (task) =>
                {
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"Controls are sane again.");
                },
                new string[] { EffectMutex.KeyChange });
        }

        // Template for effects that press, change or disable keys that need to do something every frame.
        private void KeyManipulationPerFrameEffect(EffectRequest request, Func<bool> startAction, Action<Task> endAction, Func<bool> perFrameAction)
        {
            RepeatAction(request,
                startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
                startAction: startAction,
                startRetry: TimeSpan.FromSeconds(1),
                refreshCondition: () => IsInGameplay(),
                refreshRetry: TimeSpan.FromMilliseconds(500),
                refreshAction: perFrameAction,
                refreshInterval: TimeSpan.FromMilliseconds(33),
                extendOnFail: false,
                mutex: new string[] { EffectMutex.KeyDisable, EffectMutex.KeyPress }).WhenCompleted.Then(endAction);
        }

        // Template for effects that press, change or disable keys that don't do anything else in between start and end.
        private void KeyManipulationEffect(EffectRequest request, Func<bool> startAction, Action<Task> endAction, string[] specialCaseMutex = null)
        {
            string[] mutex = specialCaseMutex != null ? specialCaseMutex : new string[] { EffectMutex.KeyPress, EffectMutex.KeyDisable };
            RepeatAction(request,
                startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
                startAction: startAction,
                startRetry: TimeSpan.FromSeconds(1),
                refreshCondition: () => true,
                refreshRetry: TimeSpan.FromMilliseconds(500),
                refreshAction: () => true,
                refreshInterval: TimeSpan.FromMilliseconds(1000),
                extendOnFail: false,
                mutex: mutex).WhenCompleted.Then(endAction);
        }

        // Forces fire and sets time between shots to 0.
        // If unlimitedAmmo is true, it give infinite ammo, clip, battery and heat.
        // Else, it prevents shotgun pumping and sets charge to max so the battery rifle heats up as usual.
        public void FullAuto(EffectRequest request, bool unlimitedAmmo)
        {
            bool keyUp = true; // Used to alternate events on each frame.
            RepeatAction(request,
                startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
                startAction: () =>
                {
                    InjectFullerAuto();
                    if (unlimitedAmmo)
                    {
                        TrySetIndirectTimedEffectFlag(ContinuousEffect.TrulyInfiniteAmmo, true);
                        QueueOneShotEffect((short)OneShotEffect.Fullestauto, 0);
                    }

                    if (!keyManager.SwapActionWithArbitraryKeyCode(GameAction.Fire, HIDConnector.VirtualKeyCode.NUMPAD0))
                    {
                        Connector.SendMessage("Could not swap fire to an unused key.");
                    }

                    if (!keyManager.SwapActionWithArbitraryKeyCode(GameAction.SwapWeapons, HIDConnector.VirtualKeyCode.NUMPAD1))
                    {
                        Connector.SendMessage("Could not swap 'swap weapons' to an unused key.");
                    }

                    if (!keyManager.SwapActionWithArbitraryKeyCode(GameAction.Reload, HIDConnector.VirtualKeyCode.NUMPAD2))
                    {
                        Connector.SendMessage("Could not swap 'reload' to an unused key.");
                    }

                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    BringGameToForeground();
                    keyManager.ForceShortPause();
                    Connector.SendMessage($"{request.DisplayViewer} ordered to fire at will. THEIR will.");
                    return true;
                },
                startRetry: TimeSpan.FromSeconds(1),
                refreshCondition: () => IsInGameplay(),
                refreshRetry: TimeSpan.FromMilliseconds(500),
                refreshAction: () =>
                {
                    BringGameToForeground();
                    keyUp = !keyUp;

                    return keyManager.SendAction(GameAction.Fire, keyUp);
                },
                refreshInterval: TimeSpan.FromMilliseconds(33),
                extendOnFail: false,
                mutex: new string[] { EffectMutex.KeyPress, EffectMutex.KeyDisable, EffectMutex.Ammo }).WhenCompleted.Then((task) =>
                {
                    if (unlimitedAmmo)
                    {
                        QueueOneShotEffect((short)OneShotEffect.Fullestauto_Stop, 0);
                    }
                    BringGameToForeground();
                    keyManager.SendAction(GameAction.Fire, true);
                    keyManager.RestoreAllKeyBinds();
                    keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                    keyManager.ForceShortPause();
                    UndoInjection(FullerAutoId);
                    TrySetIndirectTimedEffectFlag(ContinuousEffect.TrulyInfiniteAmmo, false);
                    Connector.SendMessage($"Trigger discipline is now available once more.");
                });
        }
    }
}