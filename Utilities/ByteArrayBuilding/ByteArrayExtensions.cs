using System;
using System.Linq;

namespace CrowdControl.Games.Packs
{
    // Utility methods to make the byte arrays created to inject easier to read.
    public static class ByteArrayExtensions
    {
        // Convert to bytes and append an Int16
        public static byte[] AppendNum(this byte[] bytes, short number)
        {
            return bytes.Concat(BitConverter.GetBytes(number)).ToArray();
        }

        // Convert to bytes and append an Int32
        public static byte[] AppendNum(this byte[] bytes, int number)
        {
            return bytes.Concat(BitConverter.GetBytes(number)).ToArray();
        }

        // Convert to bytes and append an Int64
        public static byte[] AppendNum(this byte[] bytes, long number)
        {
            return bytes.Concat(BitConverter.GetBytes(number)).ToArray();
        }

        // Appends bytes, which can be specified as an array or as a series of parameters.
        public static byte[] Append(this byte[] bytes, params byte[] newBytes)
        {
            return bytes.Concat(newBytes).ToArray();
        }

        // Syntactic sugar. Does nothing, but helps identify relative addresses that may need updating.
        public static byte[] AppendRelativePointer(this byte[] bytes, string pointedSectionId, params byte[] newBytes)
        {
            return bytes.Append(newBytes);
        }

        // Syntactic sugar. Does nothing, but helps to identify jumping points.
        public static byte[] LocalJumpLocation(this byte[] bytes, string sectionId)
        {
            return bytes;
        }
    }
}