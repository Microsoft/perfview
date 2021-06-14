using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FastSerialization
{
    public class SegmentedMemoryStreamWriter
    {
        public SegmentedMemoryStreamWriter() : this(64) { }
        public SegmentedMemoryStreamWriter(int initialSize)
        {
            bytes = new SegmentedList<byte>(65_536, initialSize);
        }

        public virtual long Length { get { return bytes.Count; } }
        public virtual void Clear() { bytes = new SegmentedList<byte>(131_072); }

        public void Write(byte value)
        {
            bytes.Add(value);
        }
        public void Write(short value)
        {
            int intValue = value;
            bytes.Add((byte)intValue); intValue = intValue >> 8;
            bytes.Add((byte)intValue); intValue = intValue >> 8;
        }
        public void Write(int value)
        {
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
        }
        public void Write(long value)
        {
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
            bytes.Add((byte)value); value = value >> 8;
        }
        public void Write(StreamLabel value)
        {
            Write((int)value);
        }
        public void Write(string value)
        {
            if (value == null)
            {
                Write(-1);          // negative charCount means null. 
            }
            else
            {
                Write(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    // TODO do actual UTF8
                    Write((byte)value[i]);
                }
            }
        }
        public virtual StreamLabel GetLabel(bool allowPadding)
        {
            if ((Length & 0x1) != 0)
            {
                if (!allowPadding)
                    throw new NotSupportedException("Labels must be aligned to a 2-byte boundary.");

                Write((byte)Tags.Padding);
                Debug.Assert((Length & 0x1) == 0);
            }

            return (StreamLabel)Length;
        }
        public void WriteSuffixLabel(StreamLabel value)
        {
            // This is guaranteed to be uncompressed, but since we are not compressing anything, we can
            // simply write the value.  
            Write(value);
        }

        public void WriteToStream(Stream outputStream)
        {
            // TODO really big streams will overflow;
            outputStream.Write(bytes.ToArray(), 0, (int)Length);
        }
        // Note that the returned IMemoryStreamReader is not valid if more writes are done.  
        public SegmentedMemoryStreamReader GetReader()
        {
            var readerBytes = bytes;
            return new SegmentedMemoryStreamReader(readerBytes, 0, (int)readerBytes.Count);
        }
        public void Dispose() { }

        #region private
        protected virtual void MakeSpace()
        {
            // Not necessary
        }

        public byte[] GetBytes()
        {
            return bytes.ToArray();
        }

        protected SegmentedList<byte> bytes;
        #endregion
    }
}

