using CrowdControl.Common;
using CrowdControl.Games.Packs.MCCHaloCE.Effects;
using System;

namespace CrowdControl.Games.Packs.MCCHaloCE;

public partial class MCCHaloCE
{
    // Applies a random force to the player. Negative vertical (Z) forces are avoided by default to avoid smashing the player against the ground to death.
    private void ApplyRandomForce(float maxX, float maxY, float maxZ, bool allowNegativeZ = false)
    {
        Random rng = new Random();
        float x = GenerateRandomFloat(rng, maxX, true);
        float y = GenerateRandomFloat(rng, maxY, true);
        float z = GenerateRandomFloat(rng, maxZ, allowNegativeZ);

        ApplyForce(x, y, z);
    }

    // Applies a random force to the player. If relative, it is added to the current applied forces instead of replaced.
    public void ApplyForce(float x, float y, float z, bool relative = true)
    {
        TrySetIndirectFloat(x, basePlayerPointer_ch, XSpeedOffset, relative);
        TrySetIndirectFloat(y, basePlayerPointer_ch, XSpeedOffset + 4, relative);
        TrySetIndirectFloat(z, basePlayerPointer_ch, XSpeedOffset + 8, relative);
    }

    // Applies a random force to the player of up to forceStrength every specified interval.
    public void ShakePlayer(EffectRequest request, float forceStrength, int intervalInMs, string startMessage, string endMessage)
    {
        bool shake = true; // if true, apply force. If false, remove forces.
        RepeatAction(request, () => IsReady(request),
            () => Connector.SendMessage($"{request.DisplayViewer} {startMessage}."),
            TimeSpan.FromSeconds(1),
            IsInGameplay,
            TimeSpan.FromMilliseconds(500),
            () =>
            {
                if (shake)
                {
                    ApplyRandomForce(forceStrength, forceStrength, 0, true);
                }
                else
                {
                    ApplyForce(0, 0, 0, false);
                }

                shake = !shake;

                return true;
            },
            TimeSpan.FromMilliseconds(intervalInMs), // aprox. once per frame
            false,
            EffectMutex.ArmorLock).WhenCompleted.Then(_ =>
        {
            ApplyForce(0, 0, 0, false);
            Connector.SendMessage(endMessage);
        });
    }

    // Generates a random float between 0-max, or -max,max if allowing negatives
    private float GenerateRandomFloat(Random rng, float max, bool allowNegative)
    {
        float random = (float)rng.NextDouble();

        return allowNegative
            ? random * max * 2 - max
            : random * max;
    }
}