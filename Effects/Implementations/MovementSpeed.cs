using CrowdControl.Common;

namespace CrowdControl.Games.Packs.MCCHaloCE.Effects.Implementations;

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
        TaskEx.Then(StartTimed(request, () => IsReady(request),
                    () =>
                    {
                        Connector.SendMessage($"{request.DisplayViewer} {message}");
                        PlayerSpeedFactor = speedFactor;
                        return InjectSpeedMultiplier();
                    },
                    EffectMutex.PlayerSpeed)
                .WhenCompleted, _ =>
            {
                Connector.SendMessage($"Player speed back to normal.");

                PlayerSpeedFactor = 1;
                if (OthersSpeedFactor != 1)
                {
                    InjectSpeedMultiplier();
                }
                else
                {
                    UndoInjection(Injections.MCCHaloCE.SpeedFactorId);
                }
            });
    }

    // Sets a factor that multiplies all NPC movement speed. New speed is previous speed * (1 + speedFactor)
    public void SetNPCMovementSpeed(EffectRequest request, float speedFactor, string message)
    {
        TaskEx.Then(StartTimed(request, () => IsReady(request),
                    () =>
                    {
                        Connector.SendMessage($"{request.DisplayViewer} {message}");
                        OthersSpeedFactor = speedFactor;
                        return InjectSpeedMultiplier();
                    },
                    EffectMutex.NPCSpeed)
                .WhenCompleted, _ =>
            {
                OthersSpeedFactor = 1;
                if (PlayerSpeedFactor != 1)
                {
                    InjectSpeedMultiplier();
                }
                else
                {
                    UndoInjection(Injections.MCCHaloCE.SpeedFactorId);
                }
                Connector.SendMessage($"NPC speed back to normal.");
            });
    }
}