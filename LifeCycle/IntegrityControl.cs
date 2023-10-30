using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Common;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Timers;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    // Functionality related to checking and fixing the state in case of changes to the process or game memory.
    public partial class MCCHaloCE
    {
        // Allows us to access the instance form the timer static methods.
        private static MCCHaloCE instance;

        // Base address of halo1.dll in memory. Using relative addresses to this is much more reliable than absolute addresses.
        private AddressChain halo1BaseAddress_ch;

        private long halo1BaseAddress;

        private Process mccProcess = null;

        private bool IsProcessReady = false;

        // Periodically checks if injections are needed.
        private static System.Timers.Timer injectionCheckerTimer;

        // Starts the mechanisms that make sure injections are done and redone if overwritten.
        private void InitIntegrityControl()
        {
            if (injectionCheckerTimer != null)
            {
                // Clear residual timers.
                injectionCheckerTimer.Enabled = false;
                injectionCheckerTimer.Dispose();
                injectionCheckerTimer = null;
            }

            if (!DONT_OVERWRITE)
            {
                CreatePeriodicStateChecker();
            }
            else
            {
                CcLog.Message("Debugging mode. Injections are not automatic.");
                halo1BaseAddress_ch = AddressChain.ModuleBase(Connector, "halo1.dll");
                if (!halo1BaseAddress_ch.Calculate(out long halo1BaseAddress))
                {
                    CcLog.Message("Could not get halo1.dll base address."); return;
                }
            }
        }

        // Activates the timer that periodically checks if code injections should be done, for instance, when going back to the main menu.
        // Also sets the variable that determines if the game is in gameplay and not a menu/loading screen.
        private void CreatePeriodicStateChecker()
        {
            CcLog.Message("Create periodic injection checker.");
            injectionCheckerTimer = new System.Timers.Timer(500);
            injectionCheckerTimer.Elapsed += OnPeriodicStateCheck;
            injectionCheckerTimer.AutoReset = true;
            injectionCheckerTimer.Enabled = true;
        }

        // Checks if the code injections should be done, by checking if an injection point still has its original code.
        private bool WereInjectionsOverwrittenByTheGameOrOS()
        {
            if (scriptVarInstantEffectsPointerPointer_ch == null)
            {
                CcLog.Message("scriptVarCommPointer was null");
                return true;
            }

            var scriptVarReadingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + ScriptInjectionOffset);

            // original instruction is 0x48, 0x63, 0x42, 0x34, // movsxd  rax,dword ptr [rdx+34]
            // if it is there, the code has been reset and needs to be reinjected. We assume that if one injection was reset, all were.
            byte[] originalInstruction = new byte[] { 0x48, 0x63, 0x42, 0x34 }; // <-- the original instruction at that address
            byte[] bytesAtInjectionPoint = scriptVarReadingInstruction_ch.GetBytes(4);
            if (bytesAtInjectionPoint.Length != 4)
            {
                CcLog.Message("Bytes read had a length different than 4: " + bytesAtInjectionPoint.Length);
                return true;
            }
            for (int i = 0; i < bytesAtInjectionPoint.Length; i++)
            {
                if (originalInstruction[i] != bytesAtInjectionPoint[i])
                {
                    return false;
                }
            }

            CcLog.Message("The bytes read match the original instruction, which means our hooks were overwritten.");
            return true;
        }

        // Inject all needed code, including both permanent injections and injections that only accur is specific state is not the default one.
        private void InjectAllHooks()
        {
            CcLog.Message("Clearing all existing caves");
            AbortAllInjection(false); // Destroy any old caves to prevent memory leaks.

            CcLog.Message("(Re)injecting all hooks");

            InjectScriptHook();
            InjectPlayerBaseReader();
            InjectIsInGameplayPolling();
            InjectLevelSkipper();
            InjectAllWeaponClipAmmoReaders();

            if (ShouldInjectDamageFactors) { InjectConditionalDamageMultiplier(); }
            if (ShouldInjectSpeed) { InjectSpeedMultiplier(); }
        }

        /// <summary>
        /// Disables periodic injection checks, and undoes all current injections.
        /// </summary>
        private void AbortAllInjection(bool disableCheckTimer, bool disableEffectQueue = true)
        {
            if (disableCheckTimer && injectionCheckerTimer != null)
            {
                CcLog.Message("Disabling periodic injection check.");
                injectionCheckerTimer.Enabled = false;
            }

            if (disableEffectQueue)
            {
                CcLog.Message("Disabling H1 one-shot effect queue reading.");
                oneShotEffectSpacingTimer.Enabled = false;
            }

            CcLog.Message("Restoring memory and freeing caves.");

            foreach (var code in ReplacedBytes.Select(x => x.Identifier).Distinct())
            {
                UndoInjection(code);
            }
            // This second loop should be redundant, but just in case there's a cave not related to a replacement.
            foreach (var code in CreatedCaves.Select(x => x.Identifier).Distinct())
            {
                UndoInjection(code);
            }
        }

        // Called by the periodic timer.
        private static void OnPeriodicStateCheck(Object source, ElapsedEventArgs e)
        {
            injectionCheckerTimer.Enabled = false;
            try
            {
                //CcLog.Message("Running timer");
                if (instance == null)
                {
                    CcLog.Message("No effect pack instance."); return;
                }

                // If the process instance is missing or has exited, stop here until a new one is found and ready.
                if (!VerifyOrFixProcessIsReady())
                {
                    CcLog.Message("Process was not ready nor in a fixable wrong state yet.");
                    instance.IsProcessReady = false;
                    return;
                }

                // Recalculate the base address of halo1.dll.
                BaseHaloAddressResult addressResult = RecalculateBaseHaloAddress();
                if (addressResult == BaseHaloAddressResult.Failure)
                {
                    CcLog.Message("Could not properly calculate the base address for halo1.dll.");
                    instance.IsProcessReady = false;
                    return;
                }

                if (addressResult == BaseHaloAddressResult.RecalculatedDifferentFromPrevious ||
                    (addressResult == BaseHaloAddressResult.WasAlreadyCorrect && instance.WereInjectionsOverwrittenByTheGameOrOS()))
                {
                    instance.InjectAllHooks();
                }

                instance.IsProcessReady = true;
                instance.currentlyInGameplay = instance.IsInGameplayCheck();
                if (oneShotEffectSpacingTimer != null)
                {
                    oneShotEffectSpacingTimer.Enabled = true;
                }
            }
            finally
            {
                injectionCheckerTimer.Enabled = true;
            }
        }

        /// <summary>
        /// Verifies that the game process exists, has a valid state, and is currently connected to the Connector.
        /// If that's not the case, it will try to fix it.
        ///
        /// </summary>
        /// <returns>False if it is not ready and it can't currently fix it, true otherwise.</returns>
        private static bool VerifyOrFixProcessIsReady()
        {
            if (instance.mccProcess == null || instance.mccProcess.HasExited)
            {
                CcLog.Message(instance.mccProcess == null
                    ? "Current process instance is null."
                    : $"Current process instance with ID {instance.mccProcess.Id} has exited.");
                CcLog.Message("Looking for new process.");

                var newMccProcess = Process.GetProcessesByName(ProcessName).Where(p => !p.HasExited).FirstOrDefault();
                if (newMccProcess == null)
                {
                    CcLog.Message("New process not yet found.");
                    return false;
                }
                if (instance.mccProcess == null)
                {
                    CcLog.Message($"Swapping null MCC instance with instance with ID {newMccProcess.Id}");
                }
                else
                {
                    CcLog.Message($"Swapping old exited MCC instance with id {instance.mccProcess.Id} with instance with ID {newMccProcess.Id}");
                }

                ProcessModule halo1Module = null;
                foreach (ProcessModule module in newMccProcess.Modules)
                {
                    //CcLog.Message(module.ModuleName);
                    if (module.ModuleName == "halo1.dll")
                    {
                        halo1Module = module;
                        CcLog.Message("Halo 1 base address is " + module.BaseAddress.ToString("X"));
                        break;
                    }
                }
                if (halo1Module == null)
                {
                    CcLog.Message("Halo1.dll module was still not loaded. Retry.");
                    return false;
                }

                instance.mccProcess = newMccProcess;
                instance.halo1BaseAddress_ch = null;

                try
                {
                    CcLog.Message("Disconnecting connector.");
                    instance.Connector.Disconnect();
                    CcLog.Message("Connecting connector.");
                    instance.Connector.Connect();
                }
                catch (Exception exception) { CcLog.Error(exception, "Recconection failed."); }
            }

            return true;
        }

        /// <summary>
        /// Recalculates the halo1.dll base memory address, used as a base for all the injections.
        /// </summary>
        /// <returns>A <see cref="BaseHaloAddressResult"/.></returns>
        private static BaseHaloAddressResult RecalculateBaseHaloAddress()
        {
            try
            {
                AddressChain asdf = AddressChain.ModuleBase(instance.Connector, "halo1.dll");
            }
            catch (Exception ex)
            {
                CcLog.Error(ex, "Problem creating module base.");
            }
            AddressChain reCalculatedhalo1BaseAddress_ch = AddressChain.ModuleBase(instance.Connector, "halo1.dll");
            try
            {
                if (!reCalculatedhalo1BaseAddress_ch.Calculate(out long a))
                {
                    CcLog.Message("Could not get halo1.dll base address."); return BaseHaloAddressResult.Failure;
                }
            }
            catch (Exception ex)
            {
                CcLog.Error(ex, "Problem calculating module base.");
            }
            if (!reCalculatedhalo1BaseAddress_ch.Calculate(out long reCalculatedhalo1BaseAddress))
            {
                CcLog.Message("Could not get halo1.dll base address."); return BaseHaloAddressResult.Failure;
            }

            //CcLog.Message("Current base address: " + reCalculatedhalo1BaseAddress.ToString("X"));
            if (instance.halo1BaseAddress_ch == null || instance.halo1BaseAddress != reCalculatedhalo1BaseAddress)
            {
                CcLog.Message(instance.halo1BaseAddress == null
                    ? $"Halo 1 base address was null. Setting it to {reCalculatedhalo1BaseAddress.ToString("X")}."
                    : $"Halo 1 base address has changed from {instance.halo1BaseAddress.ToString("X")} to {reCalculatedhalo1BaseAddress.ToString("X")}.");
                instance.halo1BaseAddress_ch = reCalculatedhalo1BaseAddress_ch;
                instance.halo1BaseAddress = reCalculatedhalo1BaseAddress;

                return BaseHaloAddressResult.RecalculatedDifferentFromPrevious;
            }

            return BaseHaloAddressResult.WasAlreadyCorrect;
        }
    }
}