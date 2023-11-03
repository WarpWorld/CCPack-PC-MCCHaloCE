using ConnectorLib.Inject.AddressChaining;
using System;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public enum CurrentWeaponType
    {
        NoneDetected = 0,
        ClipBased = 1,
        PlasmaBased = 2,
    }

    public partial class MCCHaloCE
    {
        public AddressChain WeaponClipAmmoPointer_ch = null;
        private short fullChargeValueOffset = 16;

        private const long ClipPointerReadOffset = 0xb76a30;
        private const long HeatReadOffset = 0xb76990;

        private const long ShotDelayConstantWriteOffset = 0xb75a2f;
        private const long SetStartReloadingFlagOffset = 0xb7917d;
        private const long SetChargeInstructionOffset = 0xb75641;

        private const long FullAutoWriteOffset = 0xb799db;
        private const long ShotDelayWriteOffset = 0xb79bc8; // Also b79ba7 a bit before. Each write one different byte.
        private const long IsReloadingWriteOffset = 0xb78373;
        private const long NeverPumpInjectionOffset = 0xb791a3;
        private const long ReloadCountdownWriteOffset = 0xb7530C;
        private const string ClipPointerId = "clipPointer";
        private const string FullerAutoId = "fullerAuto";

        // Injects code that constantly writes to our pointer the clip address of the currently held weapon.
        // Since some plasma weapons don't really have a clip, a different instruction is used to read its heat location, and the clip address is inferred from it.
        public void InjectAllWeaponClipAmmoReaders()
        {
            UndoInjection(ClipPointerId);
            UndoInjection(FullerAutoId);
            CcLog.Message($"Injecting clip ammo pointer readers.---------------------------");

            IntPtr clipPointer = CreateCodeCave(ProcessName, 24);
            CreatedCaves.Add((ClipPointerId, (long)clipPointer, 24));
            short fullChargeValueOffset = 16;
            WeaponClipAmmoPointer_ch = AddressChain.Absolute(Connector, (long)clipPointer);

            CcLog.Message("Weapon clip pointer: " + ((long)clipPointer).ToString("X"));

            InjectWeaponClipAmmoDirectReader((long)clipPointer);
            InjectWeaponClipAmmoThroughHeatReadingReader((long)clipPointer);
            CcLog.Message($"Weapon clip ammo pointer readers injection finished.---------------------------");

            WeaponClipAmmoPointer_ch.Offset(fullChargeValueOffset).SetFloat(1);
        }

        // Injects several effects used to generate the "non-stop shooting" effect.
        public void InjectFullerAuto()
        {
            UndoInjection(FullerAutoId);
            if (!WeaponClipAmmoPointer_ch.Calculate(out long pointerAddress))
            {
                throw new Exception("Could not read weapon clip ammo pointer");
            }
            InjectInstantShots(pointerAddress);
            InjectNeverReloading();
            InjectInstantCharge();
        }

        // Makes the delay between shots 0.
        private void InjectInstantShots(long pointerAddress)
        {
            var shotDelayWritingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + ShotDelayConstantWriteOffset);
            int bytesToReplaceLength = 0xF;

            // Replaced bytes:
            //halo1.dll + B75A2F - BD 01000000 - mov ebp,00000001
            //halo1.dll + B75A34 - 40 02 C5 - add al,bpl
            //halo1.dll + B75A37 - 88 84 1F 28020000 - mov[rdi + rbx + 00000228],al

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(shotDelayWritingInstruction_ch, bytesToReplaceLength);
            ReplacedBytes.Add((FullerAutoId, injectionAddress, originalBytes));
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            byte[] replacementBytes = new byte[]
            {
                0x50, // push rax
                0x53, // push rbx
                0xC6, 0x84, 0x3B, 0x28, 0x02, 0x00, 0x00, 0x7F, //mov [rbx+rdi+00000228],7F. This sets the fire delay as the top value so there is not delay.
                0x48, 0xb8 }.AppendNum(pointerAddress).Append(// mov rax, <pointerAddress>
                0x48, 0x8b, 0x18, // mov rbx, [rax]
                0x48, 0x83, 0xeb, 0x76,// sub rbx, 76
                0xc7, 0x03, 0x7f, 0x03, 0x00, 0x00, // mov [rbx], 0x037f (895 decimal). This makes the throwing pistol always throw, every frame.
                0x5b, // pop rbx
                0x58); // pop rax

            // Note: we are not adding the original bytes on purpose.
            byte[] fullCaveBytes = replacementBytes.Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength, bytesToReplaceLength)).ToArray();

            long cavePointer = CodeCaveInjection(shotDelayWritingInstruction_ch, bytesToReplaceLength, fullCaveBytes);
            CreatedCaves.Add((FullerAutoId, cavePointer, StandardCaveSizeBytes));
        }

        // Makes weapon charge (as in charged shots, or battery rifle speed ramp up) be set to max on the first tick.
        // Note: While this does give charge a value of 1 (the max), it does not cause plasma pistols fire charged shots for some reason.
        private void InjectInstantCharge()
        {
            var setChargeInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + SetChargeInstructionOffset);
            int bytesToReplaceLength = 0x1F;

            // Replaced bytes. We take more than the space necessary to avoid breaking a jump.
            //halo1.dll + B75641 - F3 0F11 84 1F 38020000 - movss[rdi + rbx + 00000238],xmm0
            //halo1.dll + B7564A - 76 0E - jna halo1.dll + B7565A
            //halo1.dll + B7564C - C7 84 1F 38020000 0000803F - mov[rdi + rbx + 00000238],3F800000
            //halo1.dll + B75657 - 0F28 C7 - movaps xmm0,xmm7
            //halo1.dll + B7565A - F3 41 0F10 4E 14 - movss xmm1,[r14+14]

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(setChargeInstruction_ch, bytesToReplaceLength);
            ReplacedBytes.Add((FullerAutoId, injectionAddress, originalBytes));
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            if (!WeaponClipAmmoPointer_ch.Offset(fullChargeValueOffset).Calculate(out long maxChargeValueAddress))
            {
                CcLog.Message("Could not read max value address!");
                return;
            }

            byte[] replacementBytes = new byte[]
            {
                0x50, // push rax
                0x48, 0xB8 }.AppendNum(maxChargeValueAddress).Append( // mov rax, <maxChargeValueAddress>
                0X0F, 0X28, 0X00, // MOVAPS xmm0, [rax]
                0x58); // pop rax

            // Note: we are not adding the original bytes on purpose.
            byte[] fullCaveBytes = replacementBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength, bytesToReplaceLength)).ToArray();

            long cavePointer = CodeCaveInjection(setChargeInstruction_ch, bytesToReplaceLength, fullCaveBytes);
            CreatedCaves.Add((FullerAutoId, cavePointer, StandardCaveSizeBytes));
            CcLog.Message("Instant charge injected----");
        }

        // Prevents the flag that sets if we are reloading to 0, so shotguns can rapid fire. Bent shotgun still needs to reload, but won't need to pump.
        public void InjectNeverReloading()
        {
            // Replaced (actually, not just moved to a cave) bytes:
            //halo1.dll + B7917D - 45 89 22 - mov[r10],r12d
            var setReloadingFlagInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + SetStartReloadingFlagOffset);
            int bytesToReplaceLength = 3;
            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(setReloadingFlagInstruction_ch, bytesToReplaceLength);
            ReplacedBytes.Add((FullerAutoId, injectionAddress, originalBytes));

            setReloadingFlagInstruction_ch.SetBytes(new byte[] { 0x90, 0x90, 0x90 });
        }

        // WARNING: Causes crash if injected while on a ghost and then firing.
        // Prevents the flag that sets if we are reloading to be set to 2 o 3, which are values used to indicate pump action.
        // It stills allow it to be set to 1 (which means reloading) or 0 (which means not reloading nor pumping).
        public void InjectNeverPump()
        {
            // Inject a bit further down from IsReloadingWriteOffset to avoid replacing relative instructions
            var nearSetReloadingFlagInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + NeverPumpInjectionOffset);
            int bytesToReplaceLength = 0x11;

            // Replaced bytes:
            //halo1.dll + B791A3 - 44 38 0D 07900C01 - cmp[halo1.dll + 1C421B1],r9b { (0) }
            //halo1.dll + B791AA - 41 BA 04020000 - mov r10d,00000204 { 516 }
            //halo1.dll + B791B0 - 45 8D 69 01 - lea r13d,[r9+01]

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(nearSetReloadingFlagInstruction_ch, bytesToReplaceLength);
            ReplacedBytes.Add((FullerAutoId, injectionAddress, originalBytes));

            byte[] prependedBytes = new byte[]
            {
                0x49, 0x83, 0xFc, 0x01,// cmp r12d, 01
                0x74 }.AppendRelativePointer("dontOverwriteReloadFlag", 0x7).Append(// jz 2
                0x41, 0xc7, 0x02, 0, 0, 0, 0 // mv [r10], 0
                ).LocalJumpLocation("dontOverwriteReloadFlag");

            // replacement for cmp [halo1.dll + 1c42b1], r9b using full addresses
            long fullMemAddressForCmp = halo1BaseAddress + 0x1c42b1;
            byte[] replacementBytes = new byte[]
            {
                0x50, // push rax
                0x48, 0xb8 }.AppendNum(fullMemAddressForCmp).Append( // mov rax, <fullMemAddressForCmp>,
                0x44, 0x38, 0x08, // cmp [rax], r9b,
                0x58); // pop rax
            byte[] modifiedOriginals = replacementBytes.Concat((originalBytes.Skip(7))).ToArray();
            byte[] fullCaveContents = prependedBytes.Concat(modifiedOriginals).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength, bytesToReplaceLength)).ToArray();

            long cavePointer = CodeCaveInjection(nearSetReloadingFlagInstruction_ch, bytesToReplaceLength, fullCaveContents);
            CreatedCaves.Add((FullerAutoId, cavePointer, StandardCaveSizeBytes));
        }

        // Injects code that writes the currently held weapon's clip ammo address.
        private void InjectWeaponClipAmmoDirectReader(long pointerAddress)
        {
            // Replaced bytes for clip reading.
            //halo1.dll + B76A30 - 42 0FB7 84 8F 8A020000 - movzx eax,word ptr[rdi + r9 * 4 + 0000028A]
            //halo1.dll + B76A39 - 66 89 44 4B 0E - mov[rbx + rcx * 2 + 0E],ax
            //halo1.dll + B76A3E - 0FB7 42 0A - movzx eax,word ptr[rdx + 0A]

            byte[] clipPointerReader = new byte[]
{
                0x50,                   // push rax
                0x49,0x8B,0xC1,             // mov rax,r9
                0x48,0x6B,0xC0,0x04,          // imul rax,rax,04
                0x48,0x01,0xF8,             // add rax,rdi
                0x48,0x05, 0x8A, 0x02, 0x00, 0x00,      // add rax,0000028A
                0x48,0xA3 }.AppendNum(pointerAddress).Append(// mov [clipPointer],rax
                0x58); // pop rax

            InjectWeaponClipAmmoPointerReader(ClipPointerReadOffset, 0x12, clipPointerReader, "direct");
        }

        // Injects code that gets the clip ammo location for plasma weapons, which do not use it so we need to infer it through the heat offset.
        private void InjectWeaponClipAmmoThroughHeatReadingReader(long pointerAddress)
        {
            // Replaced bytes for clip reading through heat reading instruction.
            //halo1.dll + B76990 - 8B 8F 04020000 - mov ecx,[rdi+00000204]
            //halo1.dll + B76996 - 48 8B F0              -mov rsi,rax
            //halo1.dll + B76999 - 89 0B - mov[rbx],ecx
            //halo1.dll + B7699B - 41 8B CB              -mov ecx,r11d

            byte[] clipPointerReaderThroughHeat = new byte[]
{
                0x50,                   // push rax
                0x48,0x8B,0xC7,             // mov rax,rdi
                0x48,0x05,0x04,0x02,0x00,0x00, // add rax, 0x204; the offset used in the original instruction1
                0x48,0x05,0x86,0x00,0x00,0x00, // add rax, 0x86; the offset from heat to clip ammo
                0x48,0xA3 }.AppendNum(pointerAddress).Append(// mov [clipPointer],rax
                0x58); // pop rax
            InjectWeaponClipAmmoPointerReader(HeatReadOffset, 0xE, clipPointerReaderThroughHeat, "(inferred from heat pointer)");
        }

        // Common code for both clip ammo pointer readers.
        private void InjectWeaponClipAmmoPointerReader(long offset, int bytesToReplaceLength, byte[] prependedInstructions, string message)
        {
            CcLog.Message($"Injecting weapon clip ammo pointer {message} reader");

            var clipReadingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + offset);
            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(clipReadingInstruction_ch, bytesToReplaceLength);
            ReplacedBytes.Add((ClipPointerId, injectionAddress, originalBytes));

            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            byte[] fullCaveContents = prependedInstructions
                .Concat(originalBytes)
                .Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength, bytesToReplaceLength))
                .ToArray();

            long cavePointer = CodeCaveInjection(clipReadingInstruction_ch, bytesToReplaceLength, fullCaveContents);
            CreatedCaves.Add((ClipPointerId, cavePointer, StandardCaveSizeBytes));
        }
    }
}