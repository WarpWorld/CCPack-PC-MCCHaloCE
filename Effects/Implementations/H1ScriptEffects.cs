using CrowdControl.Common;
using CrowdControl.Games.Packs.Effects;
using System;
using System.Collections.Concurrent;
using System.Timers;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE;

// TODO: Apply proper comments after the rework and embedding Inferno's stuff.
public partial class MCCHaloCE
{
    private class OneShotH1EffectQueueing
    {
        public short Code { get; }
        public int DurationInMs { get; }
        public DateTime QueuedAt { get; }
        public Action? AdditionalStartingAction { get; }
        public string? Message { get; }

        public OneShotH1EffectQueueing(short code, int durationInMs, Action additionalStartingAction = null, string message = null)
        {
            Code = code;
            DurationInMs = durationInMs;
            QueuedAt = DateTime.Now;
            AdditionalStartingAction = additionalStartingAction;
            Message = message;
        }
    }

    private static ConcurrentQueue<OneShotH1EffectQueueing> oneShotEffectQueue = new ConcurrentQueue<OneShotH1EffectQueueing>();
    private static System.Timers.Timer oneShotEffectSpacingTimer;

    public void InitializeOneShotEffectQueueing()
    {
        if (oneShotEffectSpacingTimer != null)
        {
            CcLog.Message("Disabling old oneshoteffectqueueing.");
            oneShotEffectSpacingTimer.Enabled = false;
            oneShotEffectSpacingTimer.Dispose();
            oneShotEffectSpacingTimer = null;
        }

        CcLog.Message("Initializing oneshoteffectqueueing.");
        oneShotEffectSpacingTimer = new System.Timers.Timer(33); // 30 frames per second, the iteration speed of continuous H1 scripts.
        oneShotEffectSpacingTimer.Elapsed += TryApplyQueuedEffect;
        oneShotEffectSpacingTimer.AutoReset = true;
        oneShotEffectSpacingTimer.Enabled = true;
    }

    private static void TryApplyQueuedEffect(Object source, ElapsedEventArgs e)
    {
        oneShotEffectSpacingTimer.Enabled = false;
        if (oneShotEffectQueue.TryPeek(out OneShotH1EffectQueueing effect))
        {
            try
            {
                if (effect.Message != null)
                {
                    instance.Connector.SendMessage(effect.Message);
                }
                if (effect.AdditionalStartingAction != null)
                {
                    effect.AdditionalStartingAction();
                }

                CcLog.Message($"[{DateTime.Now.ToString("hh:mm:ss.fff tt")}]Applying one-shot H1 effect with code {effect.Code}, " +
                              $"queued at {effect.QueuedAt.ToString("MM/dd/yyyy hh:mm:ss.fff tt")}, and duration {effect.DurationInMs}.");
                instance.SetScriptOneShotEffectH1Variable(effect.Code, effect.DurationInMs);
                if (!oneShotEffectQueue.TryDequeue(out _))
                {
                    CcLog.Message("Could not dequeue effect, this may cause an infinite loop");
                }
            }
            catch (Exception ex)
            {
                CcLog.Error(ex, "Error when applying queued H1 effect.");
            }
        }
        oneShotEffectSpacingTimer.Enabled = true;
    }

    public void QueueOneShotEffect(short code, int durationInMs, Action additionalStartingAction = null, string message = null)
    {
        oneShotEffectQueue.Enqueue(new OneShotH1EffectQueueing(code, durationInMs, additionalStartingAction, message));
    }

    /// <summary>
    /// Queues an H1 one-shot effect to be run as sun as a frame without other effect being applied is ready, and instantly applies
    /// </summary>
    /// <param name="request"></param>
    /// <param name="slot"></param>
    public void QueueOneShotEffect(EffectRequest request, OneShotEffect slot)
    {
        string message = slot switch
        {
            OneShotEffect.KillPlayer => "killed you in cold blood.",
            OneShotEffect.RestartLevel => "made you restart the level!",
            OneShotEffect.GiveAllVehicles => "dropped all vehicles on your head.",
            OneShotEffect.SkipLevel => "beat this level for you!",
            OneShotEffect.DisableCrosshair => "disabled your crosshair.",
            OneShotEffect.Malfunction => "disabled something on your HUD.",
            OneShotEffect.RepairHud => "repaired something in your HUD.",
            OneShotEffect.GiveSafeCheckpoint => "gave you a safe checkpoint.",
            OneShotEffect.GiveUnsafeCheckpoint => "gave you a completely unsafe checkpoint.",
            _ => "did a thing.",
        };

        string? mutex = slot switch
        {
            _ => null,
        };

        Action additionalStartAction = slot switch
        {
            OneShotEffect.AiFriendly => () => QueueOneShotEffect((short)OneShotEffect.FriendlyS, 0),
            OneShotEffect.AiFoe => () => QueueOneShotEffect((short)OneShotEffect.PublicEnemyS, 0),
            _ => () => { }
            ,
        };

        TryEffect(request, () => IsReady(request),
            () =>
            {
                message = $"{request.DisplayViewer} {message}";
                QueueOneShotEffect((short)slot, (int)request.Duration.TotalMilliseconds, additionalStartAction, message);

                return true;
            },
            true,
            mutex);
    }

    public bool TrySetIndirectTimedEffectFlag(ContinuousEffect effect, bool activeFlag)
    {
        return TrySetIndirectTimedEffectFlag((int)effect, activeFlag ? 1 : 0, 0);
    }

    public void ApplyContinuousEffect(EffectRequest request, int slot)
    {
        ContinuousEffect effect = (ContinuousEffect)slot;
        string startMessage = effect switch
        {
            //0 => "locked your armor.",
            //1 => "locked your armor, but \"forgot\" to include a shield.",
            //2 => "inverted your viewing controls.",
            //3 => "carefully delivered vehicles.",
            ContinuousEffect.AiBreak => "told the AI to chill for a second",
            ContinuousEffect.Jetpack => "gave you a jetpack.",
            ContinuousEffect.HighGravity => "increased gravity.",
            ContinuousEffect.LowGravity => "decreased gravity.",
            ContinuousEffect.SuperJump => "boosted your jumps. Remember to roll!",
            //9 => "made you big.",
            //10 => "made you tiny",
            ContinuousEffect.BodySnatcher => "granted you possession of whoever you touch.",
            ContinuousEffect.AwkwardMoment => "started an awkward moment",
            ContinuousEffect.Medusa => "turned you into a gorgon.",
            ContinuousEffect.TrulyInfiniteAmmo => "gave you truly infinite ammo.",
            //15 => "commenced your ascension.",
            //16 => "turned off the lights.",
            //17 => "made all NPCs OSHA compliant.",
            //18 => "made all NPCs harder to see.",
            ContinuousEffect.MovieBars => "brought out the popcorn.",
            //20 => "triggered something. Probably something bad.",
            ContinuousEffect.Blind => "disabled your HUD.",
            ContinuousEffect.NoCrosshair => "disabled your crosshair.",
            ContinuousEffect.Silence => "blew up your eardrums.",
            ContinuousEffect.OneShotOneKill => "granted you the power to smite your foes in one blow.",
            ContinuousEffect.Deathless => "says you will die when they say, not before",
            //30 => "Second var test.",
            _ => "started doing a thing.",
        };

        string endMessage = effect switch
        {
            //0 => "Armor lock ended.",
            //1 => "Armor lock ended.",
            //2 => "Viewing controls back to normal",
            //3 => "Delivery complete.",
            ContinuousEffect.AiBreak => "AI reactivated",
            ContinuousEffect.Jetpack => "Jetpack removed. Hope you were not far from the ground.",
            ContinuousEffect.HighGravity => "Gravity is back to normal.",
            ContinuousEffect.LowGravity => "Gravity is back to normal.",
            ContinuousEffect.SuperJump => "Jumps are back to normal",
            //9 => "You are a regular sized spartan once more.",
            //10 => "You are a regular sized spartan once more.",
            ContinuousEffect.BodySnatcher => "It is now safe to touch people again.",
            ContinuousEffect.AwkwardMoment => "Well, the moment has passed. Back to work.",
            ContinuousEffect.Medusa => "It is now safe to gaze into thy eyes again",
            ContinuousEffect.TrulyInfiniteAmmo => "Ammo is limited again",
            //15 => "Nevermind you're a total sinner.",
            //16 => "Let there be light once more.",
            //17 => "NPC visibility back to normal",
            //18 => "NPC visibility back to normal",
            ContinuousEffect.MovieBars => "Fin.",
            //20 => "You regain confidence.",
            ContinuousEffect.Blind => "HUD reactivated.",
            ContinuousEffect.NoCrosshair => "Crosshair reactivated.",
            ContinuousEffect.Silence => "Sound restored.",
            ContinuousEffect.OneShotOneKill => "Your damage is back to normal.",
            ContinuousEffect.Deathless => "You are mortal once more.",
            //30 => "Second var test over.",
            _ => "stopped doing a thing.",
        };

        string[]? mutex = effect switch
        {
            //0 => new string[] { EffectMutex.ArmorLock, EffectMutex.PlayerReceivedDamage },
            //1 => new string[] { EffectMutex.ArmorLock },
            //2 => new string[] { EffectMutex.ViewingControls },
            ContinuousEffect.AiBreak => new string[] { EffectMutex.AIBehaviour },
            ContinuousEffect.HighGravity => new string[] { EffectMutex.Gravity },
            ContinuousEffect.LowGravity => new string[] { EffectMutex.Gravity },
            //9 => new string[] { EffectMutex.Size },
            //10 => new string[] { EffectMutex.Size },
            ContinuousEffect.AwkwardMoment => new string[] { EffectMutex.ArmorLock, EffectMutex.AIBehaviour },
            ContinuousEffect.TrulyInfiniteAmmo => new string[] { EffectMutex.Ammo, EffectMutex.SetGrenades },
            ContinuousEffect.OneShotOneKill => new string[] { EffectMutex.NPCReceivedDamage },
            ContinuousEffect.Deathless => new string[] { EffectMutex.PlayerReceivedDamage },
            //15 => new string[] { EffectMutex.Gravity },
            //17 => new string[] { EffectMutex.ObjectLightScale },
            //18 => new string[] { EffectMutex.ObjectLightScale },
            _ => null,
        };

        Action additionalStartAction = effect switch
        {
            //0 => () =>
            //{
            //    PlayerReceivedDamageFactor = 0f;
            //    InjectConditionalDamageMultiplier();
            //}
            //,
            //3 => () =>
            //{
            //    PlayerReceivedDamageFactor = 0f;
            //    InjectConditionalDamageMultiplier();
            //}
            //,
            ContinuousEffect.BodySnatcher => () => QueueOneShotEffect((short)OneShotEffect.BodySnatcherS, 0),
            ContinuousEffect.Deathless => () => QueueOneShotEffect((short)OneShotEffect.DeathlessS, 0),
            ContinuousEffect.Jetpack => () => QueueOneShotEffect((short)OneShotEffect.JetpackS, 0),
            ContinuousEffect.SuperJump => () => QueueOneShotEffect((short)OneShotEffect.SuperJumpS, 0),
            ContinuousEffect.Medusa => () => QueueOneShotEffect((short)OneShotEffect.MedusaS, 0),
            ContinuousEffect.OneShotOneKill => () => QueueOneShotEffect((short)OneShotEffect.OneShotOneKillS, 0),
            ContinuousEffect.AwkwardMoment => () =>
            {
                QueueOneShotEffect((short)OneShotEffect.Crickets, 0);
            }
            ,
            ContinuousEffect.TrulyInfiniteAmmo => () =>
            {
                TrySetIndirectByteArray(new byte[] { 99, 99, 99, 99 }, basePlayerPointer_ch, FirstGrenadeTypeAmountOffset); // TODO: remember old amounts?
                QueueOneShotEffect((short)OneShotEffect.UnlimitedAmmoS, 0);
            }
            ,
            //15 => () => ApplyForce(0, 0, 0.1f),
            _ => () => { }
        };
        Action additionalEndAction = effect switch
        {
            //0 => () =>
            //{
            //    PlayerReceivedDamageFactor = 1f;
            //    InjectConditionalDamageMultiplier();
            //}
            //,
            //3 => () =>
            //{
            //    PlayerReceivedDamageFactor = 1f;
            //    InjectConditionalDamageMultiplier();
            //}
            //,
            ContinuousEffect.TrulyInfiniteAmmo => () =>
            {
                TrySetIndirectByteArray(new byte[] { 0x2, 0x2, 0x2, 0x2 }, basePlayerPointer_ch, FirstGrenadeTypeAmountOffset);
            }
            ,
            _ => () => { }
            ,
        };

        // Adapt the slot and offset to account for the script variable bit limits.
        // The variables are separated by 8 bytes in memory as declared in the script.
        int varOffset = (slot / MaxContinousScriptEffectSlotPerVar) * 8;
        int actualSlot = (slot % MaxContinousScriptEffectSlotPerVar);
        var act = StartTimed(request,
            () => IsReady(request),
            () => IsReady(request),
            TimeSpan.FromMilliseconds(500),
            () =>
            {
                Connector.SendMessage($"{request.DisplayViewer} {startMessage}");
                additionalStartAction();
                return TrySetIndirectTimedEffectFlag(actualSlot, 1, varOffset);
            },
            mutex);

        act.WhenCompleted.Then(_ =>
        {
            additionalEndAction();
            TrySetIndirectTimedEffectFlag(actualSlot, 0, varOffset);
            Connector.SendMessage(endMessage);
        });
    }
}