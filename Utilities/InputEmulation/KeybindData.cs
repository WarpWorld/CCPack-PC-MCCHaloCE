using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs
{
    // Stores data relative to a Keybind.
    public class KeybindData
    {
        // This uses Virutal-Key Codes https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        private const int byteOffsetBetweenKeyEntries = 0x18;

        public readonly int memoryOffsetFromJump; // How far away in memory it is from the first keybind. A multiple of the offset between entries, always.
        public byte savedBinding = 0x0; // Stores the previous binding in case of a swap.
        public byte currentBinding = 0x0; // What key is currently binded.
        public byte alternativeBinding = 0x0; // Usually unused, except when I need to set multiple things to the same action, like on Berserker.

        public KeybindData(int numberOfKeyEntryOffsets)
        {
            this.memoryOffsetFromJump = numberOfKeyEntryOffsets * byteOffsetBetweenKeyEntries;
        }

        public bool IsInitialized()
        {
            return currentBinding != 0x0;
        }

        public bool IsCurrentlySwapped()
        {
            return savedBinding != 0x0;
        }

        public bool TrySwap(KeybindData swapPartner)
        {
            if (this.IsCurrentlySwapped() || swapPartner.IsCurrentlySwapped())
            {
                CcLog.Message("Attempted to swap an already swapped key");
                return false;
            }

            byte currentPartnerBinding = swapPartner.currentBinding;
            swapPartner.Swap(this.currentBinding);
            this.Swap(currentPartnerBinding);

            return true;
        }

        public bool TrySwap(byte newBinding)
        {
            if (this.IsCurrentlySwapped())
            {
                CcLog.Message("Attempted to swap an already swapped key");
                return false;
            }

            this.Swap(newBinding);

            return true;
        }

        public void Swap(byte newBinding)
        {
            this.savedBinding = this.currentBinding;
            this.currentBinding = newBinding;
        }

        public void Restore()
        {
            if (IsCurrentlySwapped())
            {
                this.currentBinding = this.savedBinding;
                this.savedBinding = 0x00;
            }

            this.alternativeBinding = 0x00;
        }
    }
}