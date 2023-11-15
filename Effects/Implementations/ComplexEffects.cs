using CrowdControl.Common;
using CrowdControl.Games.Packs.Effects;
using System;
using System.Diagnostics;
using System.Threading;

namespace CrowdControl.Games.Packs.MCCHaloCE;

public partial class MCCHaloCE
{
    private void Thunderstorm(int totalDurationInMilliseconds, int fadeInDurationInMs, int fadeOutDurationInMs, int delayAfterFadeIn, int delayAfterFadeOut)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < totalDurationInMilliseconds)
        {
            QueueOneShotEffect((short)OneShotEffect.StormyNight_FadeOut, fadeOutDurationInMs);
            Thread.Sleep(delayAfterFadeOut + fadeOutDurationInMs);
            QueueOneShotEffect((short)OneShotEffect.StormyNight_FadeIn, fadeInDurationInMs);
            Thread.Sleep(delayAfterFadeIn + fadeInDurationInMs);
        }
        stopwatch.Stop();
    }

    private void Paranoia(int totalDurationInMs, int delayBetweenFlashlightToggleInMs)
    {
        int timeElapsed = 0;
        QueueOneShotEffect((short)OneShotEffect.Paranoia_Start, 0);
        while (timeElapsed < totalDurationInMs)
        {
            QueueOneShotEffect((short)OneShotEffect.Flashlight_On, 0);
            Thread.Sleep(delayBetweenFlashlightToggleInMs);
            QueueOneShotEffect((short)OneShotEffect.Flashlight_Off, 0);
            Thread.Sleep(delayBetweenFlashlightToggleInMs);
            timeElapsed += 2 * delayBetweenFlashlightToggleInMs;
        }

        QueueOneShotEffect((short)OneShotEffect.Paranoia_End, 0);
    }

    private void Berserker(EffectRequest request)
    {
        var act = StartTimed(request,
            startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
            continueCondition: () => IsReady(request),
            continueConditionInterval: TimeSpan.FromMilliseconds(3000),
            action: () =>
            {
                //QueueOneShotEffect((short)OneShotEffect.Berserker_start, 0);
                // Keybinds
                keyManager.SetAlernativeBindingToOTherActions(GameAction.Melee, GameAction.Fire);
                keyManager.DisableAction(GameAction.Fire);
                keyManager.DisableAction(GameAction.ThrowGrenade);
                keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
                BringGameToForeground();
                keyManager.ForceShortPause();

                Connector.SendMessage($"{request.DisplayViewer} told you to RIP AND TEAR.");
                // Deathless effect
                TrySetIndirectTimedEffectFlag(ContinuousEffect.Deathless, true);
                // Omnipotent effect
                TrySetIndirectTimedEffectFlag(ContinuousEffect.OneShotOneKill, true);

                // Movement speed
                SetPlayerMovementSpeedWithoutEffect(1.5f);

                QueueOneShotEffect((short)OneShotEffect.Berserk, (int)request.Duration.TotalMilliseconds);

                return true;
            },
            mutex: new string[] { EffectMutex.PlayerSpeed, EffectMutex.PlayerReceivedDamage, EffectMutex.Ammo, EffectMutex.KeyDisable, EffectMutex.KeyPress });
        act.WhenCompleted.Then(_ =>
        {
            QueueOneShotEffect((short)OneShotEffect.Berserker_stop, 0);

            // Keybinds
            keyManager.RestoreAllKeyBinds();
            keyManager.UpdateGameMemoryKeyState(halo1BaseAddress);
            keyManager.ResetAlternativeBindingForAction(GameAction.Melee, halo1BaseAddress);
            BringGameToForeground();
            keyManager.ForceShortPause();

            Connector.SendMessage($"You can calm down now.");
            // Repair health and shields.
            SetHealth(1, true);
            SetShield(1);

            // Deathless remove
            TrySetIndirectTimedEffectFlag(ContinuousEffect.Deathless, false);
            // Omnipotent effect
            TrySetIndirectTimedEffectFlag(ContinuousEffect.OneShotOneKill, false);

            // Reset speed.
            SetPlayerMovementSpeedWithoutEffect(1);
        });
    }
}