using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ConnectorLib.Exceptions;
using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Common;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE.Utilities;

public partial class MCCHaloCE
{
    // Store references to changed memory so it can be undone.
    private List<(string Identifier, long Address, byte[] originalBytes)> ReplacedBytes = new();

    private List<(string Identifier, long Address, int caveSize)> CreatedCaves = new();

    // Default size of created code caves.
    private const int StandardCaveSizeBytes = 1024;

    /// <summary>
    /// Restores the original code and clears the created caves for the task identified by the <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier"></param>
    private void UndoInjection(string identifier)
    {
        int replacedByteRestoreErrors = 0;
        int replacedByteRestoreErrorsUnfinishedInit = 0;

        foreach ((_, long injectionAddress, byte[] originalBytes) in ReplacedBytes.Where(x => x.Identifier == identifier))
        {
            try
            {
                CcLog.Debug("Undoing injection");
                AddressChain.Absolute(Connector, injectionAddress).SetBytes(originalBytes);
            }
            catch (InitNotCompleteException ex)
            {
                replacedByteRestoreErrorsUnfinishedInit++;
                replacedByteRestoreErrors++;
            }
            catch
            {
                replacedByteRestoreErrors++;
            }
        }

        CcLog.Message($"{replacedByteRestoreErrors} sets of injected bytes were not replaced with the originals." +
                      $" Of those, {replacedByteRestoreErrorsUnfinishedInit} failed likely because AddressChain can't be used during deinit.");

        ReplacedBytes = ReplacedBytes.Where(x => x.Identifier != identifier).ToList();

        int caveDeletionErrors = 0;
        foreach ((_, long caveAddress, int size) in CreatedCaves.Where(x => x.Identifier == identifier))
        {
            try
            {
                CcLog.Debug("Removing cave");
                AddressChain.Absolute(Connector, caveAddress).SetBytes(Enumerable.Repeat((byte)0x00, size).ToArray());
                FreeCave(Packs.MCCHaloCE.MCCHaloCE.ProcessName, new IntPtr(caveAddress), size);
            }
            catch
            {
                caveDeletionErrors++;
            }
        }

        if (caveDeletionErrors != 0)
        {
            CcLog.Message($"{caveDeletionErrors} caves could not be cleared. Most likely the memory was freed.");
        }

        CreatedCaves = CreatedCaves.Where(x => x.Identifier != identifier).ToList();

        CcLog.Debug("Undo complete");
    }

    /// <summary>
    /// Given a pointer and a byte length, take the byte array of that length from the pointer, and return it along with the
    /// absolute address of the pointer.
    /// </summary>
    /// <param name="injectionPoint_ch">The pointer.</param>
    /// <param name="bytesToReplaceLength">The byte length.</param>
    /// <returns>A tuple with the absolute address of the pointer and the retrieved original bytes.</returns>
    /// <exception cref="Exception"></exception>
    private (long address, byte[] originalBytes) GetOriginalBytes(AddressChain injectionPoint_ch, int bytesToReplaceLength)
    {
        if (!injectionPoint_ch.Calculate(out long injectionPointAddress))
        {
            throw new Exception("Injection point could not be calculated.");
        }

        byte[] originalBytes = injectionPoint_ch.GetBytes(bytesToReplaceLength);

        return (injectionPointAddress, originalBytes);
    }

    /// <summary>
    /// Given a pointer and a byte array, create a code cave, replace the bytes at the pointer with a jump to the cave and
    /// insert the byte array in the cave followed by a jump back to the code righ after the injected jump.
    /// </summary>
    /// <param name="injectionPoint_ch">The pointer.</param>
    /// <param name="bytesToReplaceLength">How many bytes to replace, to know if NOP padding is needed.</param>
    /// <param name="caveContents">Contents to insert in the code cave.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private long CodeCaveInjection(AddressChain injectionPoint_ch, int bytesToReplaceLength, byte[] caveContents)
    {
        if (caveContents.Length > StandardCaveSizeBytes)
        {
            throw new Exception("Cave bytes are longer than standard allocation.");
        }

        IntPtr cavePointer = CreateCodeCave(Packs.MCCHaloCE.MCCHaloCE.ProcessName, StandardCaveSizeBytes);

        CcLog.Message("Cave location: " + ((long)cavePointer).ToString("X"));

        AddressChain codeCave_ch = AddressChain.Absolute(Connector, (long)cavePointer);

        codeCave_ch.SetBytes(caveContents);
        byte[] replacementBytes = GenerateJumpBytes((long)cavePointer, bytesToReplaceLength);

        if (!DONT_OVERWRITE)
        {
            injectionPoint_ch.SetBytes(replacementBytes);
        }

        return (long)cavePointer;
    }

    // Reserves a new space in memory in process of size cavesize, and returns a pointer to it.
    private IntPtr CreateCodeCave(string process, int cavesize)
    {
        // Near address does not seem to work, but I don't really need it I think.
        var proc = Process.GetProcessesByName(process)[0];

        if (proc == null)
        {
            throw new AccessViolationException("Process \"" + process + "\" not found.");
        }

        // https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights
        // PROCESS_VM_OPERATION, PROCESS_VM_WRITE
        var hndProc = Packs.MCCHaloCE.MCCHaloCE.OpenProcess(0x0008 | 0x0020, 1, proc.Id);

        // https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualallocex
        // Allocation type: MEM_COMMIT | MEM_RESERVE.
        // Protection is PAGE_EXECUTE_READWRITE
        IntPtr caveAddress;
        try
        {
            caveAddress = Packs.MCCHaloCE.MCCHaloCE.VirtualAllocEx(hndProc, (IntPtr)null, cavesize, 0x1000 | 0x2000, 0x40);
        }
        catch (Exception ex)
        {
            CcLog.Error("Something went wrong with the cave creation");
            return (IntPtr)null;
        }
        finally
        {
            Packs.MCCHaloCE.MCCHaloCE.CloseHandle(hndProc);
        }

        return caveAddress;
    }

    // Writes data to a cave.
    private int WriteToCave(string process, IntPtr caveAddress, byte[] code)
    {
        var proc = Process.GetProcessesByName(process)[0];

        var hndProc = Packs.MCCHaloCE.MCCHaloCE.OpenProcess(0x0008 | 0x0020, 1, proc.Id);

        return Packs.MCCHaloCE.MCCHaloCE.WriteProcessMemory(hndProc, caveAddress, code, code.Length, 0);
    }

    // Frees the memory used by a cave.
    private int FreeCave(string process, IntPtr caveAddress, int sizeInBytes)
    {
        var proc = Process.GetProcessesByName(process)[0];

        var hndProc = Packs.MCCHaloCE.MCCHaloCE.OpenProcess(0x0008, 1, proc.Id);

        var rel = Packs.MCCHaloCE.MCCHaloCE.VirtualFreeEx(hndProc, caveAddress, sizeInBytes, 0x00008000); // MEM_RELEASE

        if (rel) { return 1; } else { return 0; } // return 1 if succeeds, 0 if fails.
    }
}