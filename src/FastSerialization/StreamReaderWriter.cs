//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Diagnostics;
using System.IO;
using System.Text;      // For StringBuilder.
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FastSerialization
{
    /// <summary>
    /// A MemoryStreamReader is an implementation of the IStreamReader interface that works over a given byte[] array.  
    /// </summary>
#if STREAMREADER_PUBLIC
    public
#endif
    class MemoryStreamReader : IStreamReader
    {
        /// <summary>
        /// Create a IStreamReader (reads binary data) from a given byte buffer
        /// </summary>
        public MemoryStreamReader(byte[] data) : this(data, 0, data.Length) { }
        /// <summary>
        /// Create a IStreamReader (reads binary data) from a given subregion of a byte buffer 
        /// </summary>
        public MemoryStreamReader(byte[] data, int start, int length)
        {
            bytes = data;
            position = start;
            endPosition = length;
        }
        /// <summary>
        /// The total length of bytes that this reader can read.
        /// </summary>
        public virtual long Length { get { return endPosition; } }
        public virtual bool HasLength { get { return true; } }

        #region implemenation of IStreamReader
        public virtual void Read(byte[] data, int offset, int length)
        {
            if (length > endPosition - position)
            {
                Fill(length);
            }

            Buffer.BlockCopy(bytes, position, data, offset, length);
            position += length;
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public byte ReadByte()
        {
            if (position >= endPosition)
            {
                Fill(1);
            }

            return bytes[position++];
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public short ReadInt16()
        {
            if (position + sizeof(short) > endPosition)
            {
                Fill(sizeof(short));
            }

            int ret = bytes[position] + (bytes[position + 1] << 8);
            position += sizeof(short);
            return unchecked((short)ret);
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public int ReadInt32()
        {
            if (position + sizeof(int) > endPosition)
            {
                Fill(sizeof(int));
            }

            int ret = bytes[position] + ((bytes[position + 1] + ((bytes[position + 2] + (bytes[position + 3] << 8)) << 8)) << 8);
            position += sizeof(int);
            return ret;
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public long ReadInt64()
        {
            uint low = unchecked((uint)ReadInt32());
            uint high = unchecked((uint)ReadInt32());
            return unchecked(((long)high << 32) + low);        // TODO find the most efficient way of doing this. 
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public string ReadString()
        {
            int len = ReadInt32();          // Expect first a character inclusiveCountRet.  -1 means null.
            if (len < 0)
            {
                Debug.Assert(len == -1);
                return null;
            }

            if (sb == null)
            {
                sb = new StringBuilder(len);
            }

            sb.Length = 0;

            Debug.Assert(!HasLength || len < Length);
            while (len > 0)
            {
                int b = ReadByte();
                if (b < 0x80)
                {
                    sb.Append((char)b);
                }
                else if (b < 0xE0)
                {
                    // TODO test this for correctness
                    b = (b & 0x1F);
                    b = b << 6 | (ReadByte() & 0x3F);
                    sb.Append((char)b);
                }
                else
                {
                    // TODO test this for correctness
                    b = (b & 0xF);
                    b = b << 6 | (ReadByte() & 0x3F);
                    b = b << 6 | (ReadByte() & 0x3F);

                    sb.Append((char)b);
                }
                --len;
            }
            return sb.ToString();
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public StreamLabel ReadLabel()
        {
            return (StreamLabel)((ulong)unchecked((uint)ReadInt32()) << 1);
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public virtual void Goto(StreamLabel label)
        {
            Debug.Assert(label != StreamLabel.Invalid);
            Debug.Assert((long)label <= int.MaxValue);
            position = (int)label;
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public virtual StreamLabel Current
        {
            get
            {
                return (StreamLabel)(uint)position;
            }
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public virtual void GotoSuffixLabel()
        {
            const int sizeOfSerializedStreamLabel = 4;
            Goto((StreamLabel)(Length - sizeOfSerializedStreamLabel));
            Goto(ReadLabel());
        }
        /// <summary>
        /// Dispose pattern
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Dispose pattern
        /// </summary>
        protected virtual void Dispose(bool disposing) { }
        #endregion

        #region private 
        internal /*protected*/ virtual void Fill(int minBytes)
        {
            throw new Exception("Streamreader read past end of buffer");
        }
        internal /*protected*/  byte[] bytes;
        internal /*protected*/  int position;
        internal /*protected*/  int endPosition;
        private StringBuilder sb;
        #endregion
    }

    // TODO is unsafe code worth it?
#if true
    /// <summary>
    /// A StreamWriter is an implementation of the IStreamWriter interface that generates a byte[] array. 
    /// </summary>
#if STREAMREADER_PUBLIC
    public
#endif
    class MemoryStreamWriter : IStreamWriter
    {
        /// <summary>
        /// Create IStreamWriter that writes its data to an internal byte[] buffer.  It will grow as needed. 
        /// Call 'GetReader' to get a IStreamReader for the written bytes. 
        /// 
        /// Call 'GetBytes' call to get the raw array.  Only the first 'Length' bytes are valid
        /// </summary>
        public MemoryStreamWriter(int initialSize = 64)
        {
            bytes = new byte[initialSize];
        }

        /// <summary>
        /// Returns a IStreamReader that will read the written bytes. You cannot write additional bytes to the stream after making this call.
        /// </summary>
        /// <returns></returns>
        public virtual MemoryStreamReader GetReader()
        {
            var readerBytes = bytes;
            if (bytes.Length - endPosition > 500000)
            {
                readerBytes = new byte[endPosition];
                Array.Copy(bytes, readerBytes, endPosition);
            }
            return new MemoryStreamReader(readerBytes, 0, endPosition);
        }

        /// <summary>
        /// The number of bytes written so far.
        /// </summary>
        public virtual long Length { get { return endPosition; } }

        /// <summary>
        /// The array that holds the serialized data.
        /// </summary>
        /// <returns></returns>
        public virtual byte[] GetBytes() { return bytes; }

        /// <summary>
        /// Clears any data that was previously written.  
        /// </summary>
        public virtual void Clear() { endPosition = 0; }

        #region Implementation of IStreamWriter
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
        public void Write(byte value)
        {
            if (endPosition >= bytes.Length)
            {
                MakeSpace();
            }

            bytes[endPosition++] = value;
        }
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
        public unsafe void Write(short value)
        {
            if (endPosition + sizeof(short) > bytes.Length)
            {
                MakeSpace();
            }

            fixed (byte* data = bytes)
            {
                *(short*)(data + endPosition) = value;
            }

            endPosition += sizeof(short);
        }
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
        public unsafe void Write(int value)
        {
            if (endPosition + sizeof(int) > bytes.Length)
            {
                MakeSpace();
            }

            fixed (byte* data = bytes)
            {
                *(int*)(data + endPosition) = value;
            }

            endPosition += sizeof(int);
        }
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
        public unsafe void Write(long value)
        {
            if (endPosition + sizeof(long) > bytes.Length)
            {
                MakeSpace();
            }

            fixed (byte* data = bytes)
            {
                *(long*)(data + endPosition) = value;
            }

            endPosition += sizeof(long);
        }
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
        public void Write(StreamLabel value)
        {
            if (((long)value & 0x1) != 0)
                throw new NotSupportedException("Labels must be aligned to a 2-byte boundary.");

            uint packedLabel = (uint)((long)value >> 1);
            Write(unchecked((int)packedLabel));
        }
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
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
                    char c = value[i];
                    if (c <= 0x7F)
                    {
                        Write((byte)value[i]);                 // Only need one byte for UTF8
                    }
                    else if (c <= 0x7FF)
                    {
                        // TODO confirm that this is correct!
                        Write((byte)(0xC0 | (c >> 6)));                // Encode 2 byte UTF8
                        Write((byte)(0x80 | (c & 0x3F)));
                    }
                    else
                    {
                        // TODO confirm that this is correct!
                        Write((byte)(0xE0 | ((c >> 12) & 0xF)));        // Encode 3 byte UTF8
                        Write((byte)(0x80 | ((c >> 6) & 0x3F)));
                        Write((byte)(0x80 | (c & 0x3F)));
                    }
                }
            }
        }
        public void Write(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (length > data.Length - offset)
            {
                throw new ArgumentNullException(nameof(length));
            }

            for (int i = 0; i < length; i++)
            {
                Write(data[offset + i]);
            }
        }
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
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
        /// <summary>
        /// Implementation of IStreamWriter
        /// </summary>
        public void WriteSuffixLabel(StreamLabel value)
        {
            // This is guaranteed to be uncompressed, but since we are not compressing anything, we can
            // simply write the value.  
            Write(value);
        }

        /// <summary>
        /// Dispose pattern
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Dispose pattern
        /// </summary>
        protected virtual void Dispose(bool disposing) { }
        #endregion 
        #region private

        /// <summary>
        /// Makespace makes at least sizeof(long) bytes available (or throws OutOfMemory)
        /// </summary>
        internal /* protected */ virtual void MakeSpace()
        {
            const int maxLength = 0x7FFFFFC7; // Max array length for byte[]
            int newLength = 0;

            // Make sure we don't exceed max possible size
            if (bytes.Length < (maxLength / 3) * 2)
            {
                newLength = (bytes.Length / 2) * 3;
            }
            else if (bytes.Length < maxLength - sizeof(long))      // Write(long) expects Makespace to make at 8 bytes of space.   
            {
                newLength = maxLength;                             // If we can do this, use up the last available length 
            }
            else
            {
                throw new OutOfMemoryException();                  // Can't make space anymore
            }

            Debug.Assert(bytes.Length + sizeof(long) <= newLength);
            byte[] newBytes = new byte[newLength];
            Array.Copy(bytes, newBytes, bytes.Length);
            bytes = newBytes;
        }
        internal /* protected */ byte[] bytes;
        internal /* protected */ int endPosition;
        #endregion
    }
#else
    /// <summary>
    /// A StreamWriter is an implementation of the IStreamWriter interface that generates a byte[] array. 
    /// </summary>
    unsafe class MemoryStreamWriter : IStreamWriter
    {
        public MemoryStreamWriter() : this(64) { }
        public MemoryStreamWriter(int size)
        {
            bytes = new byte[size];
            pinningHandle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            fixed (byte* bytesAsPtr = &bytes[0])
                bufferStart = bytesAsPtr;
            bufferCur = bufferStart;
            bufferEnd = &bufferStart[bytes.Length];
        }

        // TODO 
        public virtual long Length { get { return (int) (bufferCur - bufferStart); } }
        public virtual void Clear() { throw new Exception(); }

        public void Write(byte value)
        {
            if (bufferCur + sizeof(byte) > bufferEnd)
                DoMakeSpace();
            *((byte*)(bufferCur)) = value;
            bufferCur += sizeof(byte);
        }
        public void Write(short value)
        {
            if (bufferCur + sizeof(short) > bufferEnd)
                DoMakeSpace();
            *((short*)(bufferCur)) = value;
            bufferCur += sizeof(short);
        }
        public void Write(int value)
        {
            if (bufferCur + sizeof(int) <= bufferEnd)
            {
                *((int*)(bufferCur)) = value;
                bufferCur += sizeof(int);
            }
            else 
                WriteSlow(value);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void WriteSlow(int value)
        {
            DoMakeSpace();
            Debug.Assert(bufferCur + 8 < bufferEnd);
            Write(value);
        }

        public void Write(long value)
        {
            if (bufferCur + sizeof(long) > bufferEnd)
                DoMakeSpace();
            *((long*)(bufferCur)) = value;
            bufferCur += sizeof(long);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void DoMakeSpace()
        {
            endPosition = (int)(bufferCur - bufferStart);
            MakeSpace();
            bufferCur = &bufferStart[endPosition];
            bufferEnd = &bufferStart[bytes.Length];
        }

        public void Write(StreamLabel value)
        {
            Write((int)value);
        }
        public void Write(string value)
        {
            if (value == null)
            {
                Write(-1);          // negative bufferSize means null. 
            }
            else
            {
                Write(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c < 128)
                        Write((byte)value[i]);                 // Only need one byte for UTF8
                    else if (c < 2048)
                    {
                        // TODO confirm that this is correct!
                        Write((byte) (0xC0 | (c >> 6)));                // Encode 2 byte UTF8
                        Write((byte) (0x80 | (c & 0x3F)));
                    }
                    else
                    {
                        // TODO confirm that this is correct!
                        Write((byte) (0xE0 | ((c >> 12) & 0xF)));        // Encode 3 byte UTF8
                        Write((byte) (0x80 | ((c >> 6) & 0x3F)));
                        Write((byte) (0x80 | (c & 0x3F)));
                    }
                }
            }
        }
        public virtual StreamLabel GetLabel()
        {
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
            outputStream.Write(bytes, 0, (int)Length);
        }
        // Note that the returned MemoryStreamReader is not valid if more writes are done.  
        public MemoryStreamReader GetReader() { return new MemoryStreamReader(bytes); }

    #region private
        ~MemoryStreamWriter()
        {
            this.Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (pinningHandle.IsAllocated)
                pinningHandle.Free();
        }

        internal /*protected*/  virtual void MakeSpace()
        {
            byte[] newBytes = new byte[bytes.Length * 3 / 2];
            Array.Copy(bytes, newBytes, bytes.Length);
            bytes = newBytes;
            fixed (byte* bytesAsPtr = &bytes[0])
                bufferStart = bytesAsPtr;
            bufferCur = &bufferStart[endPosition];
            bufferEnd = &bufferStart[bytes.Length];
        }
        internal /*protected*/  byte[] bytes;
        internal /*protected*/  int endPosition;

        private System.Runtime.InteropServices.GCHandle pinningHandle;
        byte* bufferStart;
        byte* bufferCur;
        byte* bufferEnd;
    #endregion
    }
#endif

    /// <summary>
    /// A IOStreamStreamReader hooks a MemoryStreamReader up to an input System.IO.Stream.  
    /// </summary>
#if STREAMREADER_PUBLIC
    public
#endif
    class IOStreamStreamReader : MemoryStreamReader, IDisposable
    {
        /// <summary>
        /// Create a new IOStreamStreamReader from the given file.  
        /// </summary>
        /// <param name="fileName"></param>
        public IOStreamStreamReader(string fileName)
            : this(new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)) { }

        /// <summary>
        /// Create a new IOStreamStreamReader from the given System.IO.Stream.   Optionally you can specify the size of the read buffer
        /// The stream will be closed by the IOStreamStreamReader when it is closed.  
        /// </summary>
        public IOStreamStreamReader(Stream inputStream, int bufferSize = defaultBufferSize, bool leaveOpen = false)
            : base(new byte[bufferSize + align], 0, 0)
        {
            Debug.Assert(bufferSize % align == 0);
            this.inputStream = inputStream;
            this.leaveOpen = leaveOpen;
        }

        /// <summary>
        /// close the file or underlying stream and clean up 
        /// </summary>
        public void Close()
        {
            Dispose(true);
        }

        #region overrides 
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public override StreamLabel Current
        {
            get
            {
                return (StreamLabel)(positionInStream + position);
            }
        }
        /// <summary>
        /// Implementation of IStreamReader
        /// </summary>
        public override void Goto(StreamLabel label)
        {
            long offset = (long)label - positionInStream;
            if (offset > endPosition || offset < 0)
            {
                if(!inputStream.CanSeek)
                {
                    if((long)label < positionInStream + endPosition)
                    {
                        throw new Exception("Stream does not support seeking backwards");
                    }
                }
                positionInStream = (long)label & ~(align - 1);
                position = (int)((long)label & (align - 1));
                endPosition = 0;
            }
            else
            {
                position = (int)offset;
            }
        }
        /// <summary>
        /// Implementation of MemoryStreamReader
        /// </summary>
        public override long Length { get { return inputStream.Length; } }
        public override bool HasLength { get { return inputStream.CanSeek; } }

        public override void Read(byte[] data, int offset, int length)
        {
            // The base class is constrained to only handle reads that are <= size of the cache
            // For larger reads we can take what is available in the cache and satisfy the
            // remainder from the stream
            if (length > (bytes.Length - align))
            {
                int positionAlignmentOffset = position % align;
                int alignedLength = (length & ~(align - 1)) - positionAlignmentOffset;
                int cacheBytes = Math.Max(0, endPosition - position);
                Buffer.BlockCopy(bytes, position, data, offset, cacheBytes);
                int bytesRead = cacheBytes;
                while (bytesRead < alignedLength)
                {
                    int count = inputStream.Read(data, offset + bytesRead, alignedLength - bytesRead);
                    inputStreamBytesRead += count;
                    if (count == 0)
                    {
                        throw new Exception("Read past end of stream.");
                    }
                    bytesRead += count;
                }

                // Update our internal state to match the read above
                Debug.Assert(bytesRead == alignedLength);
                Goto(Current.Add(alignedLength));

                if (alignedLength == length)
                {
                    return;
                }
                // fall through to read the unaligned portion
                length -= alignedLength;
                offset += bytesRead;
            }

            if (length > endPosition - position)
            {
                Fill(length);
            }

            Buffer.BlockCopy(bytes, position, data, offset, length);
            position += length;
        }
        #endregion 

        #region private
        /// <summary>
        /// Dispose pattern
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!leaveOpen)
                {
                    inputStream.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        internal /*protected*/  const int align = 8;        // Needs to be a power of 2
        internal /*protected*/  const int defaultBufferSize = 0x4000;  // 16K 

        /// <summary>
        /// Fill the buffer, making sure at least 'minimum' byte are available to read.  Throw an exception
        /// if there are not that many bytes.  
        /// </summary>
        /// <param name="minimum"></param>
        internal /*protected*/  override void Fill(int minimum)
        {
            Debug.Assert(minimum <= (bytes.Length - align));
            if (endPosition > position)
            {
                int slideAmount = position & ~(align - 1);             // round down to stay aligned.  
                for (int i = slideAmount; i < endPosition; i++)        // Slide everything down.  
                {
                    bytes[i - slideAmount] = bytes[i];
                }

                endPosition -= slideAmount;
                position -= slideAmount;
                positionInStream += slideAmount;
            }
            else
            {
                int slideAmount = position & ~(align - 1);
                endPosition = 0;
                position -= slideAmount;
                positionInStream += slideAmount;
                if (inputStream.CanSeek)
                {
                    // if you are within one read of the end of file, go backward to read the whole block.  
                    long lastBlock = Math.Max(0, (inputStream.Length - bytes.Length + align) & ~(align - 1));
                    if (positionInStream >= lastBlock)
                    {
                        int adjustBack = (int)(positionInStream - lastBlock);
                        positionInStream -= adjustBack;
                        position += adjustBack;
                    }
                }
            }

            Debug.Assert(positionInStream % align == 0);
            lock (inputStream)
            {
                // We need to get the stream positioned at (positionInStream + endPosition)
                // Seekable streams: Easy we can seek
                // Non-seekable streams: We need to read forward. We already did error checking
                //                       in Goto() to ensure that the stream movement is going
                //                       forward, not backwards.
                if(inputStream.CanSeek)
                {
                    inputStream.Seek(positionInStream + endPosition, SeekOrigin.Begin);
                }
                else
                {
                    long seekForwardDistance = (positionInStream + endPosition) - inputStreamBytesRead;
                    for (long i = 0; i < seekForwardDistance; i++)
                    {
                        inputStream.ReadByte();
                    }
                    inputStreamBytesRead += seekForwardDistance;
                }

                // PERF policy
                // In the streaming (non-seekable) case we don't want to buffer any more data than was
                // requested and needed for alignment because this might cause the thread to block waiting 
                // for the unneeded data to arrive. There is probably a better way to do this that can
                // oportunistically buffer if the data is available but this code isn't that sophisticated
                // yet.
                //
                // In the non-streaming (seekable) case we do want to buffer because that lets the
                // reader achieve higher throughput.
                int fillSize = inputStream.CanSeek ? bytes.Length : (position + minimum + (align-1)) & ~(align-1);
                

                for (; endPosition < fillSize; )
                {
                    System.Threading.Thread.Sleep(0);       // allow for Thread.Interrupt
                    int count = inputStream.Read(bytes, endPosition, fillSize - endPosition);
                    inputStreamBytesRead += count;
                    if (count == 0)
                    {
                        break;
                    }

                    endPosition += count;
                }
            }
            if (endPosition - position < minimum)
            {
                throw new Exception("Read past end of stream.");
            }
        }
        internal /*protected*/  Stream inputStream;
        internal /* protected*/ long inputStreamBytesRead; // only required for non-seekable streams
        private bool leaveOpen;
        internal /*protected*/  long positionInStream;
        #endregion
    }

    /// <summary>
    /// A PinnedStreamReader is an IOStream reader that will pin its read buffer.
    /// This allows it it support a 'GetPointer' API efficiently.   The 
    /// GetPointer API lets you access data from the stream as raw byte 
    /// blobs without having to copy the data.  
    /// </summary>
#if STREAMREADER_PUBLIC
    public
#endif
    sealed unsafe class PinnedStreamReader : IOStreamStreamReader
    {
        /// <summary>
        /// Create a new PinnedStreamReader that gets its data from a given file.  You can optionally set the size of the read buffer.  
        /// </summary>
        public PinnedStreamReader(string fileName, int bufferSize = defaultBufferSize)
            : this(new FileStream(fileName, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete), bufferSize)
        { }

        /// <summary>
        /// Create a new PinnedStreamReader that gets its data from a given System.IO.Stream.  You can optionally set the size of the read buffer.  
        /// The stream will be closed by the PinnedStreamReader when it is closed.  
        /// </summary>
        public PinnedStreamReader(Stream inputStream, int bufferSize = defaultBufferSize)
            : base(inputStream, bufferSize)
        {
            // Pin the array
            pinningHandle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            fixed (byte* bytesAsPtr = &bytes[0])
            {
                bufferStart = bytesAsPtr;
            }
        }

        /// <summary>
        /// Clone the PinnnedStreamReader so that it reads from the same stream as this one.   They will share the same
        /// System.IO.Stream, but each will lock and seek when accessing that stream so they can both safely share it.  
        /// </summary>
        /// <returns></returns>
        public PinnedStreamReader Clone()
        {
            PinnedStreamReader ret = new PinnedStreamReader(inputStream, bytes.Length - align);
            return ret;
        }

        /// <summary>
        /// Get a byte* pointer to the input buffer at 'Position' in the IReadStream that is at least 'length' bytes long.  
        /// (thus ptr to ptr+len is valid).   Note that length cannot be larger than the buffer size passed to the reader
        /// when it was constructed.  
        /// </summary>
        public unsafe byte* GetPointer(StreamLabel Position, int length)
        {
            Goto(Position);
            return GetPointer(length);
        }

        /// <summary>
        /// Get a byte* pointer to the input buffer at the current read position is at least 'length' bytes long.  
        /// (thus ptr to ptr+len is valid).   Note that length cannot be larger than the buffer size passed to the reader
        /// when it was constructed.  
        /// </summary>
        public unsafe byte* GetPointer(int length)
        {
            if (position + length > endPosition)
            {
                Fill(length);
            }
#if DEBUG
            fixed (byte* bytesAsPtr = &bytes[0])
            {
                Debug.Assert(bytesAsPtr == bufferStart, "Error, buffer not pinnned");
            }

            Debug.Assert(position < bytes.Length);
#endif
            return (byte*)(&bufferStart[position]);
        }

        #region private
        ~PinnedStreamReader()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (pinningHandle.IsAllocated)
            {
                pinningHandle.Free();
            }

            base.Dispose(disposing);
        }

        private System.Runtime.InteropServices.GCHandle pinningHandle;
        private byte* bufferStart;
        #endregion
    }

#if PINNEDSTREAMREADER_TESTS
    public static class PinnedStreamTests
    {
        public static void Tests()
        {
            string testOrig = "text.orig";

            Random r = new Random(23);

            for (int j = 0; j < 10; j++)
            {
                for (int fileSize = 1023; fileSize <= 1025; fileSize++)
                {
                    CreateDataFile(testOrig, fileSize);
                    byte[] origData = File.ReadAllBytes(testOrig);

                    for (int bufferSize = 16; bufferSize < 300; bufferSize += 24)
                    {
                        FileStream testData = File.OpenRead(testOrig);
                        PinnedStreamReader reader = new PinnedStreamReader(testData, bufferSize);

                        // Try reading back in various seek positions. 
                        for (int i = 0; i < 100; i++)
                        {
                            int position = r.Next(0, origData.Length);
                            int size = r.Next(0, bufferSize) + 1;

                            reader.Goto((StreamLabel)(uint)position);
                            Compare(reader, origData, position, size);
                        }
                        reader.Close();
                    }
                }
                Console.WriteLine("Finished Round " + j);
            }
        }

        static int compareCount = 0;

        private static void Compare(PinnedStreamReader reader, byte[] buffer, int offset, int chunkSize)
        {
            compareCount++;
            if (compareCount == -1)
                Debugger.Break();

            for (int pos = offset; pos < buffer.Length; pos += chunkSize)
            {
                if (pos + chunkSize > buffer.Length)
                    chunkSize = buffer.Length - pos;
                CompareBuffer(reader.GetPointer(chunkSize), buffer, pos, chunkSize);
                reader.Skip(chunkSize);
            }
        }

        private unsafe static bool CompareBuffer(IntPtr ptr, byte[] buffer, int offset, int size)
        {
            byte* bytePtr = (byte*)ptr;

            for (int i = 0; i < size; i++)
            {
                if (buffer[i + offset] != bytePtr[i])
                {
                    Debug.Assert(false);
                    return false;
                }
            }
            return true;
        }
        private static void CreateDataFile(string name, int length)
        {
            FileStream stream = File.Open(name, FileMode.Create);
            byte val = 0;
            for (int i = 0; i < length; i++)
                stream.WriteByte(val++);
            stream.Close();
        }

    }
#endif

    /// <summary>
    /// A IOStreamStreamWriter hooks a MemoryStreamWriter up to an output System.IO.Stream
    /// </summary>
#if STREAMREADER_PUBLIC
    public
#endif
    class IOStreamStreamWriter : MemoryStreamWriter, IDisposable
    {
        /// <summary>
        /// Create a IOStreamStreamWriter that writes its data to a given file that it creates
        /// </summary>
        /// <param name="fileName"></param>
        public IOStreamStreamWriter(string fileName) : this(new FileStream(fileName, FileMode.Create)) { }

        /// <summary>
        /// Create a IOStreamStreamWriter that writes its data to a System.IO.Stream
        /// </summary>
        public IOStreamStreamWriter(Stream outputStream, int bufferSize = defaultBufferSize + sizeof(long), bool leaveOpen = false)
            : base(bufferSize)
        {
            this.outputStream = outputStream;
            this.leaveOpen = leaveOpen;
            streamLength = outputStream.Length;
        }

        /// <summary>
        /// Flush any written data to the underlying System.IO.Stream
        /// </summary>
        public void Flush()
        {
            outputStream.Write(bytes, 0, endPosition);
            streamLength += endPosition;
            endPosition = 0;
            outputStream.Flush();
        }

        /// <summary>
        /// Insures the bytes in the stream are written to the stream and cleans up resources.  
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// Access the underlying System.IO.Stream.   You should avoid using this if at all possible.  
        /// </summary>
        public Stream RawStream { get { return outputStream; } }

        #region overrides
        /// <summary>
        /// Implementation of the MemoryStreamWriter interface 
        /// </summary>
        public override long Length
        {
            get
            {
                Debug.Assert(streamLength == outputStream.Length);
                return base.Length + streamLength;
            }
        }
        /// <summary>
        /// Implementation of the IStreamWriter interface 
        /// </summary>
        public override StreamLabel GetLabel(bool allowPadding)
        {
            long len = Length;
            if ((len >> 1) != (uint)(len >> 1))
            {
                throw new NotSupportedException("Streams larger than 8 GB.  You need to use /MaxEventCount to limit the size.");
            }

            if ((len & 0x1) != 0)
            {
                if (!allowPadding)
                    throw new NotSupportedException("Labels must be aligned to a 2-byte boundary.");

                Write((byte)Tags.Padding);
                len++;
            }

            return (StreamLabel)len;
        }
        /// <summary>
        /// Implementation of the MemoryStreamWriter interface 
        /// </summary>
        public override void Clear()
        {
            outputStream.SetLength(0);
            streamLength = 0;
        }
        /// <summary>
        /// Implementation of the MemoryStreamWriter interface 
        /// </summary>
        public override MemoryStreamReader GetReader() { throw new InvalidOperationException(); }
        /// <summary>
        /// Implementation of the MemoryStreamWriter interface 
        /// </summary>
        public override byte[] GetBytes() { throw new InvalidOperationException(); }
        #endregion 
        #region private
        internal /*protected*/  override void MakeSpace()
        {
            Debug.Assert(endPosition > bytes.Length - sizeof(long));
            outputStream.Write(bytes, 0, endPosition);
            streamLength += endPosition;
            endPosition = 0;
        }

        /// <summary>
        /// Dispose pattern
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
                if (!leaveOpen)
                {
                    outputStream.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private const int defaultBufferSize = 1024 * 8 - sizeof(long);
        private Stream outputStream;
        private bool leaveOpen;
        private long streamLength;

        #endregion
    }
}
