using System;
using System.Runtime.InteropServices;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, int bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern int CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddres, int dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        private static extern int WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer, int size, int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, int dwFreeType);

        [DllImport("User32.dll")]
        public static extern int SetForegroundWindow(IntPtr point);
    }
}