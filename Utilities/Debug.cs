using System;
using System.Text;
using ConnectorLib.Inject.AddressChaining;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE.Utilities;

public partial class MCCHaloCE
{
    private const bool DEBUG = true;

    // If true, injection won't replace game code. Useful for testing assembly generation without crashing the game.
    private const bool DONT_OVERWRITE = false;

    /// <summary>
    /// Writes a byte array as a hexadecimal string.
    /// </summary>
    /// <param name="bytes">Byte array to write.</param>
    /// <param name="header">Tooltip printed before the array.</param>
    private void WriteByteArray(byte[] bytes, string header = null)
    {
        StringBuilder s = new StringBuilder();
        if (header != null)
        {
            s.Append(header + ": ");
        }

        foreach (byte b in bytes)
        {
            s.Append(b.ToString("X2") + " ");
        }

        s.AppendLine();
        CcLog.Message($"{s}");
    }

    private void Debug_ManuallySetHalo1BaseAddress()
    {
        halo1BaseAddress_ch = AddressChain.ModuleBase(Connector, "halo1.dll");
        if (!halo1BaseAddress_ch.Calculate(out long halo1BaseAddress))
        {
            throw new Exception("Could not get halo1.dll base address");
        }

        this.halo1BaseAddress = halo1BaseAddress;
        CcLog.Message("Halo 1 base address: " + halo1BaseAddress);
    }

    //#if DEVELOPMENT
    //        /// <summary>
    //        /// Debug function, used to extract pointers to units without and storing them in memory, instead of needing a breakpoint.
    //        /// </summary>
    //        private void InjectUnitDirectionsExtractor()
    //        {
    //            // Parameterize it later.
    //            int unitTypeDiscriminatorOffset = 0x9a3;
    //            int playerDiscriminator = 0x3f; // 63

    //            // this is the previous instruction, so we can inject the jump without having to avoid overwriting a Jcc.
    //            var onDamageHealthSubstractionInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + 0xb9fdf3);
    //            int bytesToReplaceLength = 0x14;

    //            IntPtr unitStructurePointerPointer = CreateCodeCave(ProcessName, 8); // todo: change the offset to point to the structure start.
    //            CreatedCaves.Add((PlayerPointerId, (long)unitStructurePointerPointer, 8));

    //            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength);
    //            ReplacedBytes.Add((OnDamageConditionalId, injectionAddress, originalBytes));
    //            CcLog.Message($"Pointer to unit: {((long)unitStructurePointerPointer).ToString("X")}");

    //            // Hooks to a place (a damage receiving function) that reads the pointer to the unit structure and stores it, unless it is the player's.
    //            byte[] prependedBytes = new byte[]
    //            {
    //                0x51, // push rcx
    //                0x48, 0x8b, 0xcb, // mov rcx, rbx
    //                0x48, 0x81, 0xc1 }.AppendNum(unitTypeDiscriminatorOffset) // mov rcx, <discriminator offset>
    //            .Append(
    //                0x81, 0x39).AppendNum(playerDiscriminator) // cmp [rcx], <discriminator value>
    //            .Append(
    //                0x74).AppendRelativePointer("endOfPrependedBytes", 0x0F).Append( // je to original bytes
    //                0x50, // push rax,
    //                0x48, 0x8b, 0xc1, // mov rax, rcx
    //                0x48, 0xA3).AppendNum((long)unitStructurePointerPointer) // mov rax to pointer location
    //            .Append(
    //                0x58, // pop rax
    //                0x59  // pop rcx
    //            );

    //            byte[] caveBytes = prependedBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
    //            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

    //            long cavePointer = CodeCaveInjection(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength, caveBytes);
    //            CreatedCaves.Add((OnDamageConditionalId, cavePointer, StandardCaveSizeBytes));
    //        }

    //#endif
}