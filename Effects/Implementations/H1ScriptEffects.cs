using CrowdControl.Common;
using CrowdControl.Games.Packs.MCCHaloCE.Effects;
using System;
using System.Collections.Concurrent;
using System.Timers;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
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

        /// <summary>
        /// Queues an H1 one-shot effect to be run as sun as a frame without other effect being applied is ready, and instantly applies.
        /// </summary>
        /// <param name="code"> Index for the H1 script.</param>
        /// <param name="durationInMs"> Duration in milliseconds.</param>
        /// <param name="additionalStartingAction"> Action to be run before applying the effect. </param>
        /// <param name="message"> Message to send to crowd control. </param>
        /// <remarks>Use this when applying the effect from within another effect.</remarks> 
        public void QueueOneShotEffect(short code, int durationInMs, Action additionalStartingAction = null, string message = null)
        {
            oneShotEffectQueue.Enqueue(new OneShotH1EffectQueueing(code, durationInMs, additionalStartingAction, message));
        }

        /// <summary>
        /// Queues an H1 one-shot effect to be run as sun as a frame without other effect being applied is ready, and instantly applies.
        /// </summary>
        /// <param name="request">CC request for this effect.</param>
        /// <param name="slot">Index for the H1 script.</param>
        /// <remarks> Use this when the effect is not nested. </remarks>
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

        public void ApplyContinuousEffect(EffectRequest request, int slot)
        {
            OneShotEffect effect = (OneShotEffect)slot;
            string startMessage = effect switch
            {
                _ => $"started doing {effect}.",
            };

            string endMessage = effect switch
            {
                _ => $"stopped doing {effect}.",
            };

            string[]? mutex = effect switch
            {
                //0 => new string[] { EffectMutex.ArmorLock, EffectMutex.PlayerReceivedDamage },
                //1 => new string[] { EffectMutex.ArmorLock },
                //2 => new string[] { EffectMutex.ViewingControls },
                OneShotEffect.AiBreak => new string[] { EffectMutex.AIBehaviour },
                OneShotEffect.HighGravity => new string[] { EffectMutex.Gravity },
                OneShotEffect.LowGravity => new string[] { EffectMutex.Gravity },
                //9 => new string[] { EffectMutex.Size },
                //10 => new string[] { EffectMutex.Size },
                OneShotEffect.AwkwardMoment => new string[] { EffectMutex.ArmorLock, EffectMutex.AIBehaviour },
                OneShotEffect.TrulyInfiniteAmmo => new string[] { EffectMutex.Ammo, EffectMutex.SetGrenades },
                OneShotEffect.OneShotOneKill => new string[] { EffectMutex.NPCReceivedDamage },
                OneShotEffect.Deathless => new string[] { EffectMutex.PlayerReceivedDamage },
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
                //OneShotEffect.BodySnatcher => () => QueueOneShotEffect((short)OneShotEffect.BodySnatcherS, 0),
                //OneShotEffect.Jetpack => () => QueueOneShotEffect((short)OneShotEffect.JetpackS, 0),
                //OneShotEffect.SuperJump => () => QueueOneShotEffect((short)OneShotEffect.SuperJumpS, 0),
                //OneShotEffect.Medusa => () => QueueOneShotEffect((short)OneShotEffect.MedusaS, 0),
                //OneShotEffect.AwkwardMoment => () =>
                //{
                //    QueueOneShotEffect((short)OneShotEffect.Crickets, 0);
                //}
                //,
                OneShotEffect.TrulyInfiniteAmmo => () =>
                {
                    TrySetIndirectByteArray(new byte[] { 99, 99, 99, 99 }, basePlayerPointer_ch, FirstGrenadeTypeAmountOffset); // TODO: remember old amounts?
                }
                ,
                //15 => () => ApplyForce(0, 0, 0.1f),
                _ => () => { }
            };
            Action additionalEndAction = effect switch
            {
                OneShotEffect.TrulyInfiniteAmmo => () =>
                {
                    TrySetIndirectByteArray(new byte[] { 0x2, 0x2, 0x2, 0x2 }, basePlayerPointer_ch, FirstGrenadeTypeAmountOffset);
                }
                ,
                _ => () => { }
                ,
            };

            var act = StartTimed(request,
            () => IsReady(request),
                () => IsReady(request),
                TimeSpan.FromMilliseconds(500),
            () =>
            {                
                additionalStartAction();
                string message = $"{request.DisplayViewer} {startMessage}";
                QueueOneShotEffect((short)slot, (int)request.Duration.TotalMilliseconds, additionalStartAction, message);

                return true;
            },
                mutex);

            act.WhenCompleted.Then(_ =>
            {
                additionalEndAction();
                Connector.SendMessage(endMessage);
            });
        }
    }
}