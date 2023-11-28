using ConnectorLib.Inject.AddressChaining;
using System;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {     
        /// <summary>
        /// Tries to get an array of bytes from an indirect pointer.
        /// </summary>
        /// <param name="valuePointerPointer_ch"> The indirect pointer.</param>
        /// <param name="offset">Offset to apply to the direct pointer.</param>
        /// <param name="byteAmount">How many bytes to retrieve.</param>
        /// <param name="byteArray">Output variable.</param>
        /// <returns>True if everything went right, false otherwise.</returns>
        private bool TryGetIndirectByteArray(AddressChain valuePointerPointer_ch, int offset, int byteAmount, out byte[]? byteArray)
        {
            if (!VerifyIndirectPointer(valuePointerPointer_ch, out AddressChain? valueRealPointer_ch))
            {
                byteArray = null;
                return false;
            }

            valueRealPointer_ch = valueRealPointer_ch.Offset(offset);
            return valueRealPointer_ch.TryGetBytes(byteAmount, out byteArray);
        }

        /// <summary>
        /// Byte array wrapper of <see cref="TrySetIndirectValue"/>.
        /// </summary>
        private bool TrySetIndirectByteArray(byte[] newValue, AddressChain valuePointerPointer_ch, int offset)
        {
            Func<AddressChain, (bool, byte[])> tryGetter
                = (pointerPointer_ch) => (pointerPointer_ch.TryGetBytes(newValue.Length, out byte[] oldValue), oldValue);
            Func<AddressChain, byte[], bool> trySetter = (pointerPointer_ch, newValue) => pointerPointer_ch.TrySetBytes(newValue);
            Func<byte[], byte[], byte[]> adder = (a, b) => throw new NotImplementedException("Byte array addition is not supported.");

            return TrySetIndirectValue<byte[]>(tryGetter, trySetter, adder, newValue, valuePointerPointer_ch, offset, false);
        }

        /// <summary>
        /// Float wrapper of <see cref="TrySetIndirectValue"/>.
        /// </summary>
        private bool TrySetIndirectFloat(float newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            Func<AddressChain, (bool, float)> tryGetter = (pointerPointer_ch) => (pointerPointer_ch.TryGetFloat(out float oldValue), oldValue); ;
            Func<AddressChain, float, bool> trySetter = (pointerPointer_ch, newValue) => pointerPointer_ch.TrySetFloat(newValue);
            Func<float, float, float> adder = (a, b) => a + b;

            return TrySetIndirectValue<float>(tryGetter, trySetter, adder, newValue, valuePointerPointer_ch, offset, isRelative);
        }

        /// <summary>
        /// Int32 wrapper of <see cref="TrySetIndirectValue"/>.
        /// </summary>
        private bool TrySetIndirectInt32(int newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            Func<AddressChain, (bool, int)> tryGetter = (pointerPointer_ch) => (pointerPointer_ch.TryGetInt(out int oldValue), oldValue);
            Func<AddressChain, int, bool> trySetter = (pointerPointer_ch, newValue) => pointerPointer_ch.TrySetInt(newValue);
            Func<int, int, int> adder = (a, b) => a + b;

            return TrySetIndirectValue<int>(tryGetter, trySetter, adder, newValue, valuePointerPointer_ch, offset, isRelative);
        }

        /// <summary>
        /// Short wrapper of <see cref="TrySetIndirectValue"/>.
        /// </summary>
        private bool TrySetIndirectShort(short newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            Func<AddressChain, (bool, short)> tryGetter = (pointerPointer_ch) => (pointerPointer_ch.TryGetShort(out short oldValue), oldValue);
            Func<AddressChain, short, bool> trySetter = (pointerPointer_ch, newValue) => pointerPointer_ch.TrySetShort(newValue);
            Func<short, short, short> adder = (a, b) => (short)(a + b);

            return TrySetIndirectValue<short>(tryGetter, trySetter, adder, newValue, valuePointerPointer_ch, offset, isRelative);
        }

        /// <summary>
        /// Given a pointer to an absolute memory address, try to set the value pointed by that memory address.
        /// </summary>
        /// <typeparam name="T">Type of value.</typeparam>
        /// <param name="tryGetter">Function to get the value from the pointer.</param>
        /// <param name="trySetter">Function to set the value from the pointer.</param>
        /// <param name="adder">Function to add the new value with the old one if needed.</param>
        /// <param name="newValue">Value to set.</param>
        /// <param name="valuePointerPointer_ch">Pointer to the pointer to the value.</param>
        /// <param name="offset">Offset to add to the address referred by <paramref name="valuePointerPointer_ch"/>.</param>
        /// <param name="isRelative">If true, the new value will be the old value + the new one.</param>
        /// <returns>True if successful, false otherwise.</returns>
        private bool TrySetIndirectValue<T>(Func<AddressChain, (bool, T)> tryGetter,
            Func<AddressChain, T, bool> trySetter,
            Func<T, T, T> adder,
            T newValue, AddressChain valuePointerPointer_ch, int offset, bool isRelative)
        {
            if (VerifyIndirectPointer(valuePointerPointer_ch, out AddressChain? valueRealPointer_ch))
            {
                valueRealPointer_ch = valueRealPointer_ch!.Offset(offset);

                (bool success, T oldValue) = tryGetter(valueRealPointer_ch);
                if (!success)
                {
                    if (!valueRealPointer_ch.Calculate(out long address))
                    {
                        CcLog.Error("Real pointer from indirect pointer can't be calculated.");
                    }

                    CcLog.Error($"Could not read value in address {address}, memory may have been destroyed.");
                    return false;
                }

                //CcLog.Message("Value read: " + oldValue);

                if (isRelative)
                {
                    newValue = adder(newValue, oldValue);
                }

                if (!trySetter(valueRealPointer_ch, newValue))
                {
                    return TroubleShootDirectPointerWrite(valueRealPointer_ch, newValue);
                }
            }

            return true;
        }

        /// <summary>
        /// Check and report why a direct pointer could not set a value.
        /// </summary>
        /// <typeparam name="T">Type of value that the pointer tried to set.</typeparam>
        /// <param name="directPointer">Pointer.</param>
        /// <param name="writtenValue">Value that was attempted to set, to help debugging.</param>
        /// <returns>True if the pointer itself is valid.</returns>
        private bool TroubleShootDirectPointerWrite<T>(AddressChain directPointer, T writtenValue)
        {
            CcLog.Error($"Value could not be set to value {writtenValue}");

            bool success = directPointer.Calculate(out long address);
            if (!success)
            {
                CcLog.Error($"Real pointer can't even be reached");
            }

            return false;
        }

        /// <summary>
        /// Verifies that an indirect pointer is valid, and calculates the direct pointer from it.
        /// </summary>
        /// <param name="valuePointerPointer_ch">Indirect pointer.</param>
        /// <param name="valueRealPointer_ch">Output variable for the direct pointer.</param>
        /// <returns>True if all is correct.</returns>
        private bool VerifyIndirectPointer(AddressChain valuePointerPointer_ch, out AddressChain? valueRealPointer_ch)
        {
            valueRealPointer_ch = null;
            if (valuePointerPointer_ch == null)
            {
                CcLog.Error("Indirect pointer for this value is still not set.");
                return false;
            }
            if (!TryGetRealValuePointer(valuePointerPointer_ch, out valueRealPointer_ch, 0))
            {
                CcLog.Error("Could not get pointer to value.");
                return false;
            }
            if (valueRealPointer_ch == null)
            {
                CcLog.Error("Direct pointer to value was null");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get a direct pointer from an indirect one.
        /// </summary>
        /// <param name="valuePointerPointer_ch">Indirect pointer.</param>
        /// <param name="valuePointer_ch">(output) Direct pointer.</param>
        /// <param name="offset">Offset to apply to the direct pointer.</param>
        /// <returns>True if everything is correct.</returns>
        private bool TryGetRealValuePointer(AddressChain valuePointerPointer_ch, out AddressChain? valuePointer_ch, int offset)
        {
            valuePointer_ch = null;
            if (!valuePointerPointer_ch.TryGetBytes(8, out byte[] valueAddressBytes))
            {
                CcLog.Error("Indirect pointer had no proper value.");

                return false;
            }

            long address = BitConverter.ToInt64(valueAddressBytes);

            //CcLog.Message("Value address: " + address.ToString("X"));
            valuePointer_ch = AddressChain.Absolute(Connector, address).Offset(offset);

            return true;
        }

        /// <summary>
        /// Sets the script variable for one-shot effects to a value that indicates the script to run a specific effect.
        /// </summary>
        /// <param name="scriptIndex">A value from 1 to 32767 representing the effect to run.</param>
        /// <param name="durationInMilliseconds">A value indicating how many millisecondsshould the action last.</param>
        public bool SetScriptOneShotEffectH1Variable(short scriptIndex, int durationInMilliseconds)
        {
            int durationInFrames = durationInMilliseconds / 33; // 30 frames per second -> 33 ms per frame.
            int combinedCode = (durationInFrames << 16) + scriptIndex;
            CcLog.Message($"Duration: {durationInFrames} frames, {durationInMilliseconds} ms Code: {scriptIndex} Combined: 0x{combinedCode.ToString("X")} ({combinedCode})");
            return TrySetIndirectInt32(combinedCode, scriptVarInstantEffectsPointerPointer_ch, 0, false);
        }
    }
}