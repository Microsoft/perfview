//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.IO;
using System.Text;      // For StringBuilder.
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace FastSerialization
{
    public class MemoryMappedFileStreamReader : IStreamReader
    {
        private MemoryMappedFile _file;
        private long _fileLength;
        private bool _leaveOpen;

        private MemoryMappedViewAccessor _view;
        private IntPtr _viewAddress;
        private long _viewOffset;
        private long _capacity;
        private long _offset;

        public MemoryMappedFileStreamReader(string mapName, long length)
            : this(MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read), length, leaveOpen: false)
        {
        }

        public MemoryMappedFileStreamReader(MemoryMappedFile file, long length, bool leaveOpen)
        {
            _file = file;
            _fileLength = length;
            _leaveOpen = leaveOpen;

            if (IntPtr.Size == 4)
            {
                _capacity = Math.Min(_fileLength, MemoryMappedFileStreamWriter.BlockCopyCapacity);
            }
            else
            {
                _capacity = _fileLength;
            }

            _view = File.CreateViewAccessor(0, _capacity, MemoryMappedFileAccess.Read);
            _viewAddress = _view.SafeMemoryMappedViewHandle.DangerousGetHandle();
        }

        public static MemoryMappedFileStreamReader CreateFromFile(string path)
        {
            long capacity = new FileInfo(path).Length;
            MemoryMappedFile file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, Guid.NewGuid().ToString("N"), capacity, MemoryMappedFileAccess.Read);
            return new MemoryMappedFileStreamReader(file, capacity, leaveOpen: false);
        }

        public StreamLabel Current
        {
            get
            {
                return checked((StreamLabel)(_viewOffset + _offset));
            }
        }

        public long Length => _fileLength;

        protected MemoryMappedFile File
        {
            get
            {
                var result = _file;
                if (result == null)
                {
                    throw new ObjectDisposedException(nameof(MemoryMappedFileStreamReader));
                }

                return result;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Seek(long offset)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            // see if we can just change the offset
            if (offset >= _viewOffset && offset < _viewOffset + _capacity)
            {
                _offset = offset - _viewOffset;
                return;
            }

            // Have to move the view
            _view.Dispose();
            long availableInFile = _fileLength - offset;
            long viewOffset = offset & ~0xFFFF;
            long offsetInView = offset - viewOffset;
            long viewLength = Math.Min(MemoryMappedFileStreamWriter.BlockCopyCapacity, availableInFile + offsetInView);
            _view = _file.CreateViewAccessor(viewOffset, viewLength, MemoryMappedFileAccess.Read);
            _viewAddress = _view.SafeMemoryMappedViewHandle.DangerousGetHandle();
            _viewOffset = viewOffset;
            _capacity = viewLength;
            _offset = offsetInView;
        }

        public void Goto(StreamLabel label)
        {
            if (label < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(label));
            }

            // see if we can just change the offset
            long absoluteOffset = (long)label;
            if (absoluteOffset >= _viewOffset && absoluteOffset < _viewOffset + _capacity)
            {
                _offset = absoluteOffset - _viewOffset;
                return;
            }

            // Have to move the view
            _view.Dispose();
            long availableInFile = _fileLength - absoluteOffset;
            long viewOffset = absoluteOffset & ~0xFFFF;
            long offset = absoluteOffset - viewOffset;
            long viewLength = Math.Min(MemoryMappedFileStreamWriter.BlockCopyCapacity, availableInFile + offset);
            _view = _file.CreateViewAccessor(viewOffset, viewLength, MemoryMappedFileAccess.Read);
            _viewAddress = _view.SafeMemoryMappedViewHandle.DangerousGetHandle();
            _viewOffset = viewOffset;
            _capacity = viewLength;
            _offset = offset;
        }

        public void GotoSuffixLabel()
        {
            const int sizeOfSerializedStreamLabel = 4;
            Goto((StreamLabel)(Length - sizeOfSerializedStreamLabel));
            Goto(ReadLabel());
        }

        public unsafe void Read(byte[] data, int offset, int length)
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

            Marshal.Copy((IntPtr)((byte*)_viewAddress + _offset), data, 0, length);
            _offset += length;
        }

        public T Read<T>()
            where T : struct
        {
#if NET45
            int size = Marshal.SizeOf(typeof(T));
#else
            int size = Marshal.SizeOf<T>();
#endif
            if (_offset + size > _capacity)
            {
                Resize(size);
            }

            T result;

#if NETSTANDARD1_6
            byte[] rawData = new byte[size];
            Read(rawData, 0, size);
            unsafe
            {
                fixed (byte* rawDataPtr = rawData)
                {
                    result = Marshal.PtrToStructure<T>((IntPtr)rawDataPtr);
                }
            }
#else
            _view.Read(_offset, out result);
#endif

            _offset += size;
            return result;
        }

        public unsafe byte ReadByte()
        {
            if (_offset + sizeof(byte) > _capacity)
            {
                Resize(sizeof(byte));
            }

            var result = *((byte*)_viewAddress + _offset);
            _offset += sizeof(byte);
            return result;
        }

        public unsafe short ReadInt16()
        {
            if (_offset + sizeof(short) > _capacity)
            {
                Resize(sizeof(short));
            }

            var result = *(short*)((byte*)_viewAddress + _offset);
            _offset += sizeof(short);
            return result;
        }

        public unsafe int ReadInt32()
        {
            if (_offset + sizeof(int) > _capacity)
            {
                Resize(sizeof(int));
            }

            var result = *(int*)((byte*)_viewAddress + _offset);
            _offset += sizeof(int);
            return result;
        }

        public unsafe long ReadInt64()
        {
            if (_offset + sizeof(long) > _capacity)
            {
                Resize(sizeof(long));
            }

            var result = *(long*)((byte*)_viewAddress + _offset);
            _offset += sizeof(long);
            return result;
        }

        public StreamLabel ReadLabel()
        {
            return (StreamLabel)((ulong)unchecked((uint)ReadInt32()) << 1);
        }

        public unsafe string ReadString()
        {
            int charCount = ReadInt32();
            if (charCount == -1)
            {
                return null;
            }

            string result = new string('\0', charCount);
            fixed (char* chars = result)
            {
                Decoder decoder = Encoding.UTF8.GetDecoder();

                int bytesUsed;
                int charsUsed;
                bool completed;
#if NETSTANDARD1_6
                byte[] bytes = new byte[(int)Math.Min(int.MaxValue - 50, _capacity - _offset)];
                Marshal.Copy(_viewAddress, bytes, 0, bytes.Length);
                char[] charArray = new char[charCount];
                decoder.Convert(bytes, 0, bytes.Length, charArray, 0, charArray.Length, false, out bytesUsed, out charsUsed, out completed);
                Marshal.Copy(charArray, 0, (IntPtr)chars, charsUsed * sizeof(char));
#else
                decoder.Convert((byte*)_viewAddress, (int)Math.Min(int.MaxValue - 50, _capacity - _offset), chars, charCount, false, out bytesUsed, out charsUsed, out completed);
#endif
                _offset += bytesUsed;

                if (!completed)
                {
                    long availableInFile = _fileLength - _viewOffset - _offset;
                    Resize(checked((int)Math.Min(availableInFile, Encoding.UTF8.GetMaxByteCount(charCount - charsUsed))));

                    int finalBytesUsed;
                    int finalCharsUsed;
#if NETSTANDARD1_6
                    bytes = new byte[(int)Math.Min(int.MaxValue - 50, _capacity - _offset)];
                    Marshal.Copy(_viewAddress + bytesUsed, bytes, 0, bytes.Length);
                    charArray = new char[charCount - charsUsed];
                    decoder.Convert(bytes, 0, bytes.Length, charArray, 0, charArray.Length, true, out finalBytesUsed, out finalCharsUsed, out completed);
                    Marshal.Copy(charArray, 0, (IntPtr)(chars + charsUsed), finalCharsUsed * sizeof(char));
#else
                    decoder.Convert((byte*)_viewAddress + bytesUsed, (int)Math.Min(int.MaxValue - 50, _capacity - _offset), chars + charsUsed, charCount - charsUsed, true, out finalBytesUsed, out finalCharsUsed, out completed);
#endif
                }
            }

            return result;
        }

        protected void Resize(int capacity)
        {
            // See if we can do nothing
            long available = _capacity - _offset;
            if (available >= capacity)
            {
                return;
            }

            // We can no longer use the current view, so go ahead and dispose of it
            _view.Dispose();

            // See if the underlying file is large enough
            long availableInFile = _fileLength - _viewOffset - _offset;
            if (availableInFile < capacity)
            {
                throw new InvalidOperationException("Cannot create a view outside the bounds of the file.");
            }

            long viewOffset = (_viewOffset + _offset) & ~0xFFFF;
            long offset = (_viewOffset + _offset) - viewOffset;
            long viewLength = Math.Max(Math.Min(MemoryMappedFileStreamWriter.BlockCopyCapacity, availableInFile + offset), capacity + offset);
            _view = _file.CreateViewAccessor(viewOffset, viewLength, MemoryMappedFileAccess.Read);
            _viewAddress = _view.SafeMemoryMappedViewHandle.DangerousGetHandle();
            _viewOffset = viewOffset;
            _capacity = viewLength;
            _offset = offset;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _viewAddress = IntPtr.Zero;

                Interlocked.Exchange(ref _view, null)?.Dispose();

                var file = Interlocked.Exchange(ref _file, null);
                if (!_leaveOpen)
                {
                    file?.Dispose();
                }
            }
        }
    }
}
