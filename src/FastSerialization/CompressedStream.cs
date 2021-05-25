// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
//	Copyright (C) 2007 Microsoft Corporation.  All Rights Reserved.
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Diagnostics;
using System.Threading;

// This code is currently not used, but is useful if we want to compress other stream-based
// formats (basically this allows you to add compression onto any stream-based protocol.
// I have #ifdefed it out for now, since it is unused, but want to check it in so that we
// don't lose it. 
#if false 

#if !NETSTANDARD1_3

namespace Utilities
{
    /// This is basically a DeflateStream that supports seaking ON READ and does good buffering. To do this
    /// we compress in independent chunks of blockSize code:CompressedWriteStream.blockSize. The output stream
    /// looks like the following
    /// 
    /// * DWORD Block Size (units that uncompressed data is compressed), Valid for the whole file
    /// * Block 1
    ///   * DWORD compressed blockSize (in bytes) of first Chunk
    ///   * compressed Data for first chunk
    /// * Block 2
    ///   * DWORD compressed blockSize (in bytes) of first Chunk
    ///   * compressed Data for first chunk  
    /// * ...
    /// * Block LAST
    ///   * Negative DWORD compressed blockSize (in bytes) of first Chunk (indicates last chunk
    ///   * DWORD blockSize of uncompressed data;
    ///   * compressed Data for last chunk
    /// * BlockTable (array of QWORDS of file offsets to the beginning of block 0 through N)
    /// * DWORD number of QWORDS entries in BlockTable. 
    /// * DWORD number of uncompressed bytes in the last block
    /// 
    /// This layout allows the reader to efficiently present an uncompressed view of the stream. 
    /// 
    class CompressedWriteStream : Stream, IDisposable
    {
#if false
        public static void CompressFile(string inputFilePath, string compressedFilePath)
        {
            using (Stream compressor = new CompressedWriteStream(compressedFilePath))
                Utilities.StreamUtilities.CopyFromFile(inputFilePath, compressor);
        }
#endif 
        public CompressedWriteStream(string filePath) : this(File.Create(filePath)) { }
        public CompressedWriteStream(Stream outputStream) : this(outputStream, DefaultBlockSize, false) { }
        /// <summary>
        ///  Create a compressed stream. If blocksize is less than 1K you are likely to NOT achieve
        ///  good compression. Generally a block size of 8K - 256K is a good range (64K is the default and
        ///  generally there is less than .5% to be gained by making it bigger).    
        /// </summary>
        public CompressedWriteStream(Stream outputStream, int blockSize, bool leaveOpen)
        {
            Debug.Assert(64 <= blockSize && blockSize <= 1024 * 1024 * 4);     // sane values.   
            this.outputStream = outputStream;
            this.blockSize = blockSize;
            this.leaveOpen = leaveOpen;
            outputBuffer = new MemoryStream();
            compressor = new DeflateStream(outputBuffer, CompressionMode.Compress, true);
            compressedBlockSizes = new List<int>();
            WriteSignature(outputStream, Signature);
            WriteInt(outputStream, 1);                                // Version number;
            WriteInt(outputStream, blockSize);
            positionOfFirstBlock = outputStream.Position;
            spaceLeftInBlock = blockSize;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > spaceLeftInBlock)
            {
                if (spaceLeftInBlock > 0)
                {
                    compressor.Write(buffer, offset, spaceLeftInBlock);
                    offset += spaceLeftInBlock;
                    count -= spaceLeftInBlock;
                    spaceLeftInBlock = 0;
                }
                FlushBlock(false);

                // Set up for the next block, create a new 
                outputBuffer.SetLength(0);
                compressor = new DeflateStream(outputBuffer, CompressionMode.Compress, true);
                spaceLeftInBlock = blockSize;
            }
            compressor.Write(buffer, offset, count);
            spaceLeftInBlock -= count;
        }
        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {

                int lastBlockByteCount = blockSize - spaceLeftInBlock;
                // Write out the last block
                FlushBlock(true);

                // Write out the table of block sizes (to allow for efficient arbitrary seeking). 
                CompressedWriteStream.LogLine("Writing offset table starting at 0x" + outputStream.Position.ToString("x"));
                long blockOffset = positionOfFirstBlock;
                foreach (int compressedBlockSize in compressedBlockSizes)
                {
                    WriteLong(outputStream, blockOffset);
                    blockOffset += (compressedBlockSize + 4);       // Add the total blockSize (with header) of the previous block
                }
                WriteLong(outputStream, blockOffset);

                CompressedWriteStream.LogLine("Writing offset table count " + (compressedBlockSizes.Count + 1) +
                    " uncompressed Left = 0x" + lastBlockByteCount.ToString("x"));
                // remember the count of the table. 
                WriteInt(outputStream, compressedBlockSizes.Count + 1);
                // and the number of uncompressed bytes in the last block
                WriteInt(outputStream, lastBlockByteCount);
                if (!leaveOpen)
                    outputStream.Dispose();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }

        // This is stored at the beginning as ASCII to mark this stream  
        public static readonly string Signature = "!BlockDeflateStream";

        // methods that are purposely not implemented 
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        public override void Flush()
        {
            throw new NotSupportedException();
        }

#region private
        private void FlushBlock(bool lastBlock)
        {
            compressor.Dispose();
            int compressedBlockSize = (int)outputBuffer.Length;
            Debug.Assert(spaceLeftInBlock == 0 || lastBlock);
            CompressedWriteStream.LogLine("FlushBlock: lastBlock " + lastBlock + " compressedBlockSize = 0x" + compressedBlockSize.ToString("x") +
                " uncompresseSize=0x" + (blockSize - spaceLeftInBlock).ToString("x"));
            CompressedWriteStream.LogLine("Block header placed at filePosition=0x" + outputStream.Position.ToString("x"));
            // Write the block out prepended with its blockSize
            if (lastBlock)
            {
                WriteInt(outputStream, -compressedBlockSize);
                WriteInt(outputStream, blockSize - spaceLeftInBlock);   // write the uncompressed blockSize too. 
            }
            else
            {
                compressedBlockSizes.Add(compressedBlockSize);
                WriteInt(outputStream, compressedBlockSize);
            }

            outputStream.Write(outputBuffer.GetBuffer(), 0, compressedBlockSize);
            // TODO remove outputStream.Write(new byte[compressedBlockSize], 0, compressedBlockSize);
            CompressedWriteStream.LogLine("After write, filePosition=0x" + outputStream.Position.ToString("x"));
        }
        static void WriteInt(Stream stream, int number)
        {
            for (int i = 0; i < 4; i++)
            {
                stream.WriteByte((byte)number);
                number >>= 8;
            }
        }
        static void WriteLong(Stream stream, long number)
        {
            for (int i = 0; i < 8; i++)
            {
                stream.WriteByte((byte)number);
                number >>= 8;
            }
        }
        private static void WriteSignature(Stream outputStream, string sig)
        {
            int i = 0;
            while (i < sig.Length)
            {
                outputStream.WriteByte((byte)sig[i]);
                i++;
            }
            // DWORD align it.  
            while (i % 4 != 0)
            {
                outputStream.WriteByte(0);
                i++;
            }
        }
        [Conditional("DEBUG")]
        internal static void LogLine(string line)
        {
            // Debugger.Log(1, "Compressor", line + "\r\n");
        }

        const int DefaultBlockSize = 64 * 1024;

        Stream outputStream;            // Where the compressed bytes end up.
        int blockSize;
        bool leaveOpen;                 // do not close the underlying stream on close.  
        long positionOfFirstBlock;
        List<int> compressedBlockSizes;

        // represents the current position in the file. 
        DeflateStream compressor;       // Feed writes to this. 
        MemoryStream outputBuffer;      // We need to buffer output to determine its blockSize 
        int spaceLeftInBlock;

#endregion
    }

    class CompressedReadStream : Stream, IDisposable
    {
        public CompressedReadStream(string filePath) : this(File.OpenRead(filePath), false) { }
        /// <summary>
        /// Reads the stream of bytes written by code:CompressedWriteStream 
        /// </summary>
        CompressedReadStream(Stream compressedData, bool leaveOpen)
        {
            if (!compressedData.CanRead || !compressedData.CanSeek)
                throw new ArgumentException("Stream must be readable and seekable", "compressedData");
            this.compressedData = compressedData;
            this.leaveOpen = leaveOpen;
            ReadSignature(compressedData, CompressedWriteStream.Signature);
            int versionNumber = ReadInt(compressedData);
            if (versionNumber != 1)
                throw new NotSupportedException("Version number Mismatch");
            maxUncompressedBlockSize = ReadInt(compressedData);
            Debug.Assert(64 <= maxUncompressedBlockSize && maxUncompressedBlockSize <= 1024 * 1024 * 4);      // check for sane values. 
            nextCompressedBlockStartPosition = compressedData.Position;
            // uncompressedBlockStartPosition = 0;
            // uncompressedBlockSize = 0;
        }

#if false
        public static void DecompressFile(string compressedFilePath, string outputFilePath)
        {
            using (Stream decompressor = new CompressedWriteStream(compressedFilePath))
                Utilities.StreamUtilities.CopyToFile(decompressor, outputFilePath);
        }
#endif 

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            int bytesRead;
            while (count > (uncompressedDataLeft + totalBytesRead))
            {
                bytesRead = 0;
                if (uncompressedDataLeft > 0)
                    bytesRead += decompressor.Read(buffer, offset + totalBytesRead, uncompressedDataLeft);
                totalBytesRead += bytesRead;
                uncompressedDataLeft -= bytesRead;
                if (uncompressedDataLeft == 0)
                {
                    if (lastBlock)
                        return totalBytesRead;

                    FillBlock(uncompressedBlockStartPosition + uncompressedBlockSize, nextCompressedBlockStartPosition);
                }
            }
            bytesRead = decompressor.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
            totalBytesRead += bytesRead;
            uncompressedDataLeft -= bytesRead;
            Debug.Assert(totalBytesRead <= count);
            return totalBytesRead;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.End)
                offset = offset + Length;
            else if (origin == SeekOrigin.Current)
                offset += Position;

            Position = offset;
            return offset;
        }
        public override long Length
        {
            get
            {
                InitBlockStarts();
                return uncompressedStreamLength;
            }
        }
        public override long Position
        {
            get
            {
                return uncompressedBlockStartPosition + (uncompressedBlockSize - uncompressedDataLeft);
            }
            set
            {
                long relativeOffset = value - Position;
                // Optimization: are we seeking a small forward offset
                if (0 <= relativeOffset && relativeOffset < uncompressedBlockStartPosition + uncompressedDataLeft)
                {
                    Skip((int)relativeOffset);
                    return;
                }

                int blockNumber = (int)(value / maxUncompressedBlockSize);
                long newUncompressedBlockStartPosition = blockNumber * maxUncompressedBlockSize;
                int remainder = (int)(value - newUncompressedBlockStartPosition);

                FillBlock(newUncompressedBlockStartPosition, GetCompressedPositionForBlock(blockNumber));
                Skip(remainder);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!leaveOpen)
                    compressedData.Dispose();
                if (decompressor != null)
                    decompressor.Dispose();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }

        // Methods purposefully left unimplemented since they apply to writable streams
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override void Flush() { throw new NotSupportedException(); }

#region private

        private void ReadSignature(Stream inputStream, string sig)
        {
            int i = 0;
            bool badSig = false;
            while (i < sig.Length)
            {
                if (inputStream.ReadByte() != sig[i])
                    badSig = true;
                i++;
            }
            // DWORD align it.  
            while (i % 4 != 0)
            {
                if (inputStream.ReadByte() != 0)
                    badSig = true;
                i++;
            }
            if (badSig)
                throw new Exception("Stream signature mismatch.  Bad data format.");
        }
        /// <summary>
        /// Initializes the current block to point at the beginning of the block (a block is the length as
        /// well as the data) that starts at the uncompressed location 'uncompressedBlockStart' which as the
        /// cooresponding compressed location 'compressedBlockStart'.
        /// </summary>
        private void FillBlock(long uncompressedBlockStart, long compressedBlockStart)
        {
            CompressedWriteStream.LogLine("FillBlock: uncompressedBlockStart 0x" + uncompressedBlockStart.ToString("x") +
                " compressedBlockStart 0x" + compressedBlockStart.ToString("x"));
            // Advance the uncompressed position
            uncompressedBlockStartPosition = uncompressedBlockStart;
            // and set the compressed stream to just past this block's data
            compressedData.Position = compressedBlockStart;

            // Read in the next block' blockSize (both compressed and uncompressed)
            uncompressedBlockSize = maxUncompressedBlockSize;
            int compressedBlockSize = ReadInt(compressedData);
            lastBlock = false;
            if (compressedBlockSize < 0)
            {
                compressedBlockSize = -compressedBlockSize;
                uncompressedBlockSize = ReadInt(compressedData);
                lastBlock = true;
            }
            Debug.Assert(compressedBlockSize <= maxUncompressedBlockSize * 3);       // I have never seen expansion more than 2X
            Debug.Assert(uncompressedBlockSize <= maxUncompressedBlockSize);
            if (decompressor != null)
                decompressor.Dispose();
            // Get next clump of data. 
            decompressor = new DeflateStream(compressedData, CompressionMode.Decompress, true);

            // Set the uncompressed and compressed data pointers. 
            uncompressedDataLeft = uncompressedBlockSize;
            nextCompressedBlockStartPosition = compressedData.Position + compressedBlockSize;

            CompressedWriteStream.LogLine("FillBlock compressedBlockSize = 0x" + compressedBlockSize.ToString("x") + " lastblock = " + lastBlock);
            CompressedWriteStream.LogLine("FillBlock: DONE: uncompressedDataLeft 0x" + uncompressedDataLeft.ToString("x") +
                " nextCompressedBlockStartPosition 0x" + nextCompressedBlockStartPosition.ToString("x"));
        }

        private long GetCompressedPositionForBlock(int blockNumber)
        {
            InitBlockStarts();

            int blockStartPos = blockNumber * sizeof(long);

            int low =
                 blockStarts[blockStartPos] +
                (blockStarts[blockStartPos + 1] << 8) +
                (blockStarts[blockStartPos + 2] << 16) +
                (blockStarts[blockStartPos + 3] << 24);
            int high =
                 blockStarts[blockStartPos + 4] +
                (blockStarts[blockStartPos + 5] << 8) +
                (blockStarts[blockStartPos + 6] << 16) +
                (blockStarts[blockStartPos + 7] << 24);

            return (long)(uint)low + (((long)high) << 32);
        }
        private bool Skip(int bytesToSkip)
        {
            int readSize = bytesToSkip;
            if (readSize > 1024)
                readSize = 1024;

            byte[] buffer = new byte[readSize];
            do
            {
                int count = Read(buffer, 0, readSize);
                if (count == 0)
                    return false;
                bytesToSkip -= count;

            } while (bytesToSkip > 0);
            return true;
        }
        private static int ReadInt(Stream compressedData)
        {
            int ret = compressedData.ReadByte() +
                (compressedData.ReadByte() << 8) +
                (compressedData.ReadByte() << 16) +
                (compressedData.ReadByte() << 24);
            return ret;
        }
        private void InitBlockStarts()
        {
            if (blockStarts != null)
                return;

            long origPosition = compressedData.Position;
            compressedData.Seek(-8, SeekOrigin.End);
            int numberOfBlocks = ReadInt(compressedData);
            int lastBlockLength = ReadInt(compressedData);
            Debug.Assert(lastBlockLength <= maxUncompressedBlockSize);
            Debug.Assert(numberOfBlocks <= compressedData.Length * 50 / maxUncompressedBlockSize);

            int blockStartLength = numberOfBlocks * sizeof(long);
            blockStarts = new byte[blockStartLength];
            compressedData.Seek(-blockStartLength - 8, SeekOrigin.End);
            compressedData.Read(blockStarts, 0, blockStartLength);
            compressedData.Position = origPosition;

            uncompressedStreamLength = (numberOfBlocks - 1) * (long)maxUncompressedBlockSize + lastBlockLength;
            Debug.Assert(GetCompressedPositionForBlock(0) == 0x1C);         // First block position is just skips past the header. 
        }

        // fields associated with the stream as a whole.
        Stream compressedData;              // The original stream (assumed to be compressed data)
        int maxUncompressedBlockSize;       // The uncompressed blockSize of all blocks except the last (which might be shorter) 
        byte[] blockStarts;                 // The blockStarts table, which allows random seeking (lazily inited)
        long uncompressedStreamLength;      // total blockSize of the uncompressed stream
        bool leaveOpen;

        // fields associated with the current position
        long uncompressedBlockStartPosition;    // uncompressed stream position beginning of the current block
        long nextCompressedBlockStartPosition;  // compresed stream position for the NEXT block 
        DeflateStream decompressor;         // The real stream2, we create a new one on each block. 
        int uncompressedBlockSize;          // The logical blockSize of the current uncompressed block.
        int uncompressedDataLeft;           // The number of bytes left in the current uncompressed block. 
        bool lastBlock;                     // True if this is the last block in the compressed stream.  


        class StreamCacheBuffer : StreamCache
        {
            public StreamCacheBuffer(Stream baseStream)
                : base(1024 * 16)
            {
                m_baseStream = baseStream;
            }
            public override void Fill(StreamCache.DataBlock block)
            {
                lock (this)
                {
                    if (m_curPostion != block.m_position)
                        m_baseStream.Position = block.m_position;
                    block.m_len = m_baseStream.Read(block.m_data, 0, block.m_data.Length);
                    m_curPostion = block.m_position + block.m_len;
                }
            }
            public override long Length { get { return m_baseStream.Length; } }

#region private
            long m_curPostion;
            Stream m_baseStream;
#endregion
        }

        class StreamCacheDecompressor : StreamCache
        {
            public StreamCacheDecompressor(StreamCache input, int blockSize)
                : base(blockSize)
            {
                m_input = input;
                m_maxWorkers = 1;       // TODO 
            }
            public override void Fill(StreamCache.DataBlock uncompressedBlockToReturn)
            {
                long compressedDataPos;
                int compressedDataSize;

                GetCompressedBlockInfo(uncompressedBlockToReturn.m_position, out compressedDataPos, out compressedDataSize);
                using (var compressedData = new StreamCacheStream(m_input, compressedDataPos, compressedDataSize))
                using (var uncompressedData = new DeflateStream(compressedData, CompressionMode.Decompress))
                    uncompressedBlockToReturn.m_len = uncompressedData.Read(uncompressedBlockToReturn.m_data, 0, uncompressedBlockToReturn.m_data.Length);

                uncompressedBlockToReturn.m_filled = true;
            }
            public override long Length { get { return m_input.Length; } }

            /// <summary>
            /// Given the desired uncompressed data position, return the file offset of the compressed data
            /// for that location, as well as the size of the compressed data.  
            /// </summary>
            private void GetCompressedBlockInfo(long uncompressedPosition, out long compressedDataPos, out int compressedDataSize)
            {
                long compressedBlockPosition = 0;       // TODO FIX NOW 

                compressedDataSize = ReadInt(compressedBlockPosition);
                compressedDataPos = compressedBlockPosition + 4;
            }
            /// <summary>
            /// Read in a single integer from the stream at 'position'. Because it might span blocks, this
            /// routine is not trivial.
            /// </summary>
            private int ReadInt(long position)
            {
                int ret = 0;
                DataBlock block = null;
                int positionInBlock = 0;
                for (int byteNum = 0; byteNum < 4; byteNum++)
                {
                    if (block == null)
                    {
                        block = m_input.GetBlock(position + byteNum);
                        if (block == null)
                            return 0;           // TODO throw exception instead?
                        positionInBlock = (int)(position - block.m_position);
                    }
                    ret = ret + (block.m_data[positionInBlock] << (byteNum * 8));
                    positionInBlock++;
                    if (positionInBlock >= block.m_len)
                        block = null;
                }
                return ret;
            }

            private StreamCache m_input;
        }

        abstract class StreamCache
        {
            protected StreamCache(int blockSize)
            {
                m_blocks = new DataBlock(blockSize, this, m_blocks);
                for (int i = 0; i < 3; i++)
                    m_blocks = new DataBlock(blockSize, this, m_blocks.m_next);
            }

            public abstract long Length { get; }
            public abstract void Fill(DataBlock block);
            public DataBlock GetBlock(long position)
            {
                DataBlock block;
                lock (this)
                {
                    block = FindBlockForPosition(position);
                    m_blocks = block;
                    block.m_refCount++; // TODO deal with overflow.  
                    if (m_curWorkers == 0 && m_maxWorkers > 0)
                    {
                        m_curWorkers++;
                        ThreadPool.QueueUserWorkItem(new WaitCallback(this.DoAyncWork));
                    }
                }
                if (block.m_beingFilled)
                    block.Wait();
                else
                {
                    Fill(block);
                    block.m_filled = true;
                }
                return block;
            }

            /// <summary>
            /// This is what a worker thread does.  Basically it tries to decompress more data 
            /// </summary>
            private void DoAyncWork(object state)
            {
                DataBlock block = null;
                for (; ; )
                {
                    lock (this)
                    {
                        block = GetDataBlockToFetch();
                        if (block == null)
                        {
                            --m_curWorkers;
                            return;
                        }
                        block.m_beingFilled = true;     // we are now filling the block, we own it now.  
                    }
                    Fill(block);                        // Do the main work outside the lock.  

                    block.m_beingFilled = false;        // we relinquish 'ownership' of the block
                    block.m_filled = true;              // and publish it as being filled.  
                }
            }

            /// <summary>
            /// Find the data block for stream position 'position'.  If it is already been fetched great!
            /// If not return a block that can be filled in (but has NOT been).  You can tell because
            /// block.m_filled == false in that case.  It is also possible that the block is being filled
            /// so you have to wait on it (outside the StreamCache lock), before returning it. 
            /// </summary>
            private DataBlock FindBlockForPosition(long position)
            {
                // We assume the StreamCache lock is held!
                DataBlock block = m_blocks;
                DataBlock freeBlock = null;
                for (; ; )
                {
                    if (block.m_position <= position && position < block.m_position + block.m_len)
                        return block;

                    if (freeBlock == null && block.m_refCount == 0 && !block.m_beingFilled)
                        freeBlock = block;

                    if (block == m_blocks)
                    {
                        // TODO, if we have not hit our concurrency limit, and it looks like we are streaming,
                        // add more workers, as the workers can't seem to keep up.  
                        if (freeBlock == null)
                            freeBlock = m_blocks.m_next = new DataBlock(m_blocks.m_data.Length, this, m_blocks.m_next);

                        Debug.Assert(!block.m_beingFilled && block.m_refCount == 0);
                        block = freeBlock;
                        block.Clear(position);
                        break;
                    }
                }
                return block;
            }

            /// <summary>
            /// This routine returns a block that we have determined is useful to prefetch.   If we can't
            /// find a good block to prefetch, we return null.  
            /// </summary>
            private DataBlock GetDataBlockToFetch()
            {
                // We assume the StreamCache lock is held!
                
                // Our heuristic is simple:  Just read ahead from the block at m_block 
                DataBlock block = m_blocks.m_next;
                long positionToPrefetch = m_blocks.m_position + m_blocks.m_len;
                long length = Length;
                for (; ; )
                {
                    // If we have not already done the readahead
                    if (block.m_position != positionToPrefetch)
                    {
                        if (positionToPrefetch >= length)       // past end of stream 
                            return null;

                        // It is not in use one way or the other 
                        if (block.m_refCount == 0 && !block.m_beingFilled)
                        {
                            // Then go ahead and use it to prefetch.  
                            block.Clear(positionToPrefetch);
                            return block;
                        }
                    }
                    else
                        positionToPrefetch = block.m_position + block.m_len;

                    if (block == m_blocks)
                        return null;
                }
            }

            public class DataBlock : IDisposable
            {
                internal DataBlock(int blockSize, StreamCache cache, DataBlock next)
                {
                    m_data = new byte[blockSize];
                    m_next = next;
                    m_cache = cache;

                }
                public void Dispose()
                {
                    lock (m_cache)
                    {
                        --m_refCount;
                    }
                }
                internal void Wait()
                {
                    while (!m_filled)
                        Thread.Sleep(1);
                }
                internal void Clear(long position)
                {
                    m_position = position;
                    m_filled = false;
                    m_len = m_data.Length;
                }

                internal byte[] m_data;                // data itself
                internal long m_position;              // position of data in the stream 
                internal int m_len;                    // logical length of the data. 

                internal DataBlock m_next;             // blocks are in a linked list in StreamCache
                internal volatile bool m_filled;       // set when finished filling. 
                internal volatile bool m_beingFilled;  // set async thread claims block to fill.  
                internal byte m_refCount;              // Incremented when GetBlock() is called, decremented on Dispose()
                internal StreamCache m_cache;          // cache that manages this block
            }

            DataBlock m_blocks;
            int m_curWorkers;
            protected int m_maxWorkers;
        }

        class StreamCacheStream : Stream
        {
            public StreamCacheStream(StreamCache cache, long start, long length)
            {
                m_start = start;
                m_length = length;
                m_cache = cache;
                Position = m_start;
            }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override long Seek(long offset, SeekOrigin origin)
            {
                if (origin == SeekOrigin.Begin)
                    Position = offset;
                else if (origin == SeekOrigin.Current)
                    Position = Position + offset;
                else if (origin == SeekOrigin.End)
                    Position = Length + offset;
                return Position;
            }
            public override long Length
            {
                get { return m_length; }
            }
            public override long Position
            {
                get
                {
                    return m_curBlock.m_position + m_positionInBlock;
                }
                set
                {
                    long newPos = value;
                    if (newPos < m_start)
                        newPos = m_start;
                    else if (newPos >= m_start + m_length)
                    {
                        newPos = m_start + m_length;
                        m_curBlock = null;
                        return;
                    }
                    if (m_curBlock != null)
                        m_curBlock.Dispose();
                    m_curBlock = m_cache.GetBlock(newPos);
                    if (m_curBlock != null)
                    {
                        m_positionInBlock = (int)(newPos - m_curBlock.m_position);
                        m_blockLength = m_curBlock.m_len;
                        long lengthLeft = m_length - newPos;
                        if (lengthLeft < m_blockLength)
                            m_blockLength = (int)lengthLeft;
                    }
                    else
                    {
                        m_positionInBlock = 0;
                        m_blockLength = 0;
                    }
                }
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (m_curBlock == null)
                    return 0;
                int curOffset = offset;
                int countLeft = count;
                for (; ; )
                {
                    int bytesLeft = m_blockLength - m_positionInBlock;
                    if (bytesLeft == 0)
                    {
                        // Setting the position updates m_curBlock, m_PositionInBlock and m_blockLength
                        Position = m_curBlock.m_position + m_positionInBlock;
                        if (m_curBlock == null)     // end of stream?
                            break;
                    }
                    int copySize = Math.Min(bytesLeft, countLeft);
                    Array.Copy(m_curBlock.m_data, m_positionInBlock, buffer, curOffset, copySize);
                    m_positionInBlock += copySize;
                    curOffset += copySize;
                    countLeft -= copySize;
                    if (countLeft == 0)
                        break;
                }
                return count - countLeft;
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (m_curBlock != null)
                    {
                        m_curBlock.Dispose();
                        m_curBlock = null;
                    }
                }
                base.Dispose(disposing);
            }

#region write methods (Not Implemented)
            public override bool CanWrite { get { return false; } }
            public override void SetLength(long value) { throw new NotImplementedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotImplementedException(); }
            public override void Flush() { }
#endregion
#region private
            StreamCache.DataBlock m_curBlock;   // Current chunk of data 
            int m_positionInBlock;
            int m_blockLength;                  // Logical end of the block
            long m_start;                       // logical start position of the stream in m_cache
            long m_length;                      // logical length of the stream in the m_cache
            StreamCache m_cache;                // represents all the data
#endregion
        }

#endregion
    }

#if UNIT_TESTS
    public static class CompressedStreamTests
    {
        public static void SizeTest(string inputFilePath)
        {
            Console.WriteLine("In size tests");
            for (int blockSize = 1024; blockSize <= 256 * 1024; blockSize *= 2)
            {
                string compressedFilePath = Path.ChangeExtension(inputFilePath, "." + blockSize.ToString() + ".compressed");
                using (Stream compressor = new CompressedWriteStream(File.Create(compressedFilePath), blockSize, false))
                    StreamUtilities.CopyFromFile(inputFilePath, compressor);

                double percent = 100.0 * (new FileInfo(compressedFilePath).Length) / (new FileInfo(inputFilePath).Length);
                Console.WriteLine("Blocksize " + blockSize.ToString().PadLeft(7) + 
                    " compression " + percent.ToString("f2") + "%. Placed in file " + compressedFilePath);
            }
        }
        public static void Tests()
        {
            string testOrig = "text.orig";
            string testCompressed = "text.compressed";

            for (int fileSize = 1023; fileSize <= 1025; fileSize++)
            {
                CreateDataFile(testOrig, fileSize);
                FileStream origData = File.OpenRead(testOrig);

                // Try writing in various block sizes
                // TODO more sizes?
                for (int i = 1; i < 256; i += 37)
                {
                    CompressedWriteStream compressor = new CompressedWriteStream(File.Create(testCompressed), 64, false);
                    origData.Position = 0;
                    CopyInChunks(origData, compressor, i);
                    compressor.Close();

                    CompressedReadStream decompressor = new CompressedReadStream(testCompressed);
                    origData.Position = 0;
                    Debug.Assert(CompareStreams(origData, decompressor, 1024 * 16));
                    decompressor.Close();
                }

                CompressedReadStream lengthTest = new CompressedReadStream(testCompressed);
                Debug.Assert(lengthTest.Length == origData.Length);
                lengthTest.Close();

                // Try reading back in various seek positions. 
                for (int blockSize = 20; blockSize < 300; blockSize += 47)
                {
                    CompressedReadStream decompressor = new CompressedReadStream(testCompressed);
                    for (int seekPosition = 0; seekPosition <= 1024; seekPosition += 16 * 3)
                        CompareStreams(origData, decompressor, blockSize, seekPosition);
                    decompressor.Close();
                }

                origData.Close();
            }
        }

        private static void CreateDataFile(string name, int length)
        {
            StreamWriter writer = File.CreateText(name);
            Random r = new Random(3454);

            string textLine = "The quick brown fox jumped over the lazy dog";
            int writtenBytes = 0;
            for (; ; )
            {
                if (writtenBytes + textLine.Length + 2 >= length)
                {
                    writer.WriteLine(textLine.Substring(0, length - writtenBytes - 2));
                    break;
                }
                int start = r.Next(textLine.Length - 3);
                int end = r.Next(start + 3, textLine.Length);
                writer.WriteLine(textLine.Substring(start, end - start));
                writtenBytes += (end - start + 2);
            }
            writer.Close();
        }
        private static bool FileCompare(string fileName1, string fileName2)
        {
            using (FileStream stream1 = File.OpenRead(fileName1))
            using (FileStream stream2 = File.OpenRead(fileName2))
                return CompareStreams(stream1, stream2, 1024);
        }
        private static void CompareStreams(Stream stream1, Stream stream2, int blockSize, int startPos)
        {
            stream1.Position = startPos;
            stream2.Position = startPos;
            CompareStreams(stream1, stream2, blockSize);
        }
        private static bool CompareStreams(Stream stream1, Stream stream2, int blockSize)
        {
            byte[] block1 = new byte[blockSize];
            byte[] block2 = new byte[blockSize];
            for (; ; )
            {
                Debug.Assert(stream1.Position == stream2.Position);
                int stream1Count = stream1.Read(block1, 0, blockSize);
                int stream2Count = stream2.Read(block2, 0, blockSize);
                if (stream1Count != stream2Count)
                {
                    Debug.Assert(false);
                    return false;
                }
                if (stream1Count == 0)
                    break;
                if (!ByteCompare(block1, 0, block2, 0, blockSize))
                    return false;
            }
            return true;
        }
        private static bool ByteCompare(byte[] buffer1, int offset1, byte[] buffer2, int offset2, int blockSize)
        {
            for (int i = 0; i < blockSize; i++)
                if (buffer1[i + offset1] != buffer2[i + offset2])
                {
                    Debug.Assert(false);
                    return false;
                }
            return true;
        }
        private static void CopyInChunks(Stream fromStream, Stream toStream, int blockSize)
        {
            byte[] block = new byte[blockSize];

            for (; ; )
            {
                int count = fromStream.Read(block, 0, blockSize);
                if (count == 0)
                    break;
                toStream.Write(block, 0, count);
            }
        }
    }
#endif
}

#endif // !NETSTANDARD1_3

#endif // false
