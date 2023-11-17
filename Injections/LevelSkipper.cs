using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Games.Packs.MCCHaloCE.Utilites.ByteArrayBuilding;
using System;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE;

public partial class MCCHaloCE
{
    private const string LevelSkipperId = "levelSkipperId";
    private const long NextLevelReadInstructionOffset = 0xAFA80E;

    // Contains a long with the offset of the next map when using the level skipper. The offset for the first level is 1.
    private AddressChain? nextMapOffset_ch = null;

    // Seeds the injected code to replace the next map with the given offset. Offset 1 is Pillar of Autumn, offset 0 is "no map change".
    private bool SetNextMap(int offset)
    {
        if (nextMapOffset_ch == null)
        {
            CcLog.Message("Map offset pointer is null"); return false;
        }
        if (!nextMapOffset_ch.TryGetLong(out _))
        {
            CcLog.Message("Map offset pointer can't be accessed."); return false;
        }

        nextMapOffset_ch.SetLong(2);

        return true;
    }

    // Injects code that, when finishing the current level (or using game_won), replaces the next level by the one specified by SetNextMap, if any.
    // Despite it replacing the level, the loading screen will show the original level, and the following level will be the expected one.
    // E.g. If this is injected, and in level 3 SetNextMap(2) is called, the next two maps would be 2 (with 4's loading screen) and 5.
    private void InjectLevelSkipper()
    {
        UndoInjection(LevelSkipperId);
        CcLog.Message("Injecting level skipper.");

        AddressChain nextLevelReadInstruction_ch = AddressChain.Absolute(Connector, halo1BaseAddress + NextLevelReadInstructionOffset);
        int bytesToReplaceLength = 0x10;

        // Original bytes. Total length: 0x10
        //halo1.dll + AFA80E - 41 0F10 03 - movups xmm0,[r11]
        //halo1.dll + AFA812 - 0F11 07 - movups[rdi],xmm0
        //halo1.dll + AFA815 - 41 0F10 4B 10 - movups xmm1,[r11+10]
        //halo1.dll + AFA81A - 0F11 4F 10 - movups[rdi + 10],xmm1

        (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(nextLevelReadInstruction_ch, bytesToReplaceLength);
        ReplacedBytes.Add((LevelSkipperId, injectionAddress, originalBytes));

        CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

        IntPtr mapListPointersCave = CreateCodeCave(ProcessName, 16);
        // First 8 bytes: address written by the injected code to locate the map list
        // Last 8 bytes: address of the next level to load, initially by the CC and updated by the injected code.
        CreatedCaves.Add((LevelSkipperId, (long)mapListPointersCave, 16));
        CcLog.Message("Map list pointers: " + ((long)mapListPointersCave).ToString("X"));

        byte[] levelJumperAsm = new byte[]
            {
                // Read the list pointer. If it is 0, write it.
                0x50, //push rax
                0x53, // push rbx,
                0x51, // push rcx,
                0x52, // push rdx,
                0x57, // push rdi
                0x48, 0xBB }.AppendNum((long)mapListPointersCave).Append( // mov rbx, <cave pointer>
                0x48, 0x31, 0xFF, // xor rdi, rdi; set rdi to 0
                0x48, 0x8B, 0x03, // mov rax,[rbx]
                0x48, 0x83, 0xf8, 0x00, // cmp rax,00 { 0 }
                0x75).AppendRelativePointer("skipWritingListPointer", 0x35).Append( // jne skipWritingListPointer
                0x49, 0x8b, 0xcb, // mov rcx, r11
                0x48, 0x83, 0xc1, 0x1c, // add rcx, 1c, ; 1C is the offset of PILL
                0x48, 0xBA, 0x50, 0x00, 0x69, 0x00, 0x6C, 0x00, 0x6C, 0x00) // mov rdx, "PILL"; The last 8 bytes spell PILL in Unicode
            .LocalJumpLocation("loopToFindListStart").Append(
                0x48, 0x81, 0xFF, 0x14, 0x00, 0x00, 0x00, // cmp rdi, 0x1E; (20 in decimal)
                0x7d).AppendRelativePointer("dontChangeLevel", 0x41).Append( // JNL; jump if not less than. Means we iterated further than the start of the list
                0x48, 0x8B, 0x01, // mov rax, [rcx]
                0x48, 0x39, 0xd0, // cmp rax, rdx
                0x74).AppendRelativePointer("writeListPointerToCave", 0xC).Append( // je
                0x48, 0x81, 0xE9, 0x64, 0x02, 0x00, 0x00, // sub rcx, 0x264,
                0x48, 0xFF, 0xC7, // inc rdi
                0xEB).AppendRelativePointer("loopToFindListStart", 0xE3) // jmp

            .LocalJumpLocation("writeListPointerToCave").Append(
                0x48, 0x83, 0xE9, 0x1C, // sub rcx, 0x1C; 1C is the offset of PILL, decreased to point to the start of the entry
                0x48, 0x89, 0x0B) // mov [rbx], rcx; save the pointer

            .LocalJumpLocation("skipWritingListPointer").Append(
                // Read the offset of the next level. The first level is offset 1. If the offset is not set, do nothing.
                // If it set, change R11 by the corresponding list entry
                0x48, 0x8B, 0x0B, // mov rcx, [rbx] ; load the pointer to the  first entry list
                0x48, 0x83, 0xC3, 0x08, // add rbx, 0x08; set the cave pointer to point to the level offset value
                0x48, 0x8b, 0x03, // mov rax, [rbx]; load the level offset
                0x48, 0x83, 0xF8, 0x00, // cmp rax, 00
                0x74).AppendRelativePointer("dontChangeLevel", 0x16).Append( // je
                // calculate the pointer to the level list entry
                0x48, 0xFF, 0xC8, // dec rax; (Pillar of autumn is the first entry (0 real offset), but we use offset 1 for it to know it is set)
                0x48, 0x69, 0xC0, 0x64, 0x02, 0x00, 0x00, // imul rax, rax, 0x264; multiply the offset by the distance between entries
                0x48, 0x01, 0xC8, // add rax, rcx; combine with first entry's pointer
                0x4C, 0x8B, 0xD8, // mov r11, rax; r11 contains the pointer to the entry of the next level, so we hijack it
                //0xff, 0x03) // inc [rbx]; increase the level offset, since we are moving up one level <- the function runs more than once and crashes, so this won't work
                0xC7, 0x03, 0x0, 0x0, 0x0, 0x0) // mov [rbx], 0; this way it won't keep trying to load levels
            .LocalJumpLocation("dontChangeLevel").Append(
                0x5F, // pop rdi
                0x5A, // pop rdx,
                0x59, // pop rcx,
                0x5B, // pop rbx
                0x58); // pop rax

        byte[] fullCaveContents = levelJumperAsm.Concat(originalBytes)
            .Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength, bytesToReplaceLength)).ToArray();

        long cavePointer = CodeCaveInjection(nextLevelReadInstruction_ch, bytesToReplaceLength, fullCaveContents);
        CreatedCaves.Add((LevelSkipperId, cavePointer, StandardCaveSizeBytes));

        nextMapOffset_ch = AddressChain.Absolute(Connector, (long)mapListPointersCave).Offset(8);
        CcLog.Message("Level skipper injection finished");
    }
}