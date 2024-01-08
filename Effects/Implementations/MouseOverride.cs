using System;
using CrowdControl.Common;

namespace CrowdControl.Games.Packs.MCCHaloCE.Effects.Implementations;

public partial class MCCHaloCE
{
    // Forces the mouse to move in a random direction every frame, up to maxRange distance. Every recoveryFrameInterval frames, the mouse is reset to its original position.
    public void ForceMouseShake(EffectRequest request, int maxRange, float controlFactor, int recoveryFrameInterval)
    {
        Random rng = new Random();
        int dxToRecover = 0;
        int dyToRecover = 0;
        bool recoverFrame = false;
        int frameCounter = 0;
        TaskEx.Then(RepeatAction(request,
            startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
            startAction: () =>
            {
                Connector.SendMessage($"{request.DisplayViewer} gave you 299 cups of coffee.");
                return true;
            },
            startRetry: TimeSpan.FromSeconds(1),
            refreshCondition: () => IsInGameplay(),
            refreshRetry: TimeSpan.FromMilliseconds(500),
            refreshAction: () =>
            {
                recoverFrame = frameCounter > 0 && frameCounter % recoveryFrameInterval == 0;
                BringGameToForeground();
                if (recoverFrame)
                {
                    frameCounter = 0;
                    bool success = keyManager.ForceMouseMove((int)(-dxToRecover * controlFactor), (int)(-dyToRecover * controlFactor));
                    dxToRecover = 0;
                    dyToRecover = 0;
                }

                frameCounter++;
                int dx = rng.Next(-maxRange, maxRange);
                int dy = rng.Next(-maxRange, maxRange);
                dxToRecover += dx;
                dyToRecover += dy;
                return keyManager.ForceMouseMove(dx, dy);
            },
            refreshInterval: TimeSpan.FromMilliseconds(33),
            extendOnFail: false,
            mutex: EffectMutex.MouseForcedMove).WhenCompleted, (task) =>
        {
            Connector.SendMessage("Your hands are steady again.");
        });
    }

    // Applies mouse movement every frame.
    public void ApplyMovementEveryFrame(EffectRequest request, int dx, int dy, string startMessage, string endMessage)
    {
        TaskEx.Then(RepeatAction(request,
            startCondition: () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
            startAction: () =>
            {
                Connector.SendMessage($"{request.DisplayViewer} {startMessage}");
                return true;
            },
            startRetry: TimeSpan.FromSeconds(1),
            refreshCondition: () => IsInGameplay(),
            refreshRetry: TimeSpan.FromMilliseconds(500),
            refreshAction: () =>
            {
                BringGameToForeground();
                return keyManager.ForceMouseMove(dx, dy);
            },
            refreshInterval: TimeSpan.FromMilliseconds(33),
            extendOnFail: false,
            mutex: EffectMutex.MouseForcedMove).WhenCompleted, (task) =>
        {
            Connector.SendMessage(endMessage);
        });
    }
}