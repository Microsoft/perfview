//---------------------------------------------------------------------
//  This file is part of the CLR Managed Debugger (mdbg) Sample.
// 
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//
// Part of managed wrappers for native debugging APIs.
// NativeImports.cs: raw definitions of native methods and structures 
//  for native debugging API.
//  Also includes some useful utility methods.
//---------------------------------------------------------------------


using Microsoft.Samples.Debugging.NativeApi;
using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Permissions;

// Native structures used for the implementation of the pipeline.
namespace Microsoft.Samples.Debugging.NativeApi
{
    #region Structures for CreateProcess
    [StructLayout(LayoutKind.Sequential, Pack = 8), ComVisible(false)]
    public class PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
        public PROCESS_INFORMATION() { }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8), ComVisible(false)]
    public class SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
        public SECURITY_ATTRIBUTES() { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 8), ComVisible(false)]
    public class STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public SafeFileHandle hStdInput;
        public SafeFileHandle hStdOutput;
        public SafeFileHandle hStdError;
        public STARTUPINFO()
        {
            // Initialize size field.
            cb = Marshal.SizeOf(this);

            // initialize safe handles 
            hStdInput = new Microsoft.Win32.SafeHandles.SafeFileHandle(new IntPtr(0), false);
            hStdOutput = new Microsoft.Win32.SafeHandles.SafeFileHandle(new IntPtr(0), false);
            hStdError = new Microsoft.Win32.SafeHandles.SafeFileHandle(new IntPtr(0), false);
        }
    }

    #endregion // Structures for CreateProcess

} // Microsoft.Samples.Debugging.CorDebug.NativeApi

namespace Microsoft.Samples.Debugging.Native
{
    #region Interfaces

    /// <summary>
    /// Thrown when failing to read memory from a target.
    /// </summary>
    [Serializable()]
    public class ReadMemoryFailureException : InvalidOperationException
    {
        /// <summary>
        /// Initialize a new exception
        /// </summary>
        /// <param name="address">address where read failed</param>
        /// <param name="countBytes">size of read attempted</param>
        public ReadMemoryFailureException(IntPtr address, int countBytes)
            : base(MessageHelper(address, countBytes))
        {
        }

        public ReadMemoryFailureException(IntPtr address, int countBytes, Exception innerException)
            : base(MessageHelper(address, countBytes), innerException)
        {
        }

        // Internal helper to get the message string for the ctor.
        private static string MessageHelper(IntPtr address, int countBytes)
        {
            return String.Format("Failed to read memory at 0x" + address.ToString("x") + " of " + countBytes + " bytes.");
        }

        #region Standard Ctors
        /// <summary>
        /// Initializes a new instance of the ReadMemoryFailureException.
        /// </summary>
        public ReadMemoryFailureException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the ReadMemoryFailureException with the specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ReadMemoryFailureException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ReadMemoryFailureException with the specified error message and inner Exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ReadMemoryFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ReadMemoryFailureException class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected ReadMemoryFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
        #endregion
    }

    // <strip>This may evolve into a data-target</strip>
    /// <summary>
    /// Interface to provide access to target
    /// </summary>
    public interface IMemoryReader
    {
        /// <summary>
        /// Read memory from the target process. Either reads all memory or throws.
        /// </summary>
        /// <param name="address">target address to read memory from</param>
        /// <param name="buffer">buffer to fill with memory</param>
        /// <exception cref="ReadMemoryFailureException">Throws if can't read all the memory</exception>
        void ReadMemory(IntPtr address, byte[] buffer);
    }
    #endregion


    #region Native Structures

    /// <summary>
    /// Platform agnostic flags used to extract platform-specific context flag values
    /// </summary>
    [Flags]
    public enum AgnosticContextFlags : int
    {
        //using a seperate bit for each flag will allow logical operations of flags
        // i.e  ContextControl | ContextInteger | ContextSegments, etc
        ContextControl = 0x1,
        ContextInteger = 0x2,
        ContextFloatingPoint = 0x4,
        ContextDebugRegisters = 0x10,  //on IA64, this will be equivalent to ContextDebug
        ContextAll = 0x3F,
        None = 0x0
    }

    [Flags]
    public enum ContextFlags
    {
        None = 0,
        X86Context = 0x10000,
        X86ContextControl = X86Context | 0x1,
        X86ContextInteger = X86Context | 0x2,
        X86ContextSegments = X86Context | 0x4,
        X86ContextFloatingPoint = X86Context | 0x8,
        X86ContextDebugRegisters = X86Context | 0x10,
        X86ContextExtendedRegisters = X86Context | 0x20,
        X86ContextFull = X86Context | X86ContextControl | X86ContextInteger | X86ContextSegments,
        X86ContextAll = X86Context | X86ContextControl | X86ContextInteger | X86ContextSegments | X86ContextFloatingPoint |
                          X86ContextDebugRegisters | X86ContextExtendedRegisters,

        AMD64Context = 0x100000,
        AMD64ContextControl = AMD64Context | 0x1,
        AMD64ContextInteger = AMD64Context | 0x2,
        AMD64ContextSegments = AMD64Context | 0x4,
        AMD64ContextFloatingPoint = AMD64Context | 0x8,
        AMD64ContextDebugRegisters = AMD64Context | 0x10,
        AMD64ContextFull = AMD64Context | AMD64ContextControl | AMD64ContextInteger | AMD64ContextFloatingPoint,
        AMD64ContextAll = AMD64Context | AMD64ContextControl | AMD64ContextInteger | AMD64ContextSegments |
                            AMD64ContextFloatingPoint | AMD64ContextDebugRegisters,

        IA64Context = 0x80000,
        IA64ContextControl = IA64Context | 0x1,
        IA64ContextLowerFloatingPoint = IA64Context | 0x2,
        IA64ContextHigherFloatingPoint = IA64Context | 0x4,
        IA64ContextInteger = IA64Context | 0x8,
        IA64ContextDebug = IA64Context | 0x10,
        IA64ContextIA32Control = IA64Context | 0x20,
        IA64ContextFloatingPoint = IA64Context | IA64ContextLowerFloatingPoint | IA64ContextHigherFloatingPoint,
        IA64ContextFull = IA64Context | IA64ContextControl | IA64ContextFloatingPoint | IA64ContextInteger | IA64ContextIA32Control,
        IA64ContextAll = IA64Context | IA64ContextControl | IA64ContextFloatingPoint | IA64ContextInteger |
                           IA64ContextDebug | IA64ContextIA32Control,

        ARMContext = 0x200000,
        ARMContextControl = ARMContext | 0x1,
        ARMContextInteger = ARMContext | 0x2,
        ARMContextFloatingPoint = ARMContext | 0x4,
        ARMContextDebugRegisters = ARMContext | 0x8,
        ARMContextFull = ARMContext | ARMContextControl | ARMContextInteger,
        ARMContextAll = ARMContext | ARMContextControl | ARMContextInteger | ARMContextDebugRegisters,
    }

    public enum ContextSize : int
    {
        None = 0,
        X86 = 716,
        AMD64 = 1232,
        IA64 = 2672,
        ARM = 416,
    }

    [Flags]
    public enum X86Offsets : int
    {
        ContextFlags = 0x0,

        // This section is specified/returned if CONTEXT_DEBUG_REGISTERS is
        // set in ContextFlags.  Note that CONTEXT_DEBUG_REGISTERS is NOT
        // included in CONTEXT_FULL.
        Dr0 = 0x4,
        Dr1 = 0x8,
        Dr2 = 0xC,
        Dr3 = 0x10,
        Dr6 = 0x14,
        Dr7 = 0x18,

        // This section is specified/returned if the
        // ContextFlags word contains the flag CONTEXT_FLOATING_POINT.
        FloatSave = 0x1C,

        // This section is specified/returned if the
        // ContextFlags word contains the flag CONTEXT_SEGMENTS.
        SegGs = 0x8C,
        SegFs = 0x90,
        SegEs = 0x94,
        SegDs = 0x98,

        // This section is specified/returned if the
        // ContextFlags word contains the flag CONTEXT_INTEGER.
        Edi = 0x9C,
        Esi = 0xA0,
        Ebx = 0xA4,
        Edx = 0xA8,
        Ecx = 0xAC,
        Eax = 0xB0,

        // This section is specified/returned if the
        // ContextFlags word contains the flag CONTEXT_CONTROL.
        Ebp = 0xB4,
        Eip = 0xB8,
        SegCs = 0xBC,
        EFlags = 0xC0,
        Esp = 0xC4,
        SegSs = 0xC8,

        // This section is specified/returned if the ContextFlags word
        // contains the flag CONTEXT_EXTENDED_REGISTERS.
        // The format and contexts are processor specific
        ExtendedRegisters = 0xCB,  //512
    }

    [Flags]
    public enum X86Flags : int
    {
        SINGLE_STEP_FLAG = 0x100,
    }

    [Flags]
    public enum AMD64Offsets : int
    {
        // Register Parameter Home Addresses
        P1Home = 0x000,
        P2Home = 0x008,
        P3Home = 0x010,
        P4Home = 0x018,
        P5Home = 0x020,
        P6Home = 0x028,

        // Control Flags
        ContextFlags = 0x030,
        MxCsr = 0x034,

        // Segment Registers and Processor Flags
        SegCs = 0x038,
        SegDs = 0x03a,
        SegEs = 0x03c,
        SegFs = 0x03e,
        SegGs = 0x040,
        SegSs = 0x042,
        EFlags = 0x044,

        // Debug Registers
        Dr0 = 0x048,
        Dr1 = 0x050,
        Dr2 = 0x058,
        Dr3 = 0x060,
        Dr6 = 0x068,
        Dr7 = 0x070,

        // Integer Registers
        Rax = 0x078,
        Rcx = 0x080,
        Rdx = 0x088,
        Rbx = 0x090,
        Rsp = 0x098,
        Rbp = 0x0a0,
        Rsi = 0x0a8,
        Rdi = 0x0b0,
        R8 = 0x0b8,
        R9 = 0x0c0,
        R10 = 0x0c8,
        R11 = 0x0d0,
        R12 = 0x0d8,
        R13 = 0x0e0,
        R14 = 0x0e8,
        R15 = 0x0f0,

        // Program Counter
        Rip = 0x0f8,

        // Floating Point State
        FltSave = 0x100,
        Legacy = 0x120,
        Xmm0 = 0x1a0,
        Xmm1 = 0x1b0,
        Xmm2 = 0x1c0,
        Xmm3 = 0x1d0,
        Xmm4 = 0x1e0,
        Xmm5 = 0x1f0,
        Xmm6 = 0x200,
        Xmm7 = 0x210,
        Xmm8 = 0x220,
        Xmm9 = 0x230,
        Xmm10 = 0x240,
        Xmm11 = 0x250,
        Xmm12 = 0x260,
        Xmm13 = 0x270,
        Xmm14 = 0x280,
        Xmm15 = 0x290,

        // Vector Registers
        VectorRegister = 0x300,
        VectorControl = 0x4a0,

        // Special Debug Control Registers
        DebugControl = 0x4a8,
        LastBranchToRip = 0x4b0,
        LastBranchFromRip = 0x4b8,
        LastExceptionToRip = 0x4c0,
        LastExceptionFromRip = 0x4c8,
    }

    [Flags]
    public enum AMD64Flags : int
    {
        SINGLE_STEP_FLAG = 0x100,
    }

    [Flags]
    public enum IA64Offsets : int
    {
        ContextFlags = 0x0,

        // This section is specified/returned if the ContextFlags word contains
        // the flag CONTEXT_DEBUG.
        DbI0 = 0x010,
        DbI1 = 0x018,
        DbI2 = 0x020,
        DbI3 = 0x028,
        DbI4 = 0x030,
        DbI5 = 0x038,
        DbI6 = 0x040,
        DbI7 = 0x048,

        DbD0 = 0x050,
        DbD1 = 0x058,
        DbD2 = 0x060,
        DbD3 = 0x068,
        DbD4 = 0x070,
        DbD5 = 0x078,
        DbD6 = 0x080,
        DbD7 = 0x088,

        // This section is specified/returned if the ContextFlags word contains
        // the flag CONTEXT_LOWER_FLOATING_POINT.
        FltS0 = 0x090,
        FltS1 = 0x0a0,
        FltS2 = 0x0b0,
        FltS3 = 0x0c0,
        FltT0 = 0x0d0,
        FltT1 = 0x0e0,
        FltT2 = 0x0f0,
        FltT3 = 0x100,
        FltT4 = 0x110,
        FltT5 = 0x120,
        FltT6 = 0x130,
        FltT7 = 0x140,
        FltT8 = 0x150,
        FltT9 = 0x160,

        // This section is specified/returned if the ContextFlags word contains
        // the flag CONTEXT_HIGHER_FLOATING_POINT.
        FltS4 = 0x170,
        FltS5 = 0x180,
        FltS6 = 0x190,
        FltS7 = 0x1a0,
        FltS8 = 0x1b0,
        FltS9 = 0x1c0,
        FltS10 = 0x1d0,
        FltS11 = 0x1e0,
        FltS12 = 0x1f0,
        FltS13 = 0x200,
        FltS14 = 0x210,
        FltS15 = 0x220,
        FltS16 = 0x230,
        FltS17 = 0x240,
        FltS18 = 0x250,
        FltS19 = 0x260,

        FltF32 = 0x270,
        FltF33 = 0x280,
        FltF34 = 0x290,
        FltF35 = 0x2a0,
        FltF36 = 0x2b0,
        FltF37 = 0x2c0,
        FltF38 = 0x2d0,
        FltF39 = 0x2e0,

        FltF40 = 0x2f0,
        FltF41 = 0x300,
        FltF42 = 0x310,
        FltF43 = 0x320,
        FltF44 = 0x330,
        FltF45 = 0x340,
        FltF46 = 0x350,
        FltF47 = 0x360,
        FltF48 = 0x370,
        FltF49 = 0x380,

        FltF50 = 0x390,
        FltF51 = 0x3a0,
        FltF52 = 0x3b0,
        FltF53 = 0x3c0,
        FltF54 = 0x3d0,
        FltF55 = 0x3e0,
        FltF56 = 0x3f0,
        FltF57 = 0x400,
        FltF58 = 0x410,
        FltF59 = 0x420,

        FltF60 = 0x430,
        FltF61 = 0x440,
        FltF62 = 0x450,
        FltF63 = 0x460,
        FltF64 = 0x470,
        FltF65 = 0x480,
        FltF66 = 0x490,
        FltF67 = 0x4a0,
        FltF68 = 0x4b0,
        FltF69 = 0x4c0,

        FltF70 = 0x4d0,
        FltF71 = 0x4e0,
        FltF72 = 0x4f0,
        FltF73 = 0x500,
        FltF74 = 0x510,
        FltF75 = 0x520,
        FltF76 = 0x530,
        FltF77 = 0x540,
        FltF78 = 0x550,
        FltF79 = 0x560,

        FltF80 = 0x570,
        FltF81 = 0x580,
        FltF82 = 0x590,
        FltF83 = 0x5a0,
        FltF84 = 0x5b0,
        FltF85 = 0x5c0,
        FltF86 = 0x5d0,
        FltF87 = 0x5e0,
        FltF88 = 0x5f0,
        FltF89 = 0x600,

        FltF90 = 0x610,
        FltF91 = 0x620,
        FltF92 = 0x630,
        FltF93 = 0x640,
        FltF94 = 0x650,
        FltF95 = 0x660,
        FltF96 = 0x670,
        FltF97 = 0x680,
        FltF98 = 0x690,
        FltF99 = 0x6a0,

        FltF100 = 0x6b0,
        FltF101 = 0x6c0,
        FltF102 = 0x6d0,
        FltF103 = 0x6e0,
        FltF104 = 0x6f0,
        FltF105 = 0x700,
        FltF106 = 0x710,
        FltF107 = 0x720,
        FltF108 = 0x730,
        FltF109 = 0x740,

        FltF110 = 0x750,
        FltF111 = 0x760,
        FltF112 = 0x770,
        FltF113 = 0x780,
        FltF114 = 0x790,
        FltF115 = 0x7a0,
        FltF116 = 0x7b0,
        FltF117 = 0x7c0,
        FltF118 = 0x7d0,
        FltF119 = 0x7e0,

        FltF120 = 0x7f0,
        FltF121 = 0x800,
        FltF122 = 0x810,
        FltF123 = 0x820,
        FltF124 = 0x830,
        FltF125 = 0x840,
        FltF126 = 0x850,
        FltF127 = 0x860,

        // This section is specified/returned if the ContextFlags word contains
        // the flag CONTEXT_LOWER_FLOATING_POINT | CONTEXT_HIGHER_FLOATING_POINT | CONTEXT_CONTROL.
        StFPSR = 0x870,     //  FP status

        // This section is specified/returned if the ContextFlags word contains
        // the flag CONTEXT_INTEGER.
        IntGp = 0x878,      //  r1 = 0x, volatile
        IntT0 = 0x880,      //  r2-r3 = 0x, volatile
        IntT1 = 0x888,      //
        IntS0 = 0x890,      //  r4-r7 = 0x, preserved
        IntS1 = 0x898,
        IntS2 = 0x8a0,
        IntS3 = 0x8a8,
        IntV0 = 0x8b0,      //  r8 = 0x, volatile
        IntT2 = 0x8b8,      //  r9-r11 = 0x, volatile
        IntT3 = 0x8c0,
        IntT4 = 0x8c8,
        IntSp = 0x8d0,      //  stack pointer (r12) = 0x, special
        IntTeb = 0x8d8,     //  teb (r13) = 0x, special
        IntT5 = 0x8e0,      //  r14-r31 = 0x, volatile
        IntT6 = 0x8e8,
        IntT7 = 0x8f0,
        IntT8 = 0x8f8,
        IntT9 = 0x900,
        IntT10 = 0x908,
        IntT11 = 0x910,
        IntT12 = 0x918,
        IntT13 = 0x920,
        IntT14 = 0x928,
        IntT15 = 0x930,
        IntT16 = 0x938,
        IntT17 = 0x940,
        IntT18 = 0x948,
        IntT19 = 0x950,
        IntT20 = 0x958,
        IntT21 = 0x960,
        IntT22 = 0x968,
        IntNats = 0x970,    //  Nat bits for r1-r31
                            //  r1-r31 in bits 1 through 31.
        Preds = 0x978,      //  predicates = 0x, preserved

        BrRp = 0x980,       //  return pointer = 0x, b0 = 0x, preserved
        BrS0 = 0x988,       //  b1-b5 = 0x, preserved
        BrS1 = 0x990,
        BrS2 = 0x998,
        BrS3 = 0x9a0,
        BrS4 = 0x9a8,
        BrT0 = 0x9b0,       //  b6-b7 = 0x, volatile
        BrT1 = 0x9b8,

        // This section is specified/returned if the ContextFlags word contains
        // the flag CONTEXT_CONTROL.

        // Other application registers
        ApUNAT = 0x9c0,     //  User Nat collection register = 0x, preserved
        ApLC = 0x9c8,       //  Loop counter register = 0x, preserved
        ApEC = 0x9d0,       //  Epilog counter register = 0x, preserved
        ApCCV = 0x9d8,      //  CMPXCHG value register = 0x, volatile
        ApDCR = 0x9e0,      //  Default control register (TBD)

        // Register stack info
        RsPFS = 0x9e8,      //  Previous function state = 0x, preserved
        RsBSP = 0x9f0,      //  Backing store pointer = 0x, preserved
        RsBSPSTORE = 0x9f8,
        RsRSC = 0xa00,      //  RSE configuration = 0x, volatile
        RsRNAT = 0xa08,     //  RSE Nat collection register = 0x, preserved

        // Trap Status Information
        StIPSR = 0xa10,     //  Interruption Processor Status
        StIIP = 0xa18,      //  Interruption IP
        StIFS = 0xa20,      //  Interruption Function State

        // iA32 related control registers
        StFCR = 0xa28,      //  copy of Ar21
        Eflag = 0xa30,      //  Eflag copy of Ar24
        SegCSD = 0xa38,     //  iA32 CSDescriptor (Ar25)
        SegSSD = 0xa40,     //  iA32 SSDescriptor (Ar26)
        Cflag = 0xa48,      //  Cr0+Cr4 copy of Ar27
        StFSR = 0xa50,      //  x86 FP status (copy of AR28)
        StFIR = 0xa58,      //  x86 FP status (copy of AR29)
        StFDR = 0xa60,      //  x86 FP status (copy of AR30)
        UNUSEDPACK = 0xa68, // alignment padding

    }

    [Flags]
    public enum IA64Flags : long
    {
        PSR_RI = 41,
        IA64_BUNDLE_SIZE = 16,
        SINGLE_STEP_FLAG = 0x1000000000,
    }

    [Flags]
    public enum ARMOffsets : int
    {
        ContextFlags = 0x0,

        // Integer registers
        Dr0 = 0x4,
        Dr1 = 0x8,
        Dr2 = 0xC,
        Dr3 = 0x10,
        Dr4 = 0x14,
        Dr5 = 0x18,
        Dr6 = 0x1C,
        Dr7 = 0x20,
        Dr8 = 0x24,
        Dr9 = 0x28,
        Dr10 = 0x2C,
        Dr11 = 0x30,
        Dr12 = 0x34,

        // Control Registers
        Sp = 0x38,
        Lr = 0x3C,
        Pc = 0x40,
        Cpsr = 0x44,

        // Floating Point/NEON Registers
        //
        Fpscr = 0x48,

        /*
        union {
            M128A Q[16]; --> Quad) 16 items that are 16 byte aligned. Total size is 256 decimal or 100 hex
            ULONGLONG D[32]; --> Double) 32 items that are 8 bytes in size. Total size is 256 decimal or 100 hex
            DWORD S[32]; --> Single) 32 items that are 4 bytes in size. Total size is 128 decimal or 80 hex
        } DUMMYUNIONNAME;
        // size of the union = largest size which is 256 deciman or 100 hex...From dumping the context in Windbg
        // it appears that the union is aligned to start at the next 0x10 boundary which is 0x5C which
        // means the next item will start at 0x15C
        */
        Q = 0x50,
        D = 0x50,
        S = 0x50,

        //
        // Debug registers
        //
        /*
        // ARM_MAX_BREAKPOINTS = 8
        // ARM_MAX_WATCHPOINTS = 1
        DWORD Bvr[ARM_MAX_BREAKPOINTS];
        DWORD Bcr[ARM_MAX_BREAKPOINTS];
        DWORD Wvr[ARM_MAX_WATCHPOINTS];
        DWORD Wcr[ARM_MAX_WATCHPOINTS];
        */
        Bvr = 0x150,
        Bcr = 0x170,
        Wvr = 0x190,
        Wcr = 0x194,
    }

    [Flags]
    public enum ARMFlags : int
    {
        // There is no single step flag for arm that is implemented or recognized
        // on all arm devices. Single stepping was implemented by the CLR team for
        // managed debuging and this flag is just a place holder
        SINGLE_STEP_FLAG = 0x000,
    }

    [Flags]
    public enum ImageFileMachine : int
    {
        X86 = 0x014c,
        AMD64 = 0x8664,
        IA64 = 0x0200,
        ARM = 0x01c4, // IMAGE_FILE_MACHINE_ARMNT
    }

    public enum Platform
    {
        None = 0,
        X86 = 1,
        AMD64 = 2,
        IA64 = 3,
        ARM = 4,
    }


    /// <summary>
    /// Native debug event Codes that are returned through NativeStop event
    /// </summary>
    public enum NativeDebugEventCode
    {
        None = 0,
        EXCEPTION_DEBUG_EVENT = 1,
        CREATE_THREAD_DEBUG_EVENT = 2,
        CREATE_PROCESS_DEBUG_EVENT = 3,
        EXIT_THREAD_DEBUG_EVENT = 4,
        EXIT_PROCESS_DEBUG_EVENT = 5,
        LOAD_DLL_DEBUG_EVENT = 6,
        UNLOAD_DLL_DEBUG_EVENT = 7,
        OUTPUT_DEBUG_STRING_EVENT = 8,
        RIP_EVENT = 9,
    }

    // Debug header for debug events.
    [StructLayout(LayoutKind.Sequential)]
    public struct DebugEventHeader
    {
        public NativeDebugEventCode dwDebugEventCode;
        public UInt32 dwProcessId;
        public UInt32 dwThreadId;
    };

    public enum ThreadAccess : int
    {
        None = 0,
        THREAD_ALL_ACCESS = (0x1F03FF),
        THREAD_DIRECT_IMPERSONATION = (0x0200),
        THREAD_GET_CONTEXT = (0x0008),
        THREAD_IMPERSONATE = (0x0100),
        THREAD_QUERY_INFORMATION = (0x0040),
        THREAD_QUERY_LIMITED_INFORMATION = (0x0800),
        THREAD_SET_CONTEXT = (0x0010),
        THREAD_SET_INFORMATION = (0x0020),
        THREAD_SET_LIMITED_INFORMATION = (0x0400),
        THREAD_SET_THREAD_TOKEN = (0x0080),
        THREAD_SUSPEND_RESUME = (0x0002),
        THREAD_TERMINATE = (0x0001),

    }

    #region Exception events
    /// <summary>
    /// Common Exception codes
    /// </summary>
    /// <remarks>Users can define their own exception codes, so the code could be any value. 
    /// The OS reserves bit 28 and may clear that for its own purposes</remarks>
    public enum ExceptionCode : uint
    {
        None = 0x0, // included for completeness sake
        STATUS_BREAKPOINT = 0x80000003,
        STATUS_SINGLESTEP = 0x80000004,

        EXCEPTION_INT_DIVIDE_BY_ZERO = 0xC0000094,

        /// <summary>
        /// Fired when debuggee gets a Control-C. 
        /// </summary>
        DBG_CONTROL_C = 0x40010005,

        EXCEPTION_STACK_OVERFLOW = 0xC00000FD,
        EXCEPTION_NONCONTINUABLE_EXCEPTION = 0xC0000025,
        EXCEPTION_ACCESS_VIOLATION = 0xc0000005,
    }

    /// <summary>
    /// Flags for <see cref="EXCEPTION_RECORD"/>
    /// </summary>
    [Flags]
    public enum ExceptionRecordFlags : uint
    {
        /// <summary>
        /// No flags. 
        /// </summary>
        None = 0x0,

        /// <summary>
        /// Exception can not be continued. Debugging services can still override this to continue the exception, but recommended to warn the user in this case.
        /// </summary>
        EXCEPTION_NONCONTINUABLE = 0x1,
    }

    /// <summary>
    /// Information about an exception
    /// </summary>    
    /// <remarks>This will default to the correct caller's platform</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct EXCEPTION_RECORD
    {
        public ExceptionCode ExceptionCode;
        public ExceptionRecordFlags ExceptionFlags;

        /// <summary>
        /// Based off ExceptionFlags, is the exception Non-continuable?
        /// </summary>
        public bool IsNotContinuable
        {
            get
            {
                return (ExceptionFlags & ExceptionRecordFlags.EXCEPTION_NONCONTINUABLE) != 0;
            }
        }

        public IntPtr ExceptionRecord;

        /// <summary>
        /// Address in the debuggee that the exception occurred at.
        /// </summary>
        public IntPtr ExceptionAddress;

        /// <summary>
        /// Number of parameters used in ExceptionInformation array.
        /// </summary>
        public UInt32 NumberParameters;
        private const int EXCEPTION_MAXIMUM_PARAMETERS = 15;
        // We'd like to marshal this as a ByValArray, but that's not supported yet.
        // We get an alignment error  / TypeLoadException for DebugEventUnion
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = EXCEPTION_MAXIMUM_PARAMETERS)]
        //public IntPtr [] ExceptionInformation;  

        // Instead, mashal manually.
        public IntPtr ExceptionInformation0;
        public IntPtr ExceptionInformation1;
        public IntPtr ExceptionInformation2;
        public IntPtr ExceptionInformation3;
        public IntPtr ExceptionInformation4;
        public IntPtr ExceptionInformation5;
        public IntPtr ExceptionInformation6;
        public IntPtr ExceptionInformation7;
        public IntPtr ExceptionInformation8;
        public IntPtr ExceptionInformation9;
        public IntPtr ExceptionInformation10;
        public IntPtr ExceptionInformation11;
        public IntPtr ExceptionInformation12;
        public IntPtr ExceptionInformation13;
        public IntPtr ExceptionInformation14;
    } // end of class EXCEPTION_RECORD

    /// <summary>
    /// Information about an exception debug event.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EXCEPTION_DEBUG_INFO
    {
        public EXCEPTION_RECORD ExceptionRecord;
        public UInt32 dwFirstChance;
    } // end of class EXCEPTION_DEBUG_INFO

    #endregion // Exception events

    // MODULEINFO declared in psapi.h
    [StructLayout(LayoutKind.Sequential)]
    public struct ModuleInfo
    {
        public IntPtr lpBaseOfDll;
        public uint SizeOfImage;
        public IntPtr EntryPoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CREATE_PROCESS_DEBUG_INFO
    {
        public IntPtr hFile;
        public IntPtr hProcess;
        public IntPtr hThread;
        public IntPtr lpBaseOfImage;
        public UInt32 dwDebugInfoFileOffset;
        public UInt32 nDebugInfoSize;
        public IntPtr lpThreadLocalBase;
        public IntPtr lpStartAddress;
        public IntPtr lpImageName;
        public UInt16 fUnicode;
    } // end of class CREATE_PROCESS_DEBUG_INFO

    [StructLayout(LayoutKind.Sequential)]
    public struct CREATE_THREAD_DEBUG_INFO
    {
        public IntPtr hThread;
        public IntPtr lpThreadLocalBase;
        public IntPtr lpStartAddress;
    } // end of class CREATE_THREAD_DEBUG_INFO

    [StructLayout(LayoutKind.Sequential)]
    public struct EXIT_THREAD_DEBUG_INFO
    {
        public UInt32 dwExitCode;
    } // end of class EXIT_THREAD_DEBUG_INFO

    [StructLayout(LayoutKind.Sequential)]
    public struct EXIT_PROCESS_DEBUG_INFO
    {
        public UInt32 dwExitCode;
    } // end of class EXIT_PROCESS_DEBUG_INFO

    [StructLayout(LayoutKind.Sequential)]
    public struct LOAD_DLL_DEBUG_INFO
    {
        public IntPtr hFile;
        public IntPtr lpBaseOfDll;
        public UInt32 dwDebugInfoFileOffset;
        public UInt32 nDebugInfoSize;
        public IntPtr lpImageName;
        public UInt16 fUnicode;


        // Helper to read an IntPtr from the target
        private IntPtr ReadIntPtrFromTarget(IMemoryReader reader, IntPtr ptr)
        {
            // This is not cross-platform: it assumes host and target are the same size.
            byte[] buffer = new byte[IntPtr.Size];
            reader.ReadMemory(ptr, buffer);

            System.UInt64 val = 0;
            // Note: this is dependent on endienness.
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                val <<= 8;
                val += buffer[i];
            }
            IntPtr newptr = new IntPtr(unchecked((long)val));

            return newptr;
        }


        /// <summary>
        /// Read the image name from the target.
        /// </summary>
        /// <param name="reader">access to target's memory</param>
        /// <returns>String for full path to image. Null if name not available</returns>
        /// <remarks>MSDN says this will never be provided for during Attach scenarios; nor for the first 1 or 2 dlls.</remarks>
        public string ReadImageNameFromTarget(IMemoryReader reader)
        {
            string moduleName;
            bool bUnicode = (fUnicode != 0);

            if (lpImageName == IntPtr.Zero)
            {
                return null;
            }
            else
            {
                try
                {
                    IntPtr newptr = ReadIntPtrFromTarget(reader, lpImageName);

                    if (newptr == IntPtr.Zero)
                    {
                        return null;
                    }
                    else
                    {
                        int charSize = (bUnicode) ? 2 : 1;
                        byte[] buffer = new byte[charSize];

                        System.Text.StringBuilder sb = new System.Text.StringBuilder();

                        while (true)
                        {
                            // Read 1 character at a time. This is extremely inefficient,
                            // but we don't know the whole length of the string and it ensures we don't
                            // read off a page.
                            reader.ReadMemory(newptr, buffer);

                            int b;
                            if (bUnicode)
                            {
                                b = (int)buffer[0] + ((int)buffer[1] << 8);
                            }
                            else
                            {
                                b = (int)buffer[0];
                            }

                            if (b == 0) // string is null-terminated
                            {
                                break;
                            }
                            sb.Append((char)b);
                            newptr = new IntPtr(newptr.ToInt64() + charSize); // move to next character
                        }

                        moduleName = sb.ToString();
                    }
                }
                catch (System.DataMisalignedException)
                {
                    return null;
                }
                catch (InvalidOperationException) // ignore failures to read
                {
                    return null;
                }
                catch (COMException e)
                {
                    // 0x80131c49 is CORDBG_E_READVIRTUAL_FAILURE, but because of the way MDbg is built I can't 
                    // reference Microsoft.Samples.Debugging.CorDebug.HResult.CORDBG_E_READVIRTUAL_FAILURE here.
                    //
                    // On Win8, for some reason the name of the module is not available at the module load debug 
                    // event, even though the address is stored by the OS in the DEBUG_EVENT struct.
                    if (e.ErrorCode == unchecked((int)0x80131c49))
                    {
                        return null;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return moduleName;
        }


    } // end of class LOAD_DLL_DEBUG_INFO

    [StructLayout(LayoutKind.Sequential)]
    public struct UNLOAD_DLL_DEBUG_INFO
    {
        public IntPtr lpBaseOfDll;
    } // end of class UNLOAD_DLL_DEBUG_INFO

    [StructLayout(LayoutKind.Sequential)]
    public struct OUTPUT_DEBUG_STRING_INFO
    {
        public IntPtr lpDebugStringData;
        public UInt16 fUnicode;
        public UInt16 nDebugStringLength;

        // 
        /// <summary>
        /// Read the log message from the target. 
        /// </summary>
        /// <param name="reader">interface to access debuggee memory</param>
        /// <returns>string containing message or null if not available</returns>
        public string ReadMessageFromTarget(IMemoryReader reader)
        {
            try
            {
                bool isUnicode = (fUnicode != 0);

                int cbCharSize = (isUnicode) ? 2 : 1;
                byte[] buffer = new byte[nDebugStringLength * cbCharSize];
                reader.ReadMemory(lpDebugStringData, buffer);

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < buffer.Length; i += cbCharSize)
                {
                    int val;
                    if (isUnicode)
                    {
                        val = (int)buffer[i] + ((int)buffer[i + 1] << 8);
                    }
                    else
                    {
                        val = buffer[i];
                    }
                    sb.Append((char)val);
                }
                return sb.ToString();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

    } // end of class OUTPUT_DEBUG_STRING_INFO

    [StructLayout(LayoutKind.Explicit)]
    public struct DebugEventUnion
    {
        [FieldOffset(0)]
        public CREATE_PROCESS_DEBUG_INFO CreateProcess;

        [FieldOffset(0)]
        public EXCEPTION_DEBUG_INFO Exception;

        [FieldOffset(0)]
        public CREATE_THREAD_DEBUG_INFO CreateThread;

        [FieldOffset(0)]
        public EXIT_THREAD_DEBUG_INFO ExitThread;

        [FieldOffset(0)]
        public EXIT_PROCESS_DEBUG_INFO ExitProcess;

        [FieldOffset(0)]
        public LOAD_DLL_DEBUG_INFO LoadDll;

        [FieldOffset(0)]
        public UNLOAD_DLL_DEBUG_INFO UnloadDll;

        [FieldOffset(0)]
        public OUTPUT_DEBUG_STRING_INFO OutputDebugString;
    }

    // 32-bit and 64-bit have sufficiently different alignment that we need 
    // two different debug event structures.

    /// <summary>
    /// Matches DEBUG_EVENT layout on 32-bit architecture
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct DebugEvent32
    {
        [FieldOffset(0)]
        public DebugEventHeader header;

        [FieldOffset(12)]
        public DebugEventUnion union;
    }

    /// <summary>
    /// Matches DEBUG_EVENT layout on 64-bit architecture
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct DebugEvent64
    {
        [FieldOffset(0)]
        public DebugEventHeader header;

        [FieldOffset(16)]
        public DebugEventUnion union;
    }

    #endregion Native Structures


    // SafeHandle to call CloseHandle
    [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
    public sealed class SafeWin32Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeWin32Handle() : base(true) { }

        public SafeWin32Handle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }


        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(handle);
        }
    }

    // These extend the Mdbg native definitions.
    public static class NativeMethods
    {
        private const string Kernel32LibraryName = "kernel32.dll";
        private const string PsapiLibraryName = "psapi.dll";

        //
        // These should be sharable with other pinvokes
        //

        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        public static extern int WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport(Kernel32LibraryName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetThreadContext(IntPtr hThread, IntPtr lpContext);

        [DllImport(Kernel32LibraryName)]
        public static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwThreadId);

        [DllImport(Kernel32LibraryName)]
        public static extern SafeWin32Handle OpenProcess(Int32 dwDesiredAccess, bool bInheritHandle, Int32 dwProcessId);

        [DllImport(Kernel32LibraryName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadContext(IntPtr hThread, IntPtr lpContext);

        // This gets the raw OS thread ID. This is not fiber aware. 
        [DllImport(Kernel32LibraryName)]
        public static extern int GetCurrentThreadId();

        [DllImport(Kernel32LibraryName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(SafeWin32Handle hProcess, ref bool isWow);

        [Flags]
        public enum PageProtection : uint
        {
            NoAccess = 0x01,
            Readonly = 0x02,
            ReadWrite = 0x04,
            WriteCopy = 0x08,
            Execute = 0x10,
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40,
            ExecuteWriteCopy = 0x80,
            Guard = 0x100,
            NoCache = 0x200,
            WriteCombine = 0x400,
        }

        // Call BOOL UnmapViewOfFile(void*) to clean up. 
        [DllImport(Kernel32LibraryName, SetLastError = true)]
        public static extern SafeMemoryMappedViewHandle MapViewOfFile(SafeMemoryMappedFileHandle hFileMappingObject, uint
           dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow,
           IntPtr dwNumberOfBytesToMap);

        [Flags]
        public enum LoadLibraryFlags : uint
        {
            NoFlags = 0x00000000,
            DontResolveDllReferences = 0x00000001,
            LoadIgnoreCodeAuthzLevel = 0x00000010,
            LoadLibraryAsDatafile = 0x00000002,
            LoadLibraryAsDatafileExclusive = 0x00000040,
            LoadLibraryAsImageResource = 0x00000020,
            LoadWithAlteredSearchPath = 0x00000008
        }

        // SafeHandle to call FreeLibrary
        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public sealed class SafeLoadLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeLoadLibraryHandle() : base(true) { }
            public SafeLoadLibraryHandle(IntPtr handle) : base(true)
            {
                SetHandle(handle);
            }

            protected override bool ReleaseHandle()
            {
                return FreeLibrary(handle);
            }

            // This is technically equivalent to DangerousGetHandle, but it's safer for loaded
            // libraries where the HMODULE is also the base address the module is loaded at.
            public IntPtr BaseAddress
            {
                get
                {
                    return handle;
                }
            }
        }

        [DllImportAttribute(Kernel32LibraryName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeLibrary(IntPtr hModule);

        [DllImportAttribute(Kernel32LibraryName)]
        internal static extern IntPtr LoadLibraryEx(String fileName, int hFile, LoadLibraryFlags dwFlags);

        // Filesize can be used as a approximation of module size in memory.
        // In memory size will be larger because of alignment issues.
        [DllImport(Kernel32LibraryName)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileSizeEx(IntPtr hFile, out System.Int64 lpFileSize);

        // Get the module's size.
        // This can not be called during the actual dll-load debug event. 
        // (The debug event is sent before the information is initialized)
        [DllImport(PsapiLibraryName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out ModuleInfo lpmodinfo, uint countBytes);


        // Read memory from live, local process.
        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
          byte[] lpBuffer, UIntPtr nSize, out int lpNumberOfBytesRead);



        // Requires Windows XP / Win2k03
        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DebugSetProcessKillOnExit(
            [MarshalAs(UnmanagedType.Bool)]
            bool KillOnExit
        );

        // Requires WinXp/Win2k03
        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DebugBreakProcess(IntPtr hProcess);

        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetEvent(SafeWin32Handle eventHandle);


        #region Attach / Detach APIS
        // constants used in CreateProcess functions
        public enum CreateProcessFlags
        {
            CREATE_NEW_CONSOLE = 0x00000010,

            // This will include child processes.
            DEBUG_PROCESS = 1,

            // This will be just the target process.
            DEBUG_ONLY_THIS_PROCESS = 2,
        }

        [DllImport(Kernel32LibraryName, CharSet = CharSet.Unicode, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)]
            bool bInheritHandles,
            CreateProcessFlags dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            STARTUPINFO lpStartupInfo,// class
            PROCESS_INFORMATION lpProcessInformation // class
        );


        // Attach to a process
        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DebugActiveProcess(uint dwProcessId);

        // Detach from a process
        // Requires WinXp/Win2k03
        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DebugActiveProcessStop(uint dwProcessId);

        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);


        #endregion // Attach / Detach APIS


        #region Stop-Go APIs
        // We have two separate versions of kernel32!WaitForDebugEvent to cope with different structure
        // layout on each platform.
        [DllImport(Kernel32LibraryName, EntryPoint = "WaitForDebugEvent", SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WaitForDebugEvent32(ref DebugEvent32 pDebugEvent, int dwMilliseconds);

        [DllImport(Kernel32LibraryName, EntryPoint = "WaitForDebugEvent", SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WaitForDebugEvent64(ref DebugEvent64 pDebugEvent, int dwMilliseconds);

        /// <summary>
        /// Values to pass to ContinueDebugEvent for ContinueStatus
        /// </summary>
        public enum ContinueStatus : uint
        {
            /// <summary>
            /// This is our own "empty" value
            /// </summary>
            CONTINUED = 0,

            /// <summary>
            /// Debugger consumes exceptions. Debuggee will never see the exception. Like "gh" in Windbg.
            /// </summary>
            DBG_CONTINUE = 0x00010002,

            /// <summary>
            /// Debugger does not interfere with exception processing, this passes the exception onto the debuggee.
            /// Like "gn" in Windbg.
            /// </summary>
            DBG_EXCEPTION_NOT_HANDLED = 0x80010001,
        }

        [DllImport(Kernel32LibraryName, SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ContinueDebugEvent(uint dwProcessId, uint dwThreadId, ContinueStatus dwContinueStatus);

        #endregion // Stop-Go

        [DllImport("kernel32.dll")]
        public static extern void GetSystemInfo([MarshalAs(UnmanagedType.Struct)] out SYSTEM_INFO lpSystemInfo);


        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_INFO
        {
            // Don't marshal dwOemId since that's obsolete and lets
            // us avoid marshalling a union.
            internal ProcessorArchitecture wProcessorArchitecture;
            internal ushort wReserved;

            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType; // obsolete
            public uint dwAllocationGranularity;
            public ushort dwProcessorLevel;
            public ushort dwProcessorRevision;
        }

    } // NativeMethods

}
