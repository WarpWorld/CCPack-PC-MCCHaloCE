using CrowdControl.Common;

namespace CrowdControl.Games.Packs.MCCHaloCE.Effects.Implementations;

public partial class MCCHaloCE
{
    // While on air, multiplies the player current horizontal speed by a factor, making it get out of control quickly.
    public void ActivateUnstableAirtime(EffectRequest request)
    {
        TaskEx.Then(StartTimed(request, () => IsReady(request),
                    () =>
                    {
                        Connector.SendMessage($"{request.DisplayViewer} aggressively suggest you stay grounded.");
                        return InjectUnstableAirtime();
                    },
                    EffectMutex.PlayerSpeed)
                .WhenCompleted, _ =>
            {
                Connector.SendMessage($"You can jump safely again.");
                UndoInjection(Injections.MCCHaloCE.UnstableAirtimeId);
            });
    }
}