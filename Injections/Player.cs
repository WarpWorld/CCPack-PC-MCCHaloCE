using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Games.Packs.MCCHaloCE.Utilites.ByteArrayBuilding;
using System;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE;

public partial class MCCHaloCE
{
    // Points to the start of the player unit data structure.
    private AddressChain? basePlayerPointer_ch = null;

    private const string PlayerPointerId = "playerPointer";
    private const long PlayerBasePointerInjectionOffset = 0xC50557;

    // Player pointer offsets
    // Relative to base player pointer. Grenade type value offset + grenade of type 1 amount offset.
    // Each grenade type has a 1 byte amount. 2 total bytes in normal halo, 4 in cursed.
    private const int FirstGrenadeTypeAmountOffset = 0x2d6 + 0x26;

    private const int XCoordOffset = 0x18; // Y and Z are at a 4 and 8 offset from it respectevely.
    private const int XSpeedOffset = 0x24; // Y and Z are at a 4 and 8 offset from it respectevely.

    /// <summary>
    /// Injects code that writes a pointer to the beginning of the player's data location, <see cref="basePlayerPointer_ch"/>.
    /// </summary>
    private void InjectPlayerBaseReader()
    {
        try
        {
            UndoInjection(PlayerPointerId);
        }
        catch (Exception e)
        {
            CcLog.Error(e, "Undoing player base injection caused a crash - player base reader.");
        }

        CcLog.Message("Injecting player base reader.---------------------------");

        // Replaced bytes:
        //halo1.dll + C50557 - F3 0F10 96 9C000000 - movss xmm2,[rsi+0000009C]
        //halo1.dll + C5055F - 0F57 C9 - xorps xmm1,xmm1
        //halo1.dll + C50562 - 0F2F D1 - comiss xmm2,xmm1
        //halo1.dll + C50565 - 0F86 E1000000 - jbe halo1.dll + C5064C
        //halo1.dll + C5056B - 45 84 FF - test r15b,r15b

        var valueReadingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + PlayerBasePointerInjectionOffset);

        int bytesToReplaceLength = 0x17;

        (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(valueReadingInstruction_ch, bytesToReplaceLength);

        ReplacedBytes.Add((PlayerPointerId, injectionAddress, originalBytes));
        IntPtr playerPointer = CreateCodeCave(ProcessName, 8); // todo: change the offset to point to the structure start.
        CreatedCaves.Add((PlayerPointerId, (long)playerPointer, 8));
        basePlayerPointer_ch = AddressChain.Absolute(Connector, (long)playerPointer);

        CcLog.Message("Player pointer: " + ((long)playerPointer).ToString("X"));

        CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

        // Simply hooks to an instruction where the player structure location is read, and stores it.
        byte[] prependedBytes = new byte[]
            {
                0x50, // push rax,
                0x48, 0x8B, 0XC6 } // mov rax, rsi
            .Append(
                0x48, 0xA3).AppendNum((long)playerPointer) // mov [playerPointer], rax. Note: 0x48 means we're using long mode (i.e. 64 bits instead of 32)
            .Append(
                0x58); // pop rax

        byte[] originalWithUnCondJump = AppendUnconditionalJump(originalBytes, injectionAddress + bytesToReplaceLength);
        byte[] originalWithFixedJcc = FixRelativeJccAfterRelocation(originalWithUnCondJump, injectionAddress, 0xE, 0x6);
        byte[] fullCaveContents = prependedBytes.Concat(originalWithFixedJcc).ToArray();

        long cavePointer = CodeCaveInjection(valueReadingInstruction_ch, bytesToReplaceLength, fullCaveContents);
        CreatedCaves.Add((PlayerPointerId, cavePointer, StandardCaveSizeBytes));

        CcLog.Message("Player base injection finished.---------------------------");
    }
}