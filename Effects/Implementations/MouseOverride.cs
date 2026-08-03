using CrowdControl.Common;
using CrowdControl.Games.Packs.MCCHaloCE.Effects;

namespace CrowdControl.Games.Packs.MCCHaloCE;

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
        RepeatAction(request,
            () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
            () =>
            {
                Connector.SendMessage($"{request.DisplayViewer} gave you 299 cups of coffee.");
                return true;
            },
            TimeSpan.FromSeconds(1),
            () => IsInGameplay(),
            TimeSpan.FromMilliseconds(500),
            () =>
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
            TimeSpan.FromMilliseconds(33),
            false,
            EffectMutex.MouseForcedMove).WhenCompleted.Then((task) =>
        {
            Connector.SendMessage("Your hands are steady again.");
        });
    }

    // Applies mouse movement every frame.
    public void ApplyMovementEveryFrame(EffectRequest request, int dx, int dy, string startMessage, string endMessage)
    {
        RepeatAction(request,
            () => IsReady(request) && keyManager.EnsureKeybindsInitialized(halo1BaseAddress),
            () =>
            {
                Connector.SendMessage($"{request.DisplayViewer} {startMessage}");
                return true;
            },
            TimeSpan.FromSeconds(1),
            () => IsInGameplay(),
            TimeSpan.FromMilliseconds(500),
            () =>
            {
                BringGameToForeground();
                return keyManager.ForceMouseMove(dx, dy);
            },
            TimeSpan.FromMilliseconds(33),
            false,
            EffectMutex.MouseForcedMove).WhenCompleted.Then((task) =>
        {
            Connector.SendMessage(endMessage);
        });
    }
}