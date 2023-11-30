using CrowdControl.Common;
using CrowdControl.Games.Packs.MCCHaloCE.Effects;
using System.Collections.Generic;

namespace CrowdControl.Games.Packs.MCCHaloCE;

public partial class MCCHaloCE
{
    private float PlayerReceivedDamageFactor = 1;
    private float OthersReceivedDamageFactor = 1;
    private bool InstakillEnemies = false;

    /// <summary>
    /// Sets the factors that multiplies the damage received by units.
    /// </summary>
    /// <param name="playerFactor">Factor for damage RECEIVED by the player.</param>
    /// <param name="npcFactor">Factor for damage RECEIVED by NPCs, including vehicles.</param>
    /// <param name="instakillEnemies">If true, any NPC received damage is replaced by an extremly high value.
    /// Made almost obsolete by the "omnipotent" cheat, since that allows to kill even dropships.</param>
    public void SetDamageFactors(EffectRequest request, float? playerFactor, float? npcFactor, bool? instakillEnemies, string startMessage, OneShotEffect? sound = null)
    {
        List<string> mutex = new();
        if (playerFactor != null) { mutex.Add(EffectMutex.PlayerReceivedDamage); }
        if (npcFactor != null || instakillEnemies != null) { mutex.Add(EffectMutex.NPCReceivedDamage); }
        StartTimed(request,
                () => { return IsReady(request) && PlayerReceivedDamageFactor == 1 && OthersReceivedDamageFactor == 1 && !InstakillEnemies; },
                () =>
                {
                    if (sound != null)
                    {
                        switch (sound)
                        {
                            case OneShotEffect.HealingBullets: QueueOneShotEffect((short)sound, 0); break;
                            case OneShotEffect.HeavenOrHell: QueueOneShotEffect((short)sound, 0); break;
                            case OneShotEffect.QuadDamage: QueueOneShotEffect((short)sound, 0); break;
                            case OneShotEffect.EnemyGodModeS: QueueOneShotEffect((short)sound, 0); break;
                            case OneShotEffect.GlassCannonS: QueueOneShotEffect((short)sound, 0); break;
                            case OneShotEffect.NerWarS: QueueOneShotEffect((short)sound, 0); break;
                            case OneShotEffect.GodModeS: QueueOneShotEffect((short)sound, 0); break;
                            default: break;
                        }
                    }

                    Connector.SendMessage($"{request.DisplayViewer} {startMessage}");
                    if (playerFactor != null)
                    {
                        PlayerReceivedDamageFactor = playerFactor ?? 1;
                    }

                    if (npcFactor != null)
                    {
                        OthersReceivedDamageFactor = npcFactor ?? 1;
                    }

                    if (instakillEnemies != null)
                    {
                        InstakillEnemies = instakillEnemies ?? false;
                    }

                    return InjectConditionalDamageMultiplier();
                },
                mutex.ToArray())
            .WhenCompleted.Then(_ =>
            {
                if (playerFactor != null)
                {
                    PlayerReceivedDamageFactor = 1;
                }
                if (npcFactor != null)
                {
                    OthersReceivedDamageFactor = 1;
                }
                if (instakillEnemies != null)
                {
                    InstakillEnemies = false;
                }

                string endMessageStart = "";
                if (playerFactor != null)
                {
                    if (instakillEnemies != null || npcFactor != null)
                    {
                        endMessageStart = "All";
                    }
                    else
                    {
                        endMessageStart = "Your";
                    }
                }
                else
                {
                    endMessageStart = "NPC";
                }

                Connector.SendMessage($"{endMessageStart} damage is back to normal.");
                if (ShouldInjectDamageFactors)
                {
                    InjectConditionalDamageMultiplier();
                    return;
                }

                UndoInjection(OnDamageConditionalId);
            });
    }
}