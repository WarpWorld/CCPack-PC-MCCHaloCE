using CrowdControl.Common;
using CrowdControl.Games.Packs.Effects;
using System;

namespace CrowdControl.Games.Packs.MCCHaloCE;

public enum ShieldRegenEffectType
{
    No,
    Instant
}

public partial class MCCHaloCE
{
    private const int HealthOffset = 0x9C;
    private const int ShieldsOffset = 0xA0;
    private const int ShieldRegenOffset = 0xC0;

    // Adds the given amount to the player shield. 1.0 is a full normal shield charge. Negative values remove shields.
    public void AddShield(EffectRequest request, float amount, string messageVerb)
    {
        TryEffect(request, () => IsReady(request),
            () => TrySetIndirectFloat(amount, basePlayerPointer_ch, ShieldsOffset, true),
            () => Connector.SendMessage($"{request.DisplayViewer} {messageVerb} your shield"),
            true,
            EffectMutex.SetShield);
    }

    public void SetShield(EffectRequest request, float value)
    {
        TryEffect(request, () => IsReady(request),
            () => SetShield(value),
            () => Connector.SendMessage($"{request.DisplayViewer} set your shield to {value}."),
            true,
            EffectMutex.SetShield);
    }

    public bool SetShield(float value)
    {
        return TrySetIndirectFloat(value, basePlayerPointer_ch, ShieldsOffset, false);
    }

    // Sets the shield to a given value. 1.0 is a full normal shield charge.
    private void SetShieldRegen(EffectRequest request, ShieldRegenEffectType type)
    {
        short regenValue = type switch
        {
            ShieldRegenEffectType.No => Int16.MaxValue,
            ShieldRegenEffectType.Instant => 0
        };

        string message = type switch
        {
            ShieldRegenEffectType.No => "prevented your shield from recharging.",
            ShieldRegenEffectType.Instant => "gave you a fast regenerating shield."
        };

        RepeatAction(request, () => IsReady(request),
            () => Connector.SendMessage($"{request.DisplayViewer} {message}"),
            TimeSpan.FromSeconds(1),
            () => IsReady(request),
            TimeSpan.FromMilliseconds(500),
            () => TrySetIndirectShort(regenValue, basePlayerPointer_ch, ShieldRegenOffset, false),
            TimeSpan.FromMilliseconds(500),
            false,
            EffectMutex.SetShield).WhenCompleted.Then(_ =>
        {
            TrySetIndirectShort(0, basePlayerPointer_ch, ShieldRegenOffset, false);
            Connector.SendMessage("Shields are back to normal.");
        });
    }

    // Increases (or decreases if delta is negative) health by the given value, without killing him.
    public void SetRelativeHealth(EffectRequest request, float deltaHealth, string message)
    {
        TryEffect(request, () => IsReady(request),
            () =>
            {
                if (!TryGetIndirectByteArray(basePlayerPointer_ch, HealthOffset, 4, out byte[] data))
                {
                    return false;
                }

                float currentHealth = BitConverter.ToSingle(data);

                currentHealth += deltaHealth;
                if (currentHealth < (1f / 8f))
                {
                    currentHealth = (1f / 8f);
                }

                return SetHealth(currentHealth, true);
            },
            () => Connector.SendMessage($"{request.DisplayViewer} {message}"),
            true,
            EffectMutex.SetHealth);
    }

    // Sets health to the given value. 1.0 is full health. 0 health does not kill the player until he receives damage.
    public void SetHealth(EffectRequest request, float value, string message)
    {
        TryEffect(request, () => IsReady(request),
            () =>
            {
                return SetHealth(value, true);
            },
            () => Connector.SendMessage($"{request.DisplayViewer} {message}"),
            true,
            EffectMutex.SetHealth);
    }

    public bool SetHealth(float value, bool soundEffectOnFullHealth)
    {
        if (value == 1 && soundEffectOnFullHealth)
        {
            QueueOneShotEffect((short)OneShotEffect.Heal, 0);
        }

        return TrySetIndirectFloat(value, basePlayerPointer_ch, HealthOffset, false);
    }

    // Increases health every interval.
    public void GiveHealthRegen(EffectRequest request, float valuePerTick, int tickIntervalInMs)
    {
        RepeatAction(request,
                () => IsReady(request),
                () =>
                {
                    if (valuePerTick > 0)
                    {
                        QueueOneShotEffect((short)OneShotEffect.Heal, 0);
                    }
                    else
                    {
                        QueueOneShotEffect((short)OneShotEffect.OneHp, 0);
                    }

                    Connector.SendMessage($"{request.DisplayViewer} gave you health regeneration.");
                    return true;
                },
                TimeSpan.FromSeconds(1),
                () => IsReady(request),
                TimeSpan.FromMilliseconds(500),
                () =>
                {
                    if (!TryGetIndirectByteArray(basePlayerPointer_ch, HealthOffset, 4, out byte[] data))
                    {
                        return false;
                    }

                    float currentHealth = BitConverter.ToSingle(data);

                    currentHealth += valuePerTick;

                    if (currentHealth > 1)
                    {
                        currentHealth = 1;
                    }
                    if (currentHealth < 0)
                    {
                        currentHealth = 0.01f;
                    }

                    return SetHealth(currentHealth, false);
                },
                TimeSpan.FromMilliseconds(tickIntervalInMs),
                false,
                Guid.NewGuid().ToString())// Using a random mutext because null caused function signature ambiguity.
            .WhenCompleted.Then(_ =>
            {
                Connector.SendMessage("Health regeneration ended.");
            });
    }

    // Sets health to minimum, and restores it to its previous value after the duration.
    public void OneHealthAndADream(EffectRequest request)
    {
        float previousHealth = 1;
        StartTimed(request,
            startCondition: () => IsReady(request),
            continueCondition: () => IsReady(request),
            continueConditionInterval: TimeSpan.FromMilliseconds(500),
            () =>
            {
                QueueOneShotEffect((short)OneShotEffect.OneHp, 0);
                Connector.SendMessage($"{request.DisplayViewer} left you with one health and a dream.");
                TryGetIndirectByteArray(basePlayerPointer_ch, HealthOffset, 4, out byte[] healthBytes);
                previousHealth = BitConverter.ToSingle(healthBytes);
                return TrySetIndirectFloat(0.01f, basePlayerPointer_ch, HealthOffset, false);
            },
            EffectMutex.SetHealth).WhenCompleted.Then(_ =>
        {
            Connector.SendMessage($"Critical state healed.");
            TrySetIndirectFloat(previousHealth, basePlayerPointer_ch, HealthOffset, false);
        });
    }

    // Gives grenades to the player. Use negative amount to take grenades away.
    // Warthogs are optional since they are free vehicles and hence specially useful.
    public void GiveGrenades(EffectRequest request, int amount, bool includeWarthogs, string message)
    {
        TryEffect(request, () => IsReady(request),
            () =>
            {
                if (!TryGetIndirectByteArray(basePlayerPointer_ch, FirstGrenadeTypeAmountOffset, 4, out byte[] grenadeValues))
                {
                    return false;
                }

                if (amount > 0)
                {
                    QueueOneShotEffect((short)OneShotEffect.Give, 0);
                }
                else
                {
                    QueueOneShotEffect((short)OneShotEffect.Yoink, 0);
                }

                for (int i = 0; i < grenadeValues.Length; i++)
                {
                    if (i == 3 && !includeWarthogs)
                    {
                        // Skip giving throwable warthogs.
                        continue;
                    }

                    int value = grenadeValues[i];
                    value = Math.Max(value + amount, 0);
                    grenadeValues[i] = BitConverter.GetBytes(value)[0];
                }

                if (!TrySetIndirectByteArray(grenadeValues, basePlayerPointer_ch, FirstGrenadeTypeAmountOffset))
                {
                    return false;
                }

                return true;
            },
            () => Connector.SendMessage($"{request.DisplayViewer} {message} some grenades."),
            true, EffectMutex.SetGrenades);
    }
}