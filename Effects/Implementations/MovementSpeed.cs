using CrowdControl.Common;
using CrowdControl.Games.Packs.Effects;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        private float PlayerSpeedFactor = 1;
        private float OthersSpeedFactor = 1;

        private bool ShouldInjectSpeed
        { get { return PlayerSpeedFactor != 1 || OthersSpeedFactor != 1; } }

        public void SetPlayerMovementSpeedWithoutEffect(float speedFactor)
        {
            PlayerSpeedFactor = speedFactor;
            InjectSpeedMultiplier();
        }

        // Sets a factor that multiplies all player movement speed. New speed is previous speed * (1 + speedFactor)
        public void SetPlayerMovementSpeed(EffectRequest request, float speedFactor, string message)
        {
            StartTimed(request, () => IsReady(request),
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} {message}");
                    PlayerSpeedFactor = speedFactor;
                    return InjectSpeedMultiplier();
                },
                EffectMutex.PlayerSpeed)
            .WhenCompleted.Then(_ =>
            {
                Connector.SendMessage($"Player speed back to normal.");

                PlayerSpeedFactor = 1;
                if (OthersSpeedFactor != 1)
                {
                    InjectSpeedMultiplier();
                }
                else
                {
                    UndoInjection(SpeedFactorId);
                }
            });
        }

        // Sets a factor that multiplies all NPC movement speed. New speed is previous speed * (1 + speedFactor)
        public void SetNPCMovementSpeed(EffectRequest request, float speedFactor, string message)
        {
            StartTimed(request, () => IsReady(request),
                () =>
                {
                    Connector.SendMessage($"{request.DisplayViewer} {message}");
                    OthersSpeedFactor = speedFactor;
                    return InjectSpeedMultiplier();
                },
                EffectMutex.NPCSpeed)
            .WhenCompleted.Then(_ =>
            {
                OthersSpeedFactor = 1;
                if (PlayerSpeedFactor != 1)
                {
                    InjectSpeedMultiplier();
                }
                else
                {
                    UndoInjection(SpeedFactorId);
                }
                Connector.SendMessage($"NPC speed back to normal.");
            });
        }
    }
}