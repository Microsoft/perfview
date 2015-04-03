﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Utilities;
using Address = System.UInt64;
using PerfView;

namespace PerfView
{
    public class HeapDumper
    {
        /// <summary>
        /// Take a heap dump from a live process. 
        /// </summary>
        public static void DumpGCHeap(int processID, string outputFile, TextWriter log = null, string qualifiers = "")
        {
            if (!App.IsElevated)
                throw new ApplicationException("Must be Administrator (elevated).");

            var arch = GetArchForProcess(processID);
            if (log != null)
                log.WriteLine("Starting Heap dump on Process {0} running architecture {1}.", processID, arch);

            DumpGCHeap(qualifiers, processID.ToString(), outputFile, log, arch);
            log.WriteLine("Finished Heap Dump.");
        }

#if CROSS_GENERATION_LIVENESS
        /// <summary>
        /// Take a heap dump from a live process. 
        /// </summary>
        public static void DumpGCHeapForCrossGenerationLiveness(int processID, int generationToTrigger, ulong promotedBytesThreshold, string outputFile, TextWriter log = null, string qualifiers = "")
        {
            if (!App.IsElevated)
                throw new ApplicationException("Must be Administrator (elevated).");

            var arch = GetArchForProcess(processID);
            if (log != null)
                log.WriteLine("Starting Heap dump for cross generation liveness on Process {0} running architecture {1}.", processID, arch);

            qualifiers += " /PromotedBytesThreshold:" + promotedBytesThreshold;
            qualifiers += " /GenerationToTrigger:" + generationToTrigger;
            DumpGCHeap(qualifiers, processID.ToString(), outputFile, log, arch);
            log.WriteLine("Finished Heap Dump.");
        }
#endif

        /// <summary>
        /// Force a GC on process processID
        /// </summary>
        internal static void ForceGC(int processID, TextWriter log = null)
        {
            // We force a GC by turning on an ETW provider, which needs admin to do.  
            if (!App.IsElevated)
                throw new ApplicationException("Must be Administrator (elevated) to use Force GC option.");

            var arch = GetArchForProcess(processID);
            if (log != null)
                log.WriteLine("Starting Heap dump on Process {0} running architecture {1}.", processID, arch);

            var heapDumpExe = Path.Combine(SupportFiles.SupportFileDir, arch + @"\HeapDump.exe");
            var options = new CommandOptions().AddNoThrow().AddTimeout(1 * 3600 * 1000);
            if (log != null)
                options.AddOutputStream(log);

            var commandLine = string.Format("\"{0}\" /ForceGC {1}", heapDumpExe, processID.ToString());
            log.WriteLine("Exec: {0}", commandLine);
            var cmd = Command.Run(commandLine, options);
            if (cmd.ExitCode != 0)
                throw new ApplicationException("HeapDump failed with exit code " + cmd.ExitCode + ".  See log for details.");
        }

        /// <summary>
        /// Take a heap dump from a process dump
        /// </summary>
        public static void DumpGCHeap(string processDumpFile, string outputFile, TextWriter log, string qualifiers = "")
        {

            // Determine if we are on a 64 bit system.  
            var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            var trueArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
            if (trueArch != null)
            {
                // TODO FIX NOW.   Find a way of determing which architecture a dump is
                try
                {
                    log.WriteLine("********** TRYING TO OPEN THE DUMP AS 64 BIT ************");
                    DumpGCHeap("/processDump " + qualifiers, processDumpFile, outputFile, log, trueArch);
                    return; // Yeah! success the first time
                }
                catch (Exception e)
                {
                    // It might have failed because this was a 32 bit dump, if so try again.  
                    if (e is ApplicationException)
                    {
                        log.WriteLine("********** TRYING TO OPEN THE DUMP AS 32 BIT ************");
                        DumpGCHeap("/processDump" + qualifiers, processDumpFile, outputFile, log, arch);
                        return;
                    }
                    throw;
                }
            }
            DumpGCHeap("/processDump", processDumpFile, outputFile, log, arch);
        }
        /// <summary>
        /// Given a name or a process ID, return the process ID for it.  If it is a name
        /// it will return the youngest process ID for all processes with that
        /// name.   Returns a negative ID if the process is not found.  
        /// </summary>
        public static int GetProcessID(string processNameOrID)
        {
            int parsedInt;
            if (int.TryParse(processNameOrID, out parsedInt))
            {
                var process = Process.GetProcessById(parsedInt);
                if (process != null)
                {
                    process.Dispose();
                    return parsedInt;
                }
            }
            // It is a name find the youngest process with that name.  
            // remove .exe if present.
            if (processNameOrID.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processNameOrID = processNameOrID.Substring(0, processNameOrID.Length - 4);
            Process youngestProcess = null;
            foreach (var process in Process.GetProcessesByName(processNameOrID))
            {
                if (youngestProcess == null || process.StartTime > youngestProcess.StartTime)
                    youngestProcess = process;
            }
            if (youngestProcess != null)
                return youngestProcess.Id;

            return -1;
        }

        #region private
        private static void DumpGCHeap(string qualifiers, string inputArg, string outputFile, TextWriter log, string arch)
        {
            var heapDumpExe = Path.Combine(SupportFiles.SupportFileDir, arch + @"\HeapDump.exe");

            var options = new CommandOptions().AddNoThrow().AddTimeout(CommandOptions.Infinite);
            if (log != null)
                options.AddOutputStream(log);

            // TODO breaking abstraction to know about StackWindow. 
            options.AddEnvironmentVariable("_NT_SYMBOL_PATH", App.SymbolPath);
            log.WriteLine("set _NT_SYMBOL_PATH={0}", App.SymbolPath);

            var commandLine = string.Format("\"{0}\" {1} \"{2}\" \"{3}\"", heapDumpExe, qualifiers, inputArg, outputFile);
            log.WriteLine("Exec: {0}", commandLine);
            PerfViewLogger.Log.TriggerHeapSnapshot(outputFile, inputArg, qualifiers);
            var cmd = Command.Run(commandLine, options);
            if (cmd.ExitCode != 0)
                throw new ApplicationException("HeapDump failed with exit code " + cmd.ExitCode);

            if (log != null)
                log.WriteLine("Completed Heap Dump for {0} to {1}", inputArg, outputFile);
        }

        /// <summary>
        /// Returns the x86 or AMD64 that indicates the architecture of the process with 'processID'
        /// </summary>
        private static string GetArchForProcess(int processID)
        {
            // I assume that I am always a 32 bit process.  
            Debug.Assert(System.Runtime.InteropServices.Marshal.SizeOf(typeof(IntPtr)) == 4);

            try
            {
                // TO make error paths simple always try to access the process here even throw we don't need it 
                // for a 32 bit machine. 
                var process = Process.GetProcessById(processID);

                // Get the true processor architecture.  
                var procArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
                if (procArch == null)
                    procArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                if (procArch != "AMD64")        // Currently only AMD64 has a wow. 
                    return procArch;

                bool is32Bit = false;
                bool ret = IsWow64Process(process.Handle, out is32Bit);
                GC.KeepAlive(process);
                if (ret)
                    return is32Bit ? "x86" : "AMD64";
            }
            catch (System.Runtime.InteropServices.ExternalException e)
            {
                if ((uint)e.ErrorCode == 0x80004005)
                    throw new ApplicationException("Access denied to inspect process (Not Elevated?).");
            }
            catch (Exception) { }
            throw new ApplicationException("Could not determine the process architecture for process with ID " + processID);
        }

        [DllImport("kernel32.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(
             [In] IntPtr processHandle,
             [Out, MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

        #endregion
    }
}
