using ConnectorLib.Inject.AddressChaining;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        private const long UnstableAirtimeInjectionOffset = 0xBB422E;
        private const string UnstableAirtimeId = "unstableAirtime";

        /// <summary>
        /// Injects code that makes the player accelerate uncontrollably while on air.
        /// </summary>
        private bool InjectUnstableAirtime()
        {
            UndoInjection(UnstableAirtimeId);
            var speedWritingInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + UnstableAirtimeInjectionOffset); //movsd [rbx+24],xmm0
            int bytesToReplaceLength = 0xE;
            float speedFactor = 1.05f;

            // Replaced bytes:
            //halo1.dll + BB422E - F2 0F11 43 24 - movsd[rbx + 24],xmm0
            //halo1.dll + BB4233 - 89 43 2C - mov[rbx + 2C],eax
            //halo1.dll + BB4236 - 8B 85 34040000 - mov eax,[rbp+00000434]
            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(speedWritingInstr_ch, bytesToReplaceLength);
            ReplacedBytes.Add((UnstableAirtimeId, injectionAddress, originalBytes));

            int caveDataOffset = 0x130;

            // Multiplies the speed in xmm0 by the given factor.
            byte[] newBytes = new byte[] {
                0x48, 0x83, 0xEC, 0x10, // sub rsp, 0x10
                0xf3, 0x0f, 0x7f, 0x14, 0x24, // movdqu [rsp], xmm2 // back up xmm2 on the stack, update rsp to match
                0x0f, 0x57, 0xd2, // xorps xmm2, xmm2 (to clear it)
                0xf3, 0xf, 0x6f, 0x15 }.AppendNum(caveDataOffset - 0x8 - 0x5 - 0x4 - 0x3)
            .Append(
                0x0f, 0x59, 0xc2, //mulps xmm0, xmm2
                0xf3, 0x0f, 0x6f, 0x14, 0x24, // movdqu xmm2,[rsp]
                0x48, 0x83, 0xc4, 0x10); // add rsp, 0x10

            byte[] caveBytes = newBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            long cavePointer = CodeCaveInjection(speedWritingInstr_ch, bytesToReplaceLength, caveBytes);
            CreatedCaves.Add((UnstableAirtimeId, cavePointer, StandardCaveSizeBytes));

            // Set the in place data
            AddressChain dataPointer = AddressChain.Absolute(Connector, cavePointer + caveDataOffset);
            dataPointer.Offset(0).SetFloat(speedFactor);
            dataPointer.Offset(4).SetFloat(speedFactor); // xmm0 has two floats and multiplies at the same time.

            return true;
        }
    }
}