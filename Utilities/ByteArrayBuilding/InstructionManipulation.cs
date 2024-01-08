using System;
using System.Collections.Generic;
using System.Linq;

namespace CrowdControl.Games.Packs.MCCHaloCE.Utilities.ByteArrayBuilding;

public partial class MCCHaloCE
{
    /// <summary>
    /// Utility function to insert a byte array inside another byte array.
    /// </summary>
    /// <param name="bytes">Bytes where the new ones are inserted.</param>/param>
    /// <param name="bytesToInsert">Bytes to be inserted.</param>
    /// <param name="insertionPoint">Offset in <paramref name="bytes"/> where the insertion will be done.</param>
    /// <returns>The combined byte array.</returns>
    private IEnumerable<byte> SpliceBytes(IEnumerable<byte> bytes, IEnumerable<byte> bytesToInsert, int insertionPoint)
    {
        byte[] prependedBytes = bytes.Take(insertionPoint).ToArray();
        byte[] pospendedBytes = bytes.Skip(insertionPoint).ToArray();

        return prependedBytes.Concat(bytesToInsert).Concat(pospendedBytes);
    }

    /// <summary>
    /// Appends instructions at the end of <paramref name="bytes"/> to unconditionally jump to the given absolute address. Used
    /// mostly to go back to the injection point and resume execution.
    /// </summary>
    /// <returns>The byte array with the added jump.</returns>
    private byte[] AppendUnconditionalJump(byte[] bytes, long jumpDestionationAddress)
    {
        return bytes.Concat(GenerateJumpBytes(jumpDestionationAddress)).ToArray();
    }

    /// <summary>
    ///     Relative jump instructions are problematic on code caves. If their destination instruction is not also in the cave,
    ///     the relative jump must be replaced by an absolute jump. This function does that.
    /// </summary>
    /// <param name="bytes">Bytes containing the jump.</param>
    /// <param name="originalBytesStartingAddress">Absolute address of the original bytes, before any modification.</param>
    /// <param name="jccInstructionOriginalOffset">Offest of the jump instruction before any modification.</param>
    /// <param name="instructionLength">Length in bytes of the jump instruction.</param>
    /// <param name="opCodeLength">Length in bytes of the operatio ncode of the jump instrunction.</param>
    /// <param name="additionalOffsetAddedWhenModifyingBytes">
    ///     If the offset from the start is different to <paramref name="jccInstructionOriginalOffset"/> because
    ///     <see cref="bytes"/> was modified, this is that offset difference.
    /// </param>
    /// <returns>The byte array with the updated jump instruction.</returns>
    /// <exception cref="NotImplementedException">Thrown when the operation code has an unexpected length.</exception>
    private byte[] FixRelativeJccAfterRelocation(
        byte[] bytes,
        long originalBytesStartingAddress,
        int jccInstructionOriginalOffset,
        int instructionLength,
        int opCodeLength = 2,
        int additionalOffsetAddedWhenModifyingBytes = 0) // offsets added when modifying the bytes before the Jcc instruction
    {
        // This assumes that these "original bytes" already had the unconditional jump appended.
        byte[] jccInstructionBytes = bytes
            .Skip(jccInstructionOriginalOffset + additionalOffsetAddedWhenModifyingBytes)
            .Take(instructionLength).ToArray();

        byte[] jccOpCodeBytes = jccInstructionBytes.Take(opCodeLength).ToArray();
        var jumpAddressLength = instructionLength - opCodeLength;
        int jccRelativeJump = jumpAddressLength switch
        {
            1 => (int)jccInstructionBytes[opCodeLength],
            4 => BitConverter.ToInt32(jccInstructionBytes, opCodeLength),
            _ => throw new NotImplementedException("Relative jump fixing is not implemented for relative jump length " + jumpAddressLength)
        };

        // Calculate the address to the new position. Keep in mind this is calculated from the start of the NEXT instruction to the Jcc.
        long nextInstructionStart = originalBytesStartingAddress + jccInstructionOriginalOffset + instructionLength;
        int newRelativeJumpAddress = bytes.Length -
                                     (jccInstructionOriginalOffset + instructionLength + additionalOffsetAddedWhenModifyingBytes); // Appends at the end of the given bytes.
        long newAbsoluteJumpAddress = nextInstructionStart + jccRelativeJump;
        byte[] newAbsoluteJumpBytes = GenerateJumpBytes(newAbsoluteJumpAddress);

        byte[] fullOriginalBytesWithNewJump = bytes
            .Take(jccInstructionOriginalOffset + additionalOffsetAddedWhenModifyingBytes)
            .Concat(jccOpCodeBytes)
            .Concat(BitConverter.GetBytes(newRelativeJumpAddress).Take(jumpAddressLength))
            .Concat(bytes.Skip(jccInstructionOriginalOffset + instructionLength + additionalOffsetAddedWhenModifyingBytes))
            .Concat(newAbsoluteJumpBytes)
            .ToArray();

        return fullOriginalBytesWithNewJump;
    }

    /// <summary>
    /// Similarly to jump instructions, call instructions moved to a cave need to be changed to use absolute address.
    /// </summary>
    /// <param name="bytes">Byte array containing the call instruction.</param>
    /// <param name="callInstructionOffset">Offset of the call instruction.</param>
    /// <param name="callInstructionLength">Length of the call instruction.</param>
    /// <param name="bytesStartingAddress">Original absolute address of <paramref name="bytes"/>.</param>
    /// <returns>The byte array with the updated call instruction.</returns>
    /// <remarks>This function uses the R9 register. If it is used during the call, this function needs to be modified.</remarks>
    private (byte[] transformedBytes, int newAbsoluteCallLength)
        TransformRelativeCallToAbsoluteCall(byte[] bytes, int callInstructionOffset, int callInstructionLength, long bytesStartingAddress)
    {
        // using r9 as a register. Make sure it is not used during the call.
        byte[] callInstruction = bytes.Skip(callInstructionOffset).Take(callInstructionLength).ToArray();
        int relativeAddress = BitConverter.ToInt32(callInstruction, 1); // The call operand is always 1 byte
        long absoluteAddress = bytesStartingAddress + callInstructionOffset + callInstructionLength + relativeAddress;
        byte[] absoluteCall = new byte[]
            {
                0x41, 0x51, // push r9
                0x49, 0xb9 }.AppendNum(absoluteAddress) // mov r9, <absolute address>
            .Append(
                0x41, 0xff, 0xd1, // call r9,
                0x41, 0x59 // pop r9
            );

        byte[] transformedBytes = bytes.Take(callInstructionOffset).Concat(absoluteCall).Concat(bytes.Skip(callInstructionOffset + callInstructionLength)).ToArray();

        return (transformedBytes, absoluteCall.Length);
    }

    /// <summary>
    /// Generate the instructions for an unconditional jump to the given absolute address.
    /// </summary>
    /// <param name="jumpAddress">Absolute address to jump to.</param>
    /// <param name="replacedBytesLength">How many bytes in the original code are replaced, to verify there's enough space.</param>
    /// <returns>The generated instructions.</returns>
    /// <exception cref="Exception">Thrown if there's not enough space in the injection point for the jump instructions.</exception>
    private byte[] GenerateJumpBytes(long jumpAddress, int replacedBytesLength = 0)
    {
        // The minimum bytes required are 14 (aka 0xE)
        byte[] pushOpBytes = { 0x68 };
        byte[] movToStackPointerPlus04Bytes = { 0xC7, 0x44, 0x24, 0x04 };
        byte[] retOpBytes = { 0xC3 };
        byte[] caveAddressBytes = BitConverter.GetBytes(jumpAddress);
        byte[] caveAddressUpperBytes = caveAddressBytes.Skip(4).ToArray();
        byte[] caveAddressLowerBytes = caveAddressBytes.Take(4).ToArray();
        byte[] absoluteJumpBytes = pushOpBytes.Concat(caveAddressLowerBytes)
            .Concat(movToStackPointerPlus04Bytes).Concat(caveAddressUpperBytes)
            .Concat(retOpBytes).ToArray();

        if (replacedBytesLength != 0)
        {
            if (absoluteJumpBytes.Length > replacedBytesLength)
            {
                throw new Exception("Jump bytes are longer than specified replaced bytes length");
            }

            absoluteJumpBytes = PadWithNops(absoluteJumpBytes, replacedBytesLength - absoluteJumpBytes.Length);
        }

        return absoluteJumpBytes;
    }

    /// <summary>
    /// Appends NOP instructions to fill a given size. This is necessary if the injected code
    /// has a different lenght to the replaced instructions, which would break the next instructions.
    /// </summary>
    /// <returns>The bytes with the added padding.</returns>
    private byte[] PadWithNops(byte[] bytes, int paddingLengthInBytes)
    {
        if (paddingLengthInBytes <= 0)
        {
            return bytes;
        }

        byte[] nopBytes = { 0x90 };
        IEnumerable<byte> paddedBytes = bytes.AsEnumerable();
        for (int i = 0; i < paddingLengthInBytes; i++)
        {
            paddedBytes = paddedBytes.Concat(nopBytes);
        }

        return paddedBytes.ToArray();
    }
}