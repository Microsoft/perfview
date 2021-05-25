#if CROSS_GENERATION_LIVENESS
using Microsoft.Diagnostics.Runtime.Interop;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.CrossGenerationLiveness
{
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("27fe5639-8407-4f47-8364-ee118fb08ac8")]
    public interface IDebugClient
    {
        /* IDebugClient */

        [PreserveSig]
        int AttachKernel(
            [In] DEBUG_ATTACH Flags,
            [In, MarshalAs(UnmanagedType.LPStr)] string ConnectOptions);

        [PreserveSig]
        int GetKernelConnectionOptions(
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] Int32 BufferSize,
            [Out] out UInt32 OptionsSize);

        [PreserveSig]
        int SetKernelConnectionOptions(
            [In, MarshalAs(UnmanagedType.LPStr)] string Options);

        [PreserveSig]
        int StartProcessServer(
            [In] DEBUG_CLASS Flags,
            [In, MarshalAs(UnmanagedType.LPStr)] string Options,
            [In] IntPtr Reserved);

        [PreserveSig]
        int ConnectProcessServer(
            [In, MarshalAs(UnmanagedType.LPStr)] string RemoteOptions,
            [Out] out UInt64 Server);

        [PreserveSig]
        int DisconnectProcessServer(
            [In] UInt64 Server);

        [PreserveSig]
        int GetRunningProcessSystemIds(
            [In] UInt64 Server,
            [Out, MarshalAs(UnmanagedType.LPArray)] UInt32[] Ids,
            [In] UInt32 Count,
            [Out] out UInt32 ActualCount);

        [PreserveSig]
        int GetRunningProcessSystemIdByExecutableName(
            [In] UInt64 Server,
            [In, MarshalAs(UnmanagedType.LPStr)] string ExeName,
            [In] DEBUG_GET_PROC Flags,
            [Out] out UInt32 Id);

        [PreserveSig]
        int GetRunningProcessDescription(
            [In] UInt64 Server,
            [In] UInt32 SystemId,
            [In] DEBUG_PROC_DESC Flags,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder ExeName,
            [In] Int32 ExeNameSize,
            [Out] out UInt32 ActualExeNameSize,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Description,
            [In] Int32 DescriptionSize,
            [Out] out UInt32 ActualDescriptionSize);

        [PreserveSig]
        int AttachProcess(
            [In] UInt64 Server,
            [In] UInt32 ProcessID,
            [In] DEBUG_ATTACH AttachFlags);

        [PreserveSig]
        int CreateProcess(
            [In] UInt64 Server,
            [In, MarshalAs(UnmanagedType.LPStr)] string CommandLine,
            [In] DEBUG_CREATE_PROCESS Flags);

        [PreserveSig]
        int CreateProcessAndAttach(
            [In] UInt64 Server,
            [In, MarshalAs(UnmanagedType.LPStr)] string CommandLine,
            [In] DEBUG_CREATE_PROCESS Flags,
            [In] UInt32 ProcessId,
            [In] DEBUG_ATTACH AttachFlags);

        [PreserveSig]
        int GetProcessOptions(
            [Out] out DEBUG_PROCESS Options);

        [PreserveSig]
        int AddProcessOptions(
            [In] DEBUG_PROCESS Options);

        [PreserveSig]
        int RemoveProcessOptions(
            [In] DEBUG_PROCESS Options);

        [PreserveSig]
        int SetProcessOptions(
            [In] DEBUG_PROCESS Options);

        [PreserveSig]
        int OpenDumpFile(
            [In, MarshalAs(UnmanagedType.LPStr)] string DumpFile);

        [PreserveSig]
        int WriteDumpFile(
            [In, MarshalAs(UnmanagedType.LPStr)] string DumpFile,
            [In] DEBUG_DUMP Qualifier);

        [PreserveSig]
        int ConnectSession(
            [In] DEBUG_CONNECT_SESSION Flags,
            [In] UInt32 HistoryLimit);

        [PreserveSig]
        int StartServer(
            [In, MarshalAs(UnmanagedType.LPStr)] string Options);

        [PreserveSig]
        int OutputServer(
            [In] DEBUG_OUTCTL OutputControl,
            [In, MarshalAs(UnmanagedType.LPStr)] string Machine,
            [In] DEBUG_SERVERS Flags);

        [PreserveSig]
        int TerminateProcesses();

        [PreserveSig]
        int DetachProcesses();

        [PreserveSig]
        int EndSession(
            [In] DEBUG_END Flags);

        [PreserveSig]
        int GetExitCode(
            [Out] out UInt32 Code);

        [PreserveSig]
        int DispatchCallbacks(
            [In] UInt32 Timeout);

        [PreserveSig]
        int ExitDispatch(
            [In, MarshalAs(UnmanagedType.Interface)] IDebugClient Client);

        [PreserveSig]
        int CreateClient(
            [Out, MarshalAs(UnmanagedType.Interface)] out IDebugClient Client);

        [PreserveSig]
        int GetInputCallbacks(
            [Out, MarshalAs(UnmanagedType.Interface)] out IDebugInputCallbacks Callbacks);

        [PreserveSig]
        int SetInputCallbacks(
            [In, MarshalAs(UnmanagedType.Interface)] IDebugInputCallbacks Callbacks);

        /* GetOutputCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */

        [PreserveSig]
        int GetOutputCallbacks(
            [Out] out IntPtr Callbacks);

        [PreserveSig]
        int SetOutputCallbacks(
            [In] IDebugOutputCallbacks Callbacks);

        [PreserveSig]
        int GetOutputMask(
            [Out] out DEBUG_OUTPUT Mask);

        [PreserveSig]
        int SetOutputMask(
            [In] DEBUG_OUTPUT Mask);

        [PreserveSig]
        int GetOtherOutputMask(
            [In, MarshalAs(UnmanagedType.Interface)] IDebugClient Client,
            [Out] out DEBUG_OUTPUT Mask);

        [PreserveSig]
        int SetOtherOutputMask(
            [In, MarshalAs(UnmanagedType.Interface)] IDebugClient Client,
            [In] DEBUG_OUTPUT Mask);

        [PreserveSig]
        int GetOutputWidth(
            [Out] out UInt32 Columns);

        [PreserveSig]
        int SetOutputWidth(
            [In] UInt32 Columns);

        [PreserveSig]
        int GetOutputLinePrefix(
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] Int32 BufferSize,
            [Out] out UInt32 PrefixSize);

        [PreserveSig]
        int SetOutputLinePrefix(
            [In, MarshalAs(UnmanagedType.LPStr)] string Prefix);

        [PreserveSig]
        int GetIdentity(
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] Int32 BufferSize,
            [Out] out UInt32 IdentitySize);

        [PreserveSig]
        int OutputIdentity(
            [In] DEBUG_OUTCTL OutputControl,
            [In] UInt32 Flags,
            [In, MarshalAs(UnmanagedType.LPStr)] string Format);

        /* GetEventCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */

        [PreserveSig]
        int GetEventCallbacks(
            [Out] out IntPtr Callbacks);

        [PreserveSig]
        int SetEventCallbacks(
            [In] IDebugEventCallbacks Callbacks);

        [PreserveSig]
        int FlushCallbacks();
    }
}
#endif
