using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;

namespace Tiny7z.Common
{
	public static class StreamExtensions
    {
        /// <summary>
        /// Reads a bitmask only if the AllAreDefined byte is == 0
        /// </summary>
        public static ulong ReadOptionalBoolVector(this Stream stream, ulong length, out bool[] vector)
        {
            if (stream.ReadByteThrow() == 0)
            {
                return stream.ReadBoolVector(length, out vector);
            }
            else
            {
                vector = Enumerable.Repeat(true, (int)length).ToArray();
                return length;
            }
        }

        /// <summary>
        /// Writes a bitmask only if there's any unset element.
        /// </summary>
        public static ulong WriteOptionalBoolVector(this Stream stream, bool[] vector)
        {
            for (long i = 0; i < vector.LongLength; ++i)
            {
                if (!vector[i])
                {
                    stream.WriteByte(0);
                    return stream.WriteBoolVector(vector);
                }
            }
            stream.WriteByte(1);
            return 1;
        }

        /// <summary>
        /// Reads a bitmask from stream and converts to a boolean vector
        /// </summary>
        public static ulong ReadBoolVector(this Stream stream, ulong length, out bool[] vector)
        {
            vector = new bool[length];
			ulong numDefined = 0;
			byte mask = 0;
			byte b = 0;
            for (ulong i = 0; i < length; ++i)
            {
                if (mask == 0)
                {
                    b = stream.ReadByteThrow();
                    mask = 0x80;
                }
                vector[i] = (b & mask) > 0;
                numDefined += vector[i] ? (ulong)1 : 0;
                mask >>= 1;
            }
            return numDefined;
        }

        /// <summary>
        /// Writes a boolean vector in stream in bitmask form
        /// </summary>
        public static ulong WriteBoolVector(this Stream stream, bool[] vector)
        {
			byte mask = 0x80;
			byte b = 0;
			ulong length = 0;
            for (long i = 0; i < vector.LongLength; ++i)
            {
                if (vector[i])
                    b |= mask;
                mask >>= 1;
                if (mask == 0)
                {
                    stream.WriteByte(b);
                    ++length;
                    mask = 0x80;
                    b = 0;
                }
            }
            if (mask != 0x80)
            {
                stream.WriteByte(b);
                ++length;
            }
            return length;
        }
/*
        /// <summary>
        /// Calculates size of a boolean vector in bytes
        /// </summary>
        public static ulong BoolVectorSize(bool[] vector)
        {
            return ((ulong)vector.LongLength + 7) / 8;
        }*/

        /// <summary>
        /// Stream extension to get a struct out of a byte sequence
        /// </summary>
        public static T ReadStruct<T>(this Stream stream) where T : struct
        {
            int structSize = Marshal.SizeOf(typeof(T));
            byte[] buffer = new byte[structSize];
            if (stream.Read(buffer, 0, structSize) != structSize)
                throw new EndOfStreamException();

            return buffer.GetStruct<T>();
        }
/*
        /// <summary>
        /// Stream extension to write a struct as a byte sequence
        /// </summary>
        public static void WriteStruct<T>(this Stream stream, T structObj) where T : struct
        {
            stream.Write(structObj.GetByteArray(), 0, Marshal.SizeOf(typeof(T)));
        }*/

        /// <summary>
        /// Read one encoded 64-bits integer from stream
        /// </summary>
        public static ulong ReadDecodedUInt64(this Stream stream)
        {
			byte firstByte = (byte)stream.ReadByteThrow();
			byte mask = 0x80;
			ulong value = 0;

            for (int i = 0; i < 8; ++i)
            {
                if ((firstByte & mask) == 0)
                {
					ulong highPart = firstByte & (mask - 1u);
                    value += highPart << (8 * i);
                    return value;
                }

                value |= (ulong)stream.ReadByteThrow() << (8 * i);
                mask >>= 1;
            }

            return value;
        }

        /// <summary>
        /// Write one encoded 64-bits integer to stream
        /// </summary>
        public static int WriteEncodedUInt64(this Stream stream, ulong y)
        {
            var data = new List<byte>();

			byte mask = 0x80;
            data.Add(0xFF);
            for (int i = 0; i < 8; ++i)
            {
                if (y < mask)
                {
                    mask = (byte)((0xFF ^ mask) ^ (mask - 1u));
                    data[0] = (byte)(y | mask);
                    break;
                }
                data.Add((byte)(y & 0xFF));
                y >>= 8;
                mask >>= 1;
            }
            stream.Write(data.ToArray(), 0, data.Count);
            return data.Count;
        }
/*
        /// <summary>
        /// Calculates one encoded 64-bits integer's actual size in bytes
        /// </summary>
        public static int EncodedUInt64Size(ulong y)
        {
            int i;
            for (i = 1; i < 9; i++)
                if (y < (((ulong)1 << (i * 7))))
                    break;
            return i;
        }*/

        /// <summary>
        /// Extension to read a byte from a stream, but throw instead of returning -1 if end of stream is reached.
        /// </summary>
        public static byte ReadByteThrow(this Stream stream)
        {
            int y = stream.ReadByte();
            if (y == -1)
                throw new EndOfStreamException();
            return (byte)y;
        }

        /// <summary>
        /// Extension to read an array of bytes from a stream, but throw if end of stream is reached.
        /// </summary>
        public static byte[] ReadThrow(this Stream stream, ulong size)
        {
			byte[] buffer = new byte[size];
            if (stream.Read(buffer, 0, (int)size) < (int)size)
                throw new EndOfStreamException();
            return buffer;
        }
    }
}
