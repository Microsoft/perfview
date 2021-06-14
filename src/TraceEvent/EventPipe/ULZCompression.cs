using System;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Tracing
{
    internal static class ULZCompression
    {
        private const int MinMatch = 4;

        private const int MaxExcess = 16;

        public static unsafe ArraySegment<byte> Decompress(ArraySegment<byte> input, int decompressedSize)
        {
            byte[] output = new byte[decompressedSize + MaxExcess];
            fixed (byte* inputPtr = &input.Array[input.Offset])
            {
                fixed (byte* outputPtr = &output[0])
                {
                    int actualDecompressedSize = Decompress(inputPtr, input.Count, outputPtr, output.Length);
                    if (decompressedSize != actualDecompressedSize)
                    {
                        throw new Exception($"Unexpected decompressed size. Expected: {decompressedSize}, Actual: {actualDecompressedSize}");
                    }

                    return new ArraySegment<byte>(output, 0, decompressedSize);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void WildCopy(byte* d, byte* s, int n)
        {
            Unsafe.WriteUnaligned(d, Unsafe.ReadUnaligned<ulong>(s));

            for (int i = 8; i < n; i += 8)
            {
                Unsafe.WriteUnaligned(d + i, Unsafe.ReadUnaligned<ulong>(s + i));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe uint DecodeMod(ref byte* p)
        {
            uint x = 0;

            for (int i = 0; i <= 21; i += 7)
            {
                uint c = *p++;
                x += c << i;
                if (c < 128)
                {
                    break;
                }
            }

            return x;
        }

        private static unsafe int Decompress(byte* input, int inputLength, byte* output, int outputLength)
        {
            byte* op = output;
            byte* ip = input;
            byte* ipEnd = ip + inputLength;
            byte* opEnd = op + outputLength;

            while (ip < ipEnd)
            {
                int token = *ip++;

                if (token >= 32)
                {
                    int run = token >> 5;

                    if (run == 7)
                    {
                        run += (int)DecodeMod(ref ip);
                    }

                    if ((opEnd - op) < run || (ipEnd - ip) < run) // Overrun check
                    {
                        return -1;
                    }

                    WildCopy(op, ip, run);

                    op += run;
                    ip += run;

                    if (ip >= ipEnd)
                    {
                        break;
                    }
                }

                int len = (token & 15) + MinMatch;

                if (len == 15 + MinMatch)
                {
                    len += (int)DecodeMod(ref ip);
                }

                if (opEnd - op < len) // Overrun check
                {
                    return -1;
                }

                int dist = ((token & 16) << 12) + Unsafe.ReadUnaligned<ushort>(ip);
                ip += 2;
                byte* cp = op - dist;
                if (op - output < dist)
                {
                    return -1;
                }

                if (dist >= 8)
                {
                    WildCopy(op, cp, len);
                    op += len;
                }
                else
                {
                    *op++ = *cp++;
                    *op++ = *cp++;
                    *op++ = *cp++;
                    *op++ = *cp++;

                    while (len-- != 4)
                    {
                        *op++ = *cp++;
                    }
                }
            }

            return ip == ipEnd ? (int)(op - output) : -1;
        }
    }
}
