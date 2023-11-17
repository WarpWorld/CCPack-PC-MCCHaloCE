using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Games.Packs.MCCHaloCE.Utilites.ByteArrayBuilding;
using System;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        private const long ScriptInjectionOffset = 0xACC0E9;

        // Points to where the injected code store the variables we use to communicate with the H1 scripts.
        private AddressChain? scriptVarInstantEffectsPointerPointer_ch = null;

        // Note: This points to the first var. Any others will be referred using a multiple of 8 offset on the value pointed by this one.
        private AddressChain? scriptVarTimedEffectsPointerPointer_ch = null;

        // Continuous script variables use bits in a script variable to be activated. Hence there's a max after which we need to use another variable.
        private const int MaxContinousScriptEffectSlotPerVar = 30;

        private const string ScriptVarPointerId = "scriptVarPointerId";
        private const string ScriptVar2PointerId = "scriptVar2PointerId";

        /// <summary>
        /// Inserts code that writes pointers to the scripts variables, <see cref="scriptVarInstantEffectsPointerPointer_ch"/>
        /// and <see cref="scriptVarTimedEffectsPointerPointer_ch"/>, which allows the effect pack to communicate with the H1 scripts.
        /// </summary>
        private void InjectScriptHook()
        {
            try
            {
                UndoInjection(ScriptVarPointerId);
            }
            catch (Exception e)
            {
                CcLog.Error(e, "Undoing is causing a crash - scripthook.");
            }

            CcLog.Message("Injecting script communication hook.---------------------------");
            // Original replaced bytes. Total length: 16 (0x10)
            //0x48, 0x63, 0x42, 0x34, // movsxd  rax,dword ptr [rdx+34]
            //0x48, 0x03, 0xC2, //add rax,rdx
            //0x8B, 0x44, 0xC8, 0x04, // mov eax,[rax+rcx*8+04]
            //0x48, 0x83, 0xC4, 0x20, //add rsp, 20
            //0x5B, // pop rbx
            var scriptVarReadingInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + ScriptInjectionOffset);
            int bytesToReplaceLength = 0x10;

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(scriptVarReadingInstruction_ch, bytesToReplaceLength);
            ReplacedBytes.Add((ScriptVarPointerId, injectionAddress, originalBytes));

            IntPtr scriptVarPointerPointer = CreateCodeCave(ProcessName, 8);
            IntPtr scriptVar2PointerPointer = CreateCodeCave(ProcessName, 8);
            CreatedCaves.Add((ScriptVarPointerId, (long)scriptVarPointerPointer, 8));
            CreatedCaves.Add((ScriptVarPointerId, (long)scriptVar2PointerPointer, 8));

            CcLog.Message("Script var 1 pointer: " + ((long)scriptVarPointerPointer).ToString("X"));
            CcLog.Message("Script var 2 pointer: " + ((long)scriptVar2PointerPointer).ToString("X"));

            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));
            scriptVarInstantEffectsPointerPointer_ch = AddressChain.Absolute(Connector, (long)scriptVarPointerPointer);
            scriptVarTimedEffectsPointerPointer_ch = AddressChain.Absolute(Connector, (long)scriptVar2PointerPointer);

            // This script, for each of our script communication variables, hooks to where it is read.
            // The injected code checks if the one read is the script var with its original value, and
            // checks also that the nearby "anchor" variables defined in the script match it to avoid
            // false positives, then writes the pointer on a small code cave.
            byte[] variableGetter = new byte[]
            {
                0x52, // push rdx
                0x48, 0x8B, 0xD1, // mov rdx, rcx
                0x48, 0x6B, 0xD2, 0x08, //imul rdx, 0x8
                0x48, 0x01, 0xC2, // add rdx, rax
                0x48, 0x83, 0xC2, 0x04, // add rdx, 0x4
                0x81, 0x3A }.AppendNum(0x3456ABCD).Append( // cmp [rdx], 0x3456ABCD (878.095.309 decimal) ;compare to initial value of var. Changed from the previous implementation value 0x75BCD15
                0x75).AppendRelativePointer("checkIfScriptVar2", 0x2D) // jne 0x2D to the next var check
            .Append(
                0x48, 0x83, 0xC2, 0x08, // add rdx,08
                0x81, 0x3A, 0xB1, 0x68, 0xDE, 0x3A,//cmp [rdx],3ADE68B1 ;compare to value of right anchor, 987654321
                0x75).AppendRelativePointer("checkIfScriptVar2", 0x21) //jne (0X21), to the next var check
            .Append(
                0x48, 0x83, 0xEA, 0x10,//sub rdx,10
                0x81, 0x3A, 0xE7, 0xA4, 0x5D, 0x2E,//cmp [rdx],2E5DA4E7 // compare to value of left anchor 777888999)
                0x75).AppendRelativePointer("checkIfScriptVar2", 0x15)  //jne 0x15, to the next var check
            .Append(
                0x48, 0x83, 0xC2, 0x08, // add rdx, 08 <- reset offset to point again to the main variable instead of an anchor
                0x50, // push rax
                0x48, 0x8B, 0xC2, // mov rax, rdx
                0x48, 0xA3).AppendNum((long)scriptVarPointerPointer) // mov [VarPointerPointer], rax
            .Append(
                0x58, // pop rax
                0xEB).AppendRelativePointer("popPushedRegistersAndEnd", 0x33) //jmp pop rdx (31)
            .LocalJumpLocation("checkIfScriptVar2").Append(
                0x81, 0x3A, 0x00, 0x00, 0x00, 0x40, // cmp [rdx], 0x40 00 00 00 ;compare to initial value of var
                0x75).AppendRelativePointer("popPushedRegistersAndEnd", 0x2B) // jne 0x2B to "pop rdx" to avoid storing any variable that isn't our marker
            .Append(
                0x48, 0x83, 0xC2, 0x010, // add rdx,010 (8*2)
                0x81, 0x3A, 0x09, 0xA4, 0x5D, 0x2E,//cmp [rdx],2E5DA409 ; compare to value of right anchor, 777888777
                0x75).AppendRelativePointer("popPushedRegistersAndEnd", 0x1F) //jne (0X1F), to pop prdx
            .Append(
                0x48, 0x83, 0xEA, 0x18,//sub rdx,18
                0x81, 0x3A, 0xB1, 0xD0, 0x5E, 0x07,//cmp [rdx],75ED0B1 L compare to value of left anchor 123654321)
                0x75).AppendRelativePointer("popPushedRegistersAndEnd", 0x13)  //jne 0x13, to pop rdx
            .Append(
                0x48, 0x83, 0xC2, 0x08, // add rdx, 08 <- reset offset to point again to the main variable instead of an anchor
                0x50, // push rax
                0x48, 0x8B, 0xC2, // mov rax, rdx
                0x48, 0xA3).AppendNum((long)scriptVar2PointerPointer) // mov [VarPointerPointer], rax
            .Append(
                0x58 // pop rax
            ).LocalJumpLocation("popPushedRegistersAndEnd").Append(
                0x5A // pop rdx
            );

            byte[] originalWithVariableGetter = SpliceBytes(originalBytes, variableGetter, 0x7).ToArray(); // Inserts before mov eax,[rax+rcx*8+04]
            byte[] fullCaveCode = AppendUnconditionalJump(originalWithVariableGetter, injectionAddress + bytesToReplaceLength);

            long cavePointer = CodeCaveInjection(scriptVarReadingInstruction_ch, bytesToReplaceLength, fullCaveCode);
            CreatedCaves.Add((ScriptVarPointerId, cavePointer, StandardCaveSizeBytes));

            CcLog.Message("Script communication hook injection finished.----------------------");
        }
    }
}