//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
#if false  // This code is currently unused, commented out.  
using System;
using System.Diagnostics;
using System.Text;      // For StringBuilder.
using System.Threading;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using DeferedStreamLabel = FastSerialization.StreamLabel;

#if !NETSTANDARD1_3
using System.Runtime.CompilerServices;
#endif

namespace FastSerialization
{
    public class MemoryMappedFileStreamWriter : IStreamWriter
    {
        private const int ERROR_NOT_ENOUGH_MEMORY = unchecked((int)0x80070008);

        internal const long PageSize = 64 * 1024;
        internal const long InitialCapacity = 64 * 1024;
        internal const int BlockCopyCapacity = 10 * 1024 * 1024;

        private MemoryMappedFile _file;
        private string _mapName;
        private long _fileCapacity;

        private MemoryMappedViewAccessor _view;
        private long _viewOffset;
        private int _capacity;
        private int _offset;

        public MemoryMappedFileStreamWriter(long initialCapacity = InitialCapacity)
        {
            long subPageSize = initialCapacity % PageSize;
            if (subPageSize != 0)
            {
                initialCapacity += PageSize - subPageSize;
            }

            _mapName = Guid.NewGuid().ToString("N");
            _file = MemoryMappedFile.CreateNew(_mapName, InitialCapacity);
            _fileCapacity = InitialCapacity;

            _capacity = (int)Math.Min(_fileCapacity, BlockCopyCapacity);
            try
            {
                _view = File.CreateViewAccessor(0, _capacity, MemoryMappedFileAccess.Write);
            }
            catch (IOException e) when (e.HResult == ERROR_NOT_ENOUGH_MEMORY)
            {
                throw new OutOfMemoryException(e.Message, e);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected MemoryMappedFile File
        {
            get
            {
                var result = _file;
                if (result == null)
                {
                    throw new ObjectDisposedException(nameof(MemoryMappedFileStreamWriter));
                }

                return result;
            }
        }

        public MemoryMappedFileStreamReader GetReader()
        {
            return new MemoryMappedFileStreamReader(_mapName, Length);
        }

        public void Clear()
        {
            if (_viewOffset == 0 && _offset == 0)
            {
                return;
            }

            _view.Dispose();

            _capacity = (int)Math.Min(_fileCapacity, BlockCopyCapacity);
            try
            {
                _view = File.CreateViewAccessor(0, _capacity, MemoryMappedFileAccess.Write);
            }
            catch (IOException e) when (e.HResult == ERROR_NOT_ENOUGH_MEMORY)
            {
                throw new OutOfMemoryException(e.Message, e);
            }

            _viewOffset = 0;
            _offset = 0;
        }

        public long Length
            => _viewOffset + _offset;

        public DeferedStreamLabel GetLabel(bool allowPadding)
        {
            if ((Length & 0x1) != 0)
            {
                if (!allowPadding)
                    throw new NotSupportedException("Labels must be aligned to a 2-byte boundary.");

                Write((byte)Tags.Padding);
                Debug.Assert((Length & 0x1) == 0);
            }

            return checked((DeferedStreamLabel)Length);
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

            if (_offset + length > _capacity)
            {
                Resize(length);
            }

#if NETSTANDARD1_3

            for (int i = 0; i < length; i++)
            {
                _view.Write(_offset + i, data[offset + i]);
            }
#else
            _view.WriteArray(_offset, data, offset, length);
#endif
            _offset += length;
        }

        public void Write(byte value)
        {
            if (_offset + sizeof(byte) > _capacity)
            {
                Resize(sizeof(byte));
            }

            _view.Write(_offset, value);
            _offset += sizeof(byte);
        }

        public void Write(short value)
        {
            if (_offset + sizeof(short) > _capacity)
            {
                Resize(sizeof(short));
            }

            _view.Write(_offset, value);
            _offset += sizeof(short);
        }

        public void Write(int value)
        {
            if (_offset + sizeof(int) > _capacity)
            {
                Resize(sizeof(int));
            }

            _view.Write(_offset, value);
            _offset += sizeof(int);
        }

        public void Write(long value)
        {
            if (_offset + sizeof(long) > _capacity)
            {
                Resize(sizeof(long));
            }

            _view.Write(_offset, value);
            _offset += sizeof(long);
        }

        public void Write(DeferedStreamLabel value)
        {
            if (((long)value & 0x1) != 0)
                throw new NotSupportedException("Labels must be aligned to a 2-byte boundary.");

            Write((int)value);
        }

        public unsafe void Write(string value)
        {
            if (value == null)
            {
                Write(-1);
            }
            else
            {
                Write(value.Length);

                fixed (char* chars = value)
                {
                    byte* pointer = null;

#if !NETSTANDARD1_3
                    RuntimeHelpers.PrepareConstrainedRegions();
#endif
                    try
                    {
                        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                        Encoder encoder = Encoding.UTF8.GetEncoder();

                        int charsUsed;
                        int bytesUsed;
                        bool completed;
#if NETSTANDARD1_3
                        char[] charArray = value.ToCharArray();
                        byte[] bytes = new byte[_capacity - _offset];
                        encoder.Convert(charArray, 0, charArray.Length, bytes, 0, bytes.Length, false, out charsUsed, out bytesUsed, out completed);
                        Marshal.Copy(bytes, 0, (IntPtr)pointer, bytesUsed);
#else
                        encoder.Convert(chars, value.Length, pointer, _capacity - _offset, false, out charsUsed, out bytesUsed, out completed);
#endif
                        _offset += bytesUsed;

                        if (!completed)
                        {
                            Resize(Encoding.UTF8.GetMaxByteCount(value.Length - charsUsed));

                            int finalCharsUsed;
                            int finalBytesUsed;
#if NETSTANDARD1_3
                            bytes = new byte[_capacity - _offset];
                            encoder.Convert(charArray, charsUsed, charArray.Length - charsUsed, bytes, 0, bytes.Length, true, out finalCharsUsed, out finalBytesUsed, out completed);
                            Marshal.Copy(bytes, 0, (IntPtr)(pointer + bytesUsed), finalBytesUsed);
#else
                            encoder.Convert(chars + charsUsed, value.Length - charsUsed, pointer + bytesUsed, _capacity - _offset, true, out finalCharsUsed, out finalBytesUsed, out completed);
#endif
                        }
                    }
                    finally
                    {
                        if (pointer != null)
                        {
                            _view.SafeMemoryMappedViewHandle.ReleasePointer();
                        }
                    }
                }
            }
        }

        public void WriteSuffixLabel(DeferedStreamLabel value)
        {
            // This is guaranteed to be uncompressed, but since we are not compressing anything, we can
            // simply write the value.  
            Write(value);
        }

        protected void Resize(int capacity)
        {
            // See if we can do nothing
            int available = _capacity - _offset;
            if (available >= capacity)
            {
                return;
            }

            // We can no longer use the current view, so go ahead and dispose of it
            _view.Dispose();

            // See if we need to resize the underlying file
            long availableInFile = _fileCapacity - _viewOffset - _offset;
            if (availableInFile < capacity)
            {
                long minimumFileSize = _fileCapacity - availableInFile + capacity;
                long newFileSize = _fileCapacity;
                while (newFileSize < minimumFileSize)
                {
                    newFileSize = (newFileSize / 2) * 3;
                }

                var newMapName = Guid.NewGuid().ToString("N");
                var newFile = MemoryMappedFile.CreateNew(newMapName, newFileSize);
                long currentCopyOffset = 0;
                long remainingSizeToCopy = _fileCapacity - availableInFile;
                while (remainingSizeToCopy > 0)
                {
                    int chunkSize = (int)Math.Min(BlockCopyCapacity, remainingSizeToCopy);

                    try
                    {
                        using (var readStream = _file.CreateViewStream(currentCopyOffset, chunkSize, MemoryMappedFileAccess.Read))
                        using (var writeStream = newFile.CreateViewStream(currentCopyOffset, chunkSize, MemoryMappedFileAccess.Write))
                        {
                            readStream.CopyTo(writeStream, chunkSize);
                        }
                    }
                    catch (IOException e) when (e.HResult == ERROR_NOT_ENOUGH_MEMORY)
                    {
                        throw new OutOfMemoryException(e.Message, e);
                    }

                    currentCopyOffset += chunkSize;
                    remainingSizeToCopy -= chunkSize;
                }

                _file.Dispose();
                _file = newFile;
                _mapName = newMapName;
                availableInFile += newFileSize - _fileCapacity;
                _fileCapacity = newFileSize;
            }

            long viewOffset = (_viewOffset + _offset) & ~0xFFFF;
            long offset = (_viewOffset + _offset) - viewOffset;
            long viewLength = Math.Max(Math.Min(BlockCopyCapacity, availableInFile + offset), capacity + offset);
            try
            {
                _view = _file.CreateViewAccessor(viewOffset, viewLength, MemoryMappedFileAccess.Write);
            }
            catch (IOException e) when (e.HResult == ERROR_NOT_ENOUGH_MEMORY)
            {
                throw new OutOfMemoryException(e.Message, e);
            }

            _viewOffset = viewOffset;
            _capacity = (int)viewLength;
            _offset = (int)offset;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Exchange(ref _view, null)?.Dispose();
                Interlocked.Exchange(ref _file, null)?.Dispose();
            }
        }
    }
}
#endif
