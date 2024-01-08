using System;
using System.Linq;
using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Games.Packs.MCCHaloCE.Utilities.ByteArrayBuilding;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE.Injections;

public partial class MCCHaloCE
{
    private const string IsInGameplayPollingId = "isInGameplayPollingId";

    // Functions that are constantly read during gameplay, but not when paused, loading or on the main menu.
    // Using two to have redundancy on some edge cases where one is not running.
    private const long IsInGameplayPollInjectionOffset1 = 0xBB331D;

    private const long IsInGameplayPollInjectionOffset2 = 0xAD1EA1;
    private const long IsInGameplayPollInjectionOffset3 = 0xB9D425; // reads the shield value two instructions after
    private const long IsInGameplayPollInjectionOffset4 = 0xC507F0; // reads the shield value two instructions after
    private const long IsInGameplayPollInjectionOffset5 = 0xAD1B02; // reads the shield regen variable
    private const long IsInGameplayPollInjectionOffset6 = 0xB9D2F2; // reads the shield regen variable a few instructions after
    private const long IsInGameplayPollInjectionOffset7 = 0xB9D3AA; // reads the shield regen variable a few instructions after

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

        // If the current value is 0, this is most likely after resetting to try to repair an infinite pause.
        if (value == 0)
        {
            CcLog.Debug("Gameplay polling value is 0");
            return false;
        }

        previousGamplayPollingValue = value;
        CcLog.Debug("Gameplay polling pointer changed to " + value);

        // On successful gameplay polling var change, we no longer need to ignore pause detection because it is working again.
        if (IgnoreIsInGameplayPolling)
        {
            CcLog.Message("Successful IsInGameplayCheck. No longer ignoring pausing.");
            IgnoreIsInGameplayPolling = false;
        }

        lastSuccessfulIsInGameplayCheck = DateTime.Now;

        return true;
    }

    private void MakeGameplayPollingInjection(int injectionNumber, long injectionOffset, int bytesToReplaceLength, byte[] variableWriter)
    {
        AddressChain onlyRunOnGameplayInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + injectionOffset);

        (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(onlyRunOnGameplayInstruction_ch, bytesToReplaceLength);
        ReplacedBytes.Add((IsInGameplayPollingId, injectionAddress, originalBytes));
        CcLog.Message($"Injection address {injectionNumber}: " + injectionAddress.ToString("X"));

        byte[] fullCave1Contents = variableWriter
            .Concat(originalBytes)
            .Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength, bytesToReplaceLength)).ToArray();

        long cavePointer1 = CodeCaveInjection(onlyRunOnGameplayInstruction_ch, bytesToReplaceLength, fullCave1Contents);
        CreatedCaves.Add((IsInGameplayPollingId, cavePointer1, Utilities.MCCHaloCE.StandardCaveSizeBytes));
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

        IntPtr isInGameplayPollPointer = CreateCodeCave(Packs.MCCHaloCE.MCCHaloCE.ProcessName, 8);
        CreatedCaves.Add((IsInGameplayPollingId, (long)isInGameplayPollPointer, 8));
        CcLog.Message("Polling var pointer: " + ((long)isInGameplayPollPointer).ToString("X"));
        isInGameplayPollingPointer = AddressChain.Absolute(Connector, (long)isInGameplayPollPointer);

        var variableWriter = new byte[]
        {
            0x50, // push rax
            0x48, 0xA1 }.AppendNum((long)isInGameplayPollPointer).Append( // mov rax, [var]
            0x48, 0x83, 0XC0, 0X01, // add rax, 1
            0x48, 0xA3).AppendNum((long)isInGameplayPollPointer).Append( // mov [var], rax
            0x58);// pop rax

        // I'm hooking to more than one function to have redundancy. The amount of change in the var is not important, just that it changes
        // when in gamplay and does not when not.

        // Original bytes for first polling injection. Total length: 0x13
        //halo1.dll + BB331D - 44 3B CE   - cmp r9d,esi
        //halo1.dll + BB3320 - 48 0F44 C1 - cmove rax,rcx
        //halo1.dll + BB3324 - F2 0F10 00 - movsd xmm0,[rax]
        //halo1.dll + BB3328 - F2 0F11 85 B0030000 - movsd[rbp + 000003B0],xmm0
        MakeGameplayPollingInjection(1, IsInGameplayPollInjectionOffset1, 0x13, variableWriter);

        // Original bytes for second polling injection. Total length: 0x10
        //halo1.dll + AD1EA1 - C7 44 24 38 3333D4C2 - mov[rsp + 38],C2D43333 { -106.10 }
        //halo1.dll + AD1EA9 - C7 44 24 40 000096C2 - mov[rsp + 40],C2960000 { -75.00 }
        MakeGameplayPollingInjection(2, IsInGameplayPollInjectionOffset2, 0x10, variableWriter);

        // Commenting out this one. Moving a LEA to a cave is problematic.
        ////halo1.dll + B9D425 - 8D 41 08 - lea eax,[rcx+08]
        ////halo1.dll + B9D428 - 41 3B F2              -cmp esi,r10d
        ////halo1.dll + B9D42B - 4C 0F44 E0 - cmove r12,rax
        ////halo1.dll + B9D42F - F3 41 0F10 04 24 - movss xmm0,[r12]
        //MakeGameplayPollingInjection(3, IsInGameplayPollInjectionOffset3, 0x10, variableWriter);

        //halo1.dll + C507F0 - 49 81 C3 D4000000     -add r11,000000D4 { 212 }
        //halo1.dll + C507F7 - 4C 03 D9 - add r11,rcx
        //halo1.dll + C507FA - 41 83 FA FF           -cmp r10d,-01 { 255 }
        //halo1.dll + C507FE - 4C 0F44 DF - cmove r11,rdi
        //halo1.dll + C50802 - F3 41 0F10 0B - movss xmm1,[r11]
        MakeGameplayPollingInjection(4, IsInGameplayPollInjectionOffset4, 0x17, variableWriter);

        //halo1.dll + AD1B02 - 8A 8A C2000000        -mov cl,[rdx+000000C2]
        //halo1.dll + AD1B08 - 8A 97 B5090000 - mov dl,[rdi+000009B5]
        //halo1.dll + AD1B0E - C0 E9 04 - shr cl,04 { 4 }
        MakeGameplayPollingInjection(5, IsInGameplayPollInjectionOffset5, 0xf, variableWriter);

        //halo1.dll + B9D2F2 - B8 60200000 - mov eax,00002060 { 8288 }
        //halo1.dll + B9D2F7 - 48 89 6C 24 38 - mov[rsp + 38],rbp
        //halo1.dll + B9D2FC - 0FB7 4D 00 - movzx ecx,word ptr[rbp + 00]
        MakeGameplayPollingInjection(6, IsInGameplayPollInjectionOffset6, 0xe, variableWriter);

        //halo1.dll + B9D3AA - B8 FFEF0000 - mov eax,0000EFFF { 61439 }
        //halo1.dll + B9D3AF - 0F28 FE - movaps xmm7,xmm6
        //halo1.dll + B9D3B2 - 66 23 C8 - and cx,ax
        //halo1.dll + B9D3B5 - 66 89 4D 00 - mov[rbp + 00],cx
        MakeGameplayPollingInjection(7, IsInGameplayPollInjectionOffset7, 0xf, variableWriter);

        CcLog.Message("Injection of polling to know if we are in gameplay finished.----------------------");
    }
}