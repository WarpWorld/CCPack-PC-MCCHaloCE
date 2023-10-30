using ConnectorLib;
using ConnectorLib.Inject.AddressChaining;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs
{
    /// <summary>
    /// Handles all keybind information and manipulation. Mouse clicks are still considered "key" binds, but not mouse movement.
    /// </summary>
    /// <remarks>Most functions just change the state in this class. You need to call UpdateKeyBindings to actually change the game bindings.</remarks>
    public class KeyManager
    {
        private const long FirstKeybindOffset = 0x2B05630;
        private const byte UnbindKeycode = 0xE8; // This is an unassigned virtual key code, according to the documentation.

        private static readonly HashSet<GameAction> MovementKeys = new HashSet<GameAction>
        { GameAction.RunForward, GameAction.RunBackwards, GameAction.StrafeLeft, GameAction.StrafeRight };

        // Does not include Pause
        private Dictionary<GameAction, KeybindData> SwappableKeybinds = new()
        {
            { GameAction.Jump, new KeybindData(0) },
            { GameAction.SwapGrenades, new KeybindData(1) },
            { GameAction.Use, new KeybindData(2) },
            { GameAction.Reload, new KeybindData(3) },
            { GameAction.SwapWeapons, new KeybindData(4) },
            { GameAction.Melee, new KeybindData(5) },
            { GameAction.FlashlightToggle, new KeybindData(6) },
            { GameAction.ThrowGrenade, new KeybindData(7) },
            { GameAction.Fire, new KeybindData(8) },
            { GameAction.Crouch, new KeybindData(9) },
            { GameAction.ZoomHold, new KeybindData(10) },
            { GameAction.RunForward, new KeybindData(16) },
            { GameAction.RunBackwards, new KeybindData(17) },
            { GameAction.StrafeLeft, new KeybindData(18) },
            { GameAction.StrafeRight, new KeybindData(19) },
        };

        public ConnectorLib.IPCConnector connector;
        public HIDConnector hidConnector;

        // If true, the game is forcing a pause to update controls. Useful to know prevent an endless loop where the effect gets delayed because it is mid pause.
        public bool InForcedPause = false;

        public KeyManager()
        {
        }

        public static void SendMouseMove(int dx, int dy)
        {
            MouseInput mouseInput = new MouseInput // as per https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput
            {
                dwFlags = (uint)MouseEventFlags.MOUSEEVENTF_MOVE,
                dx = dx,
                dy = dy,
            };

            Input[] inputs =
            {
                new Input
                {
                    type = (int) InputType.Mouse,
                    u = new InputUnion
                    {
                        mi = mouseInput,
                    }
                }
            };

            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        }

        public bool ForceMouseMove(int x, int y)
        {
            SendMouseMove(x, y);

            return true;
        }

        public bool InputEmulationReady()
        {
            return hidConnector != null && hidConnector.Connected;
        }

        public bool ForceActionPressOnce(GameAction action)
        {
            if (!TryGetActionKeybindData(action, out var keybindData))
            {
                CcLog.Message($"Could not force action {action}.");
                return false;
            }

            SendKeyCodeNoChecks(keybindData.currentBinding, false);
            WaitOneFrame();
            SendKeyCodeNoChecks(keybindData.currentBinding, true);
            return true;
        }

        public bool DisableAction(GameAction action)
        {
            if (!TryGetActionKeybindData(action, out var keybindData))
            {
                CcLog.Message($"Could not disable action {action}.");
                return false;
            }

            SendKeyCodeNoChecks(keybindData.currentBinding, true);
            keybindData.Swap(UnbindKeycode);

            return true;
        }

        // Swaps only one way.
        public bool SetAlernativeBindingToOTherActions(GameAction doubleBindedAction, GameAction actionToAttach)
        {
            if (!TryGetActionKeybindData(doubleBindedAction, out var keyDataToModify)
                || !TryGetActionKeybindData(actionToAttach, out var keyDatamerge))
            {
                CcLog.Message($"Could not attach action {actionToAttach}'s keybinding to " +
                    $"action {doubleBindedAction} as alt binding, one of them does not exist");
                return false;
            }

            CcLog.Message($"Binding to merge: {keyDatamerge.currentBinding}");
            HIDConnector.VirtualKeyCode virtualKeyCode = (HIDConnector.VirtualKeyCode)keyDatamerge.currentBinding;
            CcLog.Message($"Keycode to merge: {(byte)virtualKeyCode}");

            keyDataToModify.alternativeBinding = (byte)virtualKeyCode;

            return true;
        }

        public bool SwapActionWithArbitraryKeyCode(GameAction action, HIDConnector.VirtualKeyCode virtualKeyCode)
        {
            if (!TryGetActionKeybindData(action, out var keybindData))
            {
                CcLog.Message($"Could not swap action {action}'s keybinding to {virtualKeyCode}.");
                return false;
            }

            CcLog.Message($"Swapping action {action} to code {virtualKeyCode}.");
            return keybindData.TrySwap((byte)virtualKeyCode);
        }

        private bool TryGetActionKeybindData(GameAction action, out KeybindData actionKeybindData)
        {
            actionKeybindData = null;
            if (!AreKeyBindsInitialized())
            {
                CcLog.Message("Keybinds are not yet initialized, I don't know what to press.");

                return false;
            }

            if (!SwappableKeybinds.TryGetValue(action, out actionKeybindData))
            {
                CcLog.Message("Unknown action.");
                return false;
            }

            return true;
        }

        public void ForceShortPause(int pauseDuratinInMs = 350)
        {
            InForcedPause = true;
            SendPauseAction(false);
            WaitOneFrame();
            SendPauseAction(true);

            Thread.Sleep(pauseDuratinInMs);

            SendPauseAction(false);
            WaitOneFrame();
            SendPauseAction(true);
            InForcedPause = false;
        }

        public void WaitOneFrame()
        {
            Thread.Sleep(33);
        }

        private void SendKeyCodeNoChecks(int keyCode, bool isKeyUp)
        {
            HIDConnector.VirtualKeyCode virtualKeyCode = (HIDConnector.VirtualKeyCode)keyCode;

            if (isKeyUp)
            {
                hidConnector.KeyUp(virtualKeyCode);
                return;
            }

            hidConnector.KeyDown(virtualKeyCode);
        }

        private void SendPauseAction(bool isKeyUp)
        {
            if (isKeyUp)
            {
                hidConnector.KeyUp(HIDConnector.VirtualKeyCode.ESCAPE);
                return;
            }

            hidConnector.KeyDown(HIDConnector.VirtualKeyCode.ESCAPE);
        }

        public bool SendAction(GameAction action, bool isKeyUp)
        {
            if (!AreKeyBindsInitialized())
            {
                throw new Exception("Keybinds are not yet initialized, I don't know what to press.");
            }

            if (!SwappableKeybinds.TryGetValue(action, out KeybindData data))
            {
                throw new Exception("Unknown action.");
            }

            SendKeyCodeNoChecks(data.currentBinding, isKeyUp);

            return true;
        }

        public bool RestoreAllKeyBinds()
        {
            foreach (var value in SwappableKeybinds.Values)
            {
                value.Restore();
            }

            return true;
        }

        // TODO: Make this private and just have every binding check first if they need to do this.
        public void GetKeyBindingsFromGameMemory(long halo1BaseAddress)
        {
            CcLog.Message("Loading key values from game memory.");
            AddressChain basePointer = AddressChain.Absolute(this.connector, halo1BaseAddress + FirstKeybindOffset);
            foreach (var kvp in SwappableKeybinds)
            {
                byte keyBind = basePointer.Offset(kvp.Value.memoryOffsetFromJump).GetByte();
                kvp.Value.currentBinding = keyBind;

                //CcLog.Message($"Set {keyBind.ToString("X2")} for {kvp.Key}");
            }
        }

        public bool ResetAlternativeBindingForAction(GameAction action, long halo1BaseAddress)
        {
            AddressChain basePointer = AddressChain.Absolute(this.connector, halo1BaseAddress + FirstKeybindOffset);

            if (!basePointer.Offset(SwappableKeybinds[action].memoryOffsetFromJump + 0x4).TrySetByte(0x00))
            {
                CcLog.Message("Could not overwrite alternate binding");
                return false;
            }

            return true;
        }

        public bool UpdateGameMemoryKeyState(long halo1BaseAddress)
        {
            try
            {
                AddressChain basePointer = AddressChain.Absolute(this.connector, halo1BaseAddress + FirstKeybindOffset);
                int errors = 0;
                foreach (var kvp in SwappableKeybinds)
                {
                    if (!kvp.Value.IsInitialized())
                    {
                        continue;
                    }

                    //CcLog.Message($"Overwriting {kvp.Value.savedBinding.ToString("X2")} with {kvp.Value.currentBinding.ToString("X2")}");

                    if (!basePointer.Offset(kvp.Value.memoryOffsetFromJump).TrySetByte(kvp.Value.currentBinding))
                    {
                        errors++;
                    }

                    if (kvp.Value.alternativeBinding != 0x00)
                    {
                        //CcLog.Message($"Writing binding {kvp.Value.alternativeBinding} on offset {kvp.Value.memoryOffsetFromJump + 4} for action {kvp.Key}");
                        if (!basePointer.Offset(kvp.Value.memoryOffsetFromJump + 0x4).TrySetByte(kvp.Value.alternativeBinding))
                        {
                            errors++;
                        }
                    }
                }

                if (errors > 0)
                {
                    CcLog.Message($"Could not update the state of {errors} keybinds.");
                    return false;
                }
            }
            catch (Exception e)
            {
                CcLog.Error(e, "Failure while updating key state.");
            }

            return true;
        }

        public bool RandomizeNonRunningKeys(long halo1BaseAddress)
        {
            if (!EnsureKeybindsInitialized(halo1BaseAddress))
            {
                CcLog.Message("Could not randomize, keybinds are not initialzied.");
                return false;
            }

            return ShuffleControls(SwappableKeybinds.Keys.Except(MovementKeys).ToList());
        }

        public bool ReverseMovementKeys(long halo1BaseAddress)
        {
            try
            {
                if (!EnsureKeybindsInitialized(halo1BaseAddress))
                {
                    CcLog.Message("Could not reverse keys, keybinds are not initialzied.");
                    return false;
                }

                return
                    SwappableKeybinds[GameAction.RunForward].TrySwap(SwappableKeybinds[GameAction.RunBackwards])
                    && SwappableKeybinds[GameAction.StrafeLeft].TrySwap(SwappableKeybinds[GameAction.StrafeRight]);
            }
            catch (Exception e)
            {
                CcLog.Error(e, "Failure while swapping movement keys.");
                return false;
            }
        }

        public bool AreKeyBindsInitialized()
        {
            if (!SwappableKeybinds.First().Value.IsInitialized())
            {
                CcLog.Message("Keybinds are not yet initialized.");
                return false;
            };

            return true;
        }

        public bool EnsureKeybindsInitialized(long halo1BaseAddress)
        {
            if (hidConnector == null)
            {
                CcLog.Message("HIDConnector was null.");
                return false;
            }
            if (!AreKeyBindsInitialized())
            {
                GetKeyBindingsFromGameMemory(halo1BaseAddress);
            }

            return AreKeyBindsInitialized();
        }

        // Swaps a control with a random one, not repeating.
        private bool ShuffleControls(List<GameAction> actions)
        {
            Random rng = new Random();

            GameAction firstAction = actions[rng.Next(actions.Count)];
            byte firstActionKeyCode = SwappableKeybinds[firstAction].currentBinding;
            actions.Remove(firstAction);

            GameAction lastPickedAction = firstAction;
            while (actions.Count > 0)
            {
                GameAction pickedAction = actions[rng.Next(actions.Count)];
                SwappableKeybinds[lastPickedAction].TrySwap(SwappableKeybinds[pickedAction].currentBinding);
                lastPickedAction = pickedAction;
                actions.Remove(lastPickedAction);
            }

            // Complete the loop.
            SwappableKeybinds[lastPickedAction].TrySwap(firstActionKeyCode);

            return true;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);
    }
}