using CrowdControl.Common;
using CrowdControl.Games.Packs.Effects;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        // While on air, multiplies the player current horizontal speed by a factor, making it get out of control quickly.
        public void ActivateUnstableAirtime(EffectRequest request)
        {
            StartTimed(request, () => IsReady(request),
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} aggressively suggest you stay grounded.");
                    return InjectUnstableAirtime();
                },
                EffectMutex.PlayerSpeed)
            .WhenCompleted.Then(_ =>
            {
                Connector.SendMessage($"You can jump safely again.");
                UndoInjection(UnstableAirtimeId);
            });
        }
    }
}