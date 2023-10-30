using ConnectorLib.Inject.AddressChaining;
using System;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        private const string IsInGameplayPollingId = "isInGameplayPollingId";

        // Functions that are constantly read during gameplay, but not when paused, loading or on the main menu.
        // Using two to have redundancy on some edge cases where one is not running.
        private const long IsInGameplayPollInjectionOffset1 = 0xBB331D;

        private const long IsInGameplayPollInjectionOffset2 = 0xAD1EA1;

        // Points to the var that is constantly changed while in gameplay and not when not in gameplay.
        private AddressChain? isInGameplayPollingPointer = null;

        private long previousGamplayPollingValue = 69420;
        private bool currentlyInGameplay = false;

        /// <summary>
        /// Returns true if the game is not closed, paused, or in a menu. Returns true during cutscenes.
        /// </summary>
        private bool IsInGameplayCheck()
        {
            if (isInGameplayPollingPointer == null)
            {
                CcLog.Message("Gameplay polling pointer is null");
                return false;
            }

            if (!isInGameplayPollingPointer.TryGetLong(out long value))
            {
                CcLog.Message("Could not retrieve the gameplay polling variable.");

                return false;
            }

            if (value == previousGamplayPollingValue)
            {
                CcLog.Debug("Gameplay polling pointer is unchanged, currently " + value);
                return false;
            }

            previousGamplayPollingValue = value;
            CcLog.Debug("Gameplay polling pointer changed to " + value);

            return true;
        }

        /// <summary>
        /// Inserts code that constantly writes to a variable, increasing it weach time. The code is executed every frame of gameplay,
        /// so we can use it to deduce if we are in gameplay (it changes constantly)
        /// or does not change/it's pointer does not exist (menu, pause, or loading screen).
        /// </summary>
        private void InjectIsInGameplayPolling()
        {
            //Debug_ManuallySetHalo1BaseAddress();
            UndoInjection(IsInGameplayPollingId);
            CcLog.Message("Injecting polling to know if we are in gameplay.---------------------------");

            // Original bytes for first polling injection. Total length: 0x13
            //halo1.dll + BB331D - 44 3B CE   - cmp r9d,esi
            //halo1.dll + BB3320 - 48 0F44 C1 - cmove rax,rcx
            //halo1.dll + BB3324 - F2 0F10 00 - movsd xmm0,[rax]
            //halo1.dll + BB3328 - F2 0F11 85 B0030000 - movsd[rbp + 000003B0],xmm0

            // Original bytes for second polling injection. Total length: 0x10
            //halo1.dll + AD1EA1 - C7 44 24 38 3333D4C2 - mov[rsp + 38],C2D43333 { -106.10 }
            //halo1.dll + AD1EA9 - C7 44 24 40 000096C2 - mov[rsp + 40],C2960000 { -75.00 }

            // I'm hooking to more than one function to have redundancy. The amount of change in the var is not important, just that it changes
            // when in gamplay and does not when not.
            AddressChain onlyRunOnGameplayInstruction1_ch = AddressChain.Absolute(Connector, halo1BaseAddress + IsInGameplayPollInjectionOffset1);
            AddressChain onlyRunOnGameplayInstruction2_ch = AddressChain.Absolute(Connector, halo1BaseAddress + IsInGameplayPollInjectionOffset2);
            int bytesToReplaceLength1 = 0x13;
            int bytesToReplaceLength2 = 0x10;

            (long injectionAddress1, byte[] originalBytes1) = GetOriginalBytes(onlyRunOnGameplayInstruction1_ch, bytesToReplaceLength1);
            (long injectionAddress2, byte[] originalBytes2) = GetOriginalBytes(onlyRunOnGameplayInstruction2_ch, bytesToReplaceLength2);
            ReplacedBytes.Add((IsInGameplayPollingId, injectionAddress1, originalBytes1));
            ReplacedBytes.Add((IsInGameplayPollingId, injectionAddress2, originalBytes2));

            CcLog.Message("Injection address 1: " + injectionAddress1.ToString("X"));
            CcLog.Message("Injection address 2: " + injectionAddress2.ToString("X"));

            IntPtr isInGameplayPollPointerPointer = CreateCodeCave(ProcessName, 8);
            CreatedCaves.Add((IsInGameplayPollingId, (long)isInGameplayPollPointerPointer, 8));
            CcLog.Message("Polling var pointer: " + ((long)isInGameplayPollPointerPointer).ToString("X"));
            isInGameplayPollingPointer = AddressChain.Absolute(Connector, (long)isInGameplayPollPointerPointer);

            var variableWriter = new byte[]
            {
            0x50, // push rax
            0x48, 0xA1 }.AppendNum((long)isInGameplayPollPointerPointer).Append( // mov rax, [var]
            0x48, 0x83, 0XC0, 0X01, // add rax, 1
            0x48, 0xA3).AppendNum((long)isInGameplayPollPointerPointer).Append( // mov [var], rax
            0x58);// pop rax

            byte[] fullCave1Contents = variableWriter
                .Concat(originalBytes1)
                .Concat(GenerateJumpBytes(injectionAddress1 + bytesToReplaceLength1, bytesToReplaceLength1)).ToArray();
            byte[] fullCave2Contents = variableWriter
                .Concat(originalBytes2)
                .Concat(GenerateJumpBytes(injectionAddress2 + bytesToReplaceLength2, bytesToReplaceLength2)).ToArray();

            long cavePointer1 = CodeCaveInjection(onlyRunOnGameplayInstruction1_ch, bytesToReplaceLength1, fullCave1Contents);
            CreatedCaves.Add((IsInGameplayPollingId, cavePointer1, StandardCaveSizeBytes));
            long cavePointer2 = CodeCaveInjection(onlyRunOnGameplayInstruction2_ch, bytesToReplaceLength2, fullCave2Contents);
            CreatedCaves.Add((IsInGameplayPollingId, cavePointer2, StandardCaveSizeBytes));

            CcLog.Message("Injection of polling to know if we are in gameplay finished.----------------------");
        }
    }
}