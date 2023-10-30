using System;
using System.Runtime.InteropServices;

namespace CrowdControl.Games.Packs
{
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-input
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public struct Input
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeyboardInput ki;
        [FieldOffset(0)] public readonly HardwareInput hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public readonly uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput
    {
        public readonly uint uMsg;
        public readonly ushort wParamL;
        public readonly ushort wParamH;
    }

    [Flags]
    public enum InputType
    {
        Mouse = 0,
        Keyboard = 1,
        Hardware = 2
    }
}