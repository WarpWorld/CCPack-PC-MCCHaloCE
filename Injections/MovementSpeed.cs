using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Games.Packs.MCCHaloCE.Utilites.ByteArrayBuilding;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        private const string SpeedFactorId = "speedfactor";
        private const long SpeedModifierInjectionOffset = 0xB35E81;

        /// <summary>
        /// Injects code that changes the speed of the player and/or the enemies.
        /// </summary>
        /// <param name="boostPlayer"></param>
        /// <param name="boostOthers"></param>
        private bool InjectSpeedMultiplier()
        {
            UndoInjection(SpeedFactorId);
            bool boostPlayer = PlayerSpeedFactor != 1;
            bool boostOthers = OthersSpeedFactor != 1;

            byte[] jumpStatement;
            byte jumpLength = 0x73;
            if (boostPlayer)
            {
                jumpStatement = boostOthers
                    ? new byte[] { 0x90, 0x90 } // nop nop, always boost
                    : new byte[] { 0x75, jumpLength }; // jne,skip enemies
            }
            else
            {
                jumpStatement = boostOthers
                   ? new byte[] { 0x74, jumpLength } // je, skip player
                   : new byte[] { 0xEB, jumpLength }; // jmp, skip everything.
            }

            // Replaced bytes:
            //halo1.dll + B35E81 - 83 FB FF              -cmp ebx,-01 { 255 }
            //halo1.dll + B35E84 - 48 0F44 D0 - cmove rdx,rax
            //halo1.dll + B35E88 - F2 0F11 02 - movsd[rdx],xmm0
            //halo1.dll + B35E8C - 8B 47 08 - mov eax,[rdi+08]
            //halo1.dll + B35E8F - 89 42 08 - mov[rdx + 08],eax

            var speedWritingInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + SpeedModifierInjectionOffset); //cmp ebx, -01. I take the cmp to avoid conflicts with my own Jccs.
            int bytesToReplaceLength = 0x11;

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(speedWritingInstr_ch, bytesToReplaceLength);
            ReplacedBytes.Add((SpeedFactorId, injectionAddress, originalBytes));

            // Checks if the current unit is the player or not, and adds the current speed * the speed factor if that type of unit should be boosted.
            int caveDataOffset = 0x130; //0x12F; // Make sure it is divisible by 16 or xmm functions can crash
            byte[] newBytes = new byte[] {
                0x51, // push rcx,
                0x48, 0x8B, 0x8A, 0x8b, 0x09, 0x00, 0x00, // mov rcx,[rdx + 0x98b] ; (0x9a3 /*player discriminator value*/ - 0x18 /*x coord offset)
                0x48, 0x81, 0xf9, 0x3f, 0x00, 0x00, 0x00, // cmp rcx, 0x3f (63 in decimal)
                0x59 } // pop rcx
            .Append(
                jumpStatement) // TODO: these have relative jumps
            .Append(
                0x48, 0x83, 0xEC, 0x10, // sub rsp, 0x10
                0xf3, 0x0f, 0x7f, 0x1C, 0x24, // movdqu [rsp], xmm3 // back up xmm3
                0x48, 0x83, 0xEC, 0x10, // sub rsp, 0x10
                0xf3, 0x0f, 0x7f, 0x14, 0x24, // movdqu [rsp], xmm2// back up xmm2
                0x0f, 0x57, 0xdb, // xorps xmm3, xmm3 (to clear it)
                0x50, // push rax
                0x8b, 0x42, 0x18, // mov eax, [rdx + 18] // save the fourth value on the speeds so we can set it to 0 temporarily
                0xc7, 0x42, 0x18, 0x0, 0x0, 0x0, 0x0, // mov [rdx + 18], 0
                0xf3, 0x0f, 0x6f, 0x5a, 0xC, //movdqu xmm3,[rdx+C]
                0x89, 0x42, 0x18, //mov [rdx+18], eax
                0xE8, 0x0, 0x0, 0x0, 0x0, // call to next instruction. Will put rip on rax
                0x90, //nop
                0x48, 0x8b, 0x04, 0x24, // mov rax, [rsp] ; retrieve rip after call
                0x48, 0x83, 0xc4, 0x08, // add rsp, 0x8; move the stack back to before the call

                // multiply by different factors depending on if the curren tunit is the player
                0x51, // push rcx,
                0x48, 0x8B, 0x8A, 0x8b, 0x09, 0x00, 0x00, // mov rcx,[rdx + 0x98b] ; (0x9a3 /*player discriminator value*/ - 0x18 /*x coord offset)
                0x48, 0x81, 0xf9, 0x3f, 0x00, 0x00, 0x00, // cmp rcx, 0x3f (63 in decimal)
                0x59, // pop rcx
                0x75).AppendRelativePointer("Read non-player factor", 0x8)// jne 8
            .Append(
                0x48, 0x05).AppendNum(caveDataOffset - 0x3f) // add rax, [(offset for speed factors for the player)]
            .Append(
                0xEB).AppendRelativePointer("Skip non-player factor", 0x6)
            .LocalJumpLocation("Read non-player factor").Append(
                0x48, 0x05).AppendNum(caveDataOffset - 0x3f + (2 + 6) + (0x4 * 4)) // add rax, [(offset for speed factors for the non-players)]
            .LocalJumpLocation("Skip non-player factor")

            // Move factor to XMM and multiply the speed by it.
            .Append(
                0xf3, 0x0f, 0x6f, 0x10, // movdqu xmm2, [rax]
                0x58,  // pop rax
                0x0f, 0x59, 0xda, // mulps xmm3, xmm2
                0x0f, 0x58, 0xc3) // addps xmm0, xmm3
            // restore the xmm registers used
            .Append(
                0xf3, 0x0f, 0x6f, 0x14, 0x24, // movdqu xmm2,[rsp]
                0x48, 0x83, 0xc4, 0x10, // add rsp, 0x10
                0xf3, 0x0f, 0x6f, 0x1c, 0x24, // movdqu xmm3,[rsp]
                0x48, 0x83, 0xc4, 0x10); // add rsp, 0x10

            byte[] caveBytes = newBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            long cavePointer = CodeCaveInjection(speedWritingInstr_ch, bytesToReplaceLength, caveBytes);
            CreatedCaves.Add((SpeedFactorId, cavePointer, StandardCaveSizeBytes));

            // Set the in place data
            AddressChain dataPointer = AddressChain.Absolute(Connector, cavePointer + caveDataOffset);
            dataPointer.Offset(0).SetFloat(PlayerSpeedFactor);
            dataPointer.Offset(4).SetFloat(PlayerSpeedFactor);
            dataPointer.Offset(8).SetFloat(PlayerSpeedFactor);
            dataPointer.Offset(12).SetFloat(1);
            dataPointer.Offset(16).SetFloat(OthersSpeedFactor);
            dataPointer.Offset(20).SetFloat(OthersSpeedFactor);
            dataPointer.Offset(24).SetFloat(OthersSpeedFactor);
            dataPointer.Offset(28).SetFloat(1);

            return true;
        }
    }
}