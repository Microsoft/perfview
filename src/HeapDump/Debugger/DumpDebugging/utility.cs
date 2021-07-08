//---------------------------------------------------------------------
//  This file is part of the CLR Managed Debugger (mdbg) Sample.
// 
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//---------------------------------------------------------------------
using Microsoft.Samples.Debugging.CorDebug.NativeApi;
using Microsoft.Samples.Debugging.MetaDataLocator;
using Microsoft.Samples.Debugging.Native;
using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace Microsoft.Samples.Debugging.CorDebug.Utility
{
    #region Dump Data Target
    /// <summary>
    /// Data target implementation for a dump file
    /// </summary>
    public sealed class DumpDataTarget : ICorDebugDataTarget, ICorDebugMetaDataLocator, IDisposable
    {
        private DumpReader m_reader;
        private CorDebugMetaDataLocator m_metaDataLocator;

        /// <summary>
        /// Constructor a Dump Target around an existing DumpReader.
        /// </summary>
        /// <param name="reader"></param>
        public DumpDataTarget(DumpReader reader)
        {
            m_reader = reader;
            string s = ".\\";

            try
            {
                // For our trivial implementation, try looking in the CLR directory
                DumpModule dm = m_reader.LookupModule("clr.dll");
                s = dm.FullName;
                if (s.LastIndexOf('\\') != -1)
                {
                    s = s.Substring(0, s.LastIndexOf('\\'));
                }
            }
            catch (DumpMissingDataException)
            {
            }

            m_metaDataLocator = new CorDebugMetaDataLocator(s);
        }

        /// <summary>
        /// Construct a dump target around the dump file.
        /// </summary>
        /// <param name="fileName"></param>
        public DumpDataTarget(string fileName)
        {
            m_reader = new Microsoft.Samples.Debugging.Native.DumpReader(fileName);
        }

        /// <summary>
        /// Dispose method.
        /// </summary>
        public void Dispose()
        {
            if (m_reader != null)
            {
                m_reader.Dispose();
            }
        }

        #region ICorDebugDataTarget Members

        /// <summary>
        /// Implementation of ICorDebugDataTarget.GetPlatform
        /// </summary>
        /// <returns>platform that the process in this dump was executing on</returns>
        public CorDebugPlatform GetPlatform()
        {
            // Infer platform based off CPU architecture
            // At the moment we only support windows.
            ProcessorArchitecture p = m_reader.ProcessorArchitecture;

            switch (p)
            {
                case ProcessorArchitecture.PROCESSOR_ARCHITECTURE_IA32_ON_WIN64:
                case ProcessorArchitecture.PROCESSOR_ARCHITECTURE_INTEL:
                    return CorDebugPlatform.CORDB_PLATFORM_WINDOWS_X86;
                case ProcessorArchitecture.PROCESSOR_ARCHITECTURE_IA64:
                    return CorDebugPlatform.CORDB_PLATFORM_WINDOWS_IA64;
                case ProcessorArchitecture.PROCESSOR_ARCHITECTURE_AMD64:
                    return CorDebugPlatform.CORDB_PLATFORM_WINDOWS_AMD64;
                case ProcessorArchitecture.PROCESSOR_ARCHITECTURE_ARM:
                    return CorDebugPlatform.CORDB_PLATFORM_WINDOWS_ARM;
            }

            throw new InvalidOperationException("Unrecognized target architecture " + p);
        }

        // Implementation of ICorDebugDataTarget.ReadVirtual
        public uint ReadVirtual(ulong address, IntPtr buffer, uint bytesRequested)
        {
            uint result = m_reader.ReadPartialMemory(address, buffer, bytesRequested);
            byte[] data = new byte[bytesRequested];
            Marshal.Copy(buffer, data, 0, (int)bytesRequested);
            return result;
        }

        // Implementation of ICorDebugDataTarget.GetThreadContext
        public void GetThreadContext(uint threadId, uint contextFlags, uint contextSize, IntPtr context)
        {
            // Ignore contextFlags because this will retrieve everything. 
            m_reader.GetThread((int)threadId).GetThreadContext(context, (int)contextSize);
        }

        #endregion

        #region ICorDebugMetaDataLocator Members

        // Implementation of ICorDebugMetaDataLocator.GetMetaData
        public void GetMetaData(string imagePath,
                            uint dwImageTimeStamp,
                            uint dwImageSize,
                            uint cchPathBuffer,
                            out uint pcchPathBuffer,
                            char[] wszPathBuffer)
        {
            m_metaDataLocator.GetMetaData(imagePath, dwImageTimeStamp, dwImageSize, cchPathBuffer, out pcchPathBuffer, wszPathBuffer);
        }

        #endregion
    }
    #endregion


    /// <summary>
    /// Utility class to wrap an unmanaged DLL and be responsible for freeing it.
    /// </summary>
    /// <remarks>This is a managed wrapper over the native LoadLibrary, GetProcAddress, and
    /// FreeLibrary calls.
    /// <list>
    /// <item>It wraps the raw pinvokes, and uses exceptions for error handling.</item>
    /// <item>It provides type-safe delegate wrappers over GetProcAddress</item>
    /// <item>It uses SafeHandles for the unmanaged resources</item>
    /// </list>
    /// You must be very careful to not call FreeLibrary (Dispose) until you are done with the
    /// library. If you call GetProcAddress after the library is freed, it will crash. 
    /// </remarks>
    public sealed class UnmanagedLibraryLeak
    {
        #region Safe Handles and Native imports
        // See http://msdn.microsoft.com/msdnmag/issues/05/10/Reliability/ for more about safe handles.
        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        private sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeLibraryHandle() : base(true) { }

            protected override bool ReleaseHandle()
            {
                // <strip> DevDiv bugs 55228 is tracking this problem </strip>
                // Since we can't safely Free the library yet, we don't. Once we can do this
                // safely, then this is the spot to call kernel32!FreeLibrary.
                return true;
            }
        }

        private static class NativeMethods
        {
            private const string s_kernel = "kernel32";
            [DllImport(s_kernel, CharSet = CharSet.Auto, BestFitMapping = false, SetLastError = true)]
            public static extern SafeLibraryHandle LoadLibrary(string fileName);

            // <strip> DevDiv bugs 55228 is tracking this problem </strip>
            // Bug: freeing the libray is too dangerous because we must first ensure that all
            // objects implemented in the dll have been cleaned up. Need to establish a good
            // pattern here, and then we can implement freeing.
            // 
            //[ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            //[DllImport(s_kernel, SetLastError = true)]
            //[return: MarshalAs(UnmanagedType.Bool)]
            //public static extern bool FreeLibrary(IntPtr hModule);

            [DllImport(s_kernel)]
            public static extern IntPtr GetProcAddress(SafeLibraryHandle hModule, String procname);
        }
        #endregion // Safe Handles and Native imports

        /// <summary>
        /// Constructor to load a dll and be responible for freeing it.
        /// </summary>
        /// <param name="fileName">full path name of dll to load</param>
        /// <exception cref="System.IO.FileNotFoundException">if fileName can't be found</exception>
        /// <remarks>Throws exceptions on failure. Most common failure would be file-not-found, or
        /// that the file is not a  loadable image.</remarks>
        public UnmanagedLibraryLeak(string fileName)
        {
            m_hLibrary = NativeMethods.LoadLibrary(fileName);
            if (m_hLibrary.IsInvalid)
            {
                int hr = Marshal.GetHRForLastWin32Error();
                Marshal.ThrowExceptionForHR(hr);
            }
        }

        /// <summary>
        /// Dynamically lookup a function in the dll via kernel32!GetProcAddress.
        /// </summary>
        /// <param name="functionName">raw name of the function in the export table.</param>
        /// <returns>null if function is not found. Else a delegate to the unmanaged function.
        /// </returns>
        /// <remarks>GetProcAddress results are valid as long as the dll is not yet unloaded. This
        /// is very very dangerous to use since you need to ensure that the dll is not unloaded
        /// until after you're done with any objects implemented by the dll. For example, if you
        /// get a delegate that then gets an IUnknown implemented by this dll,
        /// you can not dispose this library until that IUnknown is collected. Else, you may free
        /// the library and then the CLR may call release on that IUnknown and it will crash.</remarks>
        public TDelegate GetUnmanagedFunction<TDelegate>(string functionName) where TDelegate : class
        {
            IntPtr p = NativeMethods.GetProcAddress(m_hLibrary, functionName);

            // Failure is a common case, especially for adaptive code.
            if (p == IntPtr.Zero)
            {
                return null;
            }
            Delegate function = Marshal.GetDelegateForFunctionPointer(p, typeof(TDelegate));

            // Ideally, we'd just make the constraint on TDelegate be
            // System.Delegate, but compiler error CS0702 (constrained can't be System.Delegate)
            // prevents that. So we make the constraint system.object and do the cast from object-->TDelegate.
            object o = function;

            return (TDelegate)o;
        }

        // Unmanaged resource.
        private SafeLibraryHandle m_hLibrary;

    } // UnmanagedLibrary

} // Microsoft.Samples.Debugging.CorDebug.Utility
