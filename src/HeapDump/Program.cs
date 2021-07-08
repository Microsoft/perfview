using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using Triggers;
#if CROSS_GENERATION_LIVENESS
using Microsoft.Diagnostics.CrossGenerationLiveness;
#endif

internal class Program
{
    private static int Main(string[] args)
    {
        // This EXE lives in the architecture specific directory but uses TraceEvent which lives in the neutral directory, 
        // Set up a resolve event that finds this DLL.  
        AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs resolveArgs)
        {
            var simpleName = resolveArgs.Name;
            var commaIdx = simpleName.IndexOf(',');
            if (0 <= commaIdx)
            {
                simpleName = simpleName.Substring(0, commaIdx);
            }

            var exeAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            var parentDir = Path.GetDirectoryName(Path.GetDirectoryName(exeAssembly.ManifestModule.FullyQualifiedName));
            string fileName = Path.Combine(parentDir, simpleName + ".dll");
            if (File.Exists(fileName))
            {
                return System.Reflection.Assembly.LoadFrom(fileName);
            }

            return null;
        };

        return MainWorker(args);
    }

    private static int MainWorker(string[] args)
    {
        string outputFile = null;
        try
        {
            double decayToZeroHours = 0;
            bool forceGC = false;
            bool processDump = false;
            string inputSpec = null;
            var dumper = new GCHeapDumper(Console.Out);

            for (int curArgIdx = 0; curArgIdx < args.Length; curArgIdx++)
            {
                var arg = args[curArgIdx].Trim();
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (arg.StartsWith("/"))
                {
                    // This is not for external use.  On 64 bit systems we need to do the GetProcess in a 64 bit process. 
                    if (string.Compare(arg, "/GetProcessesWithGCHeaps", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        foreach (var processInfo in GCHeapDump.GetProcessesWithGCHeaps().Values)
                        {
                            Console.WriteLine("{0}{1} {2}", processInfo.UsesDotNet ? 'N' : ' ',
                                processInfo.UsesJavaScript ? 'J' : ' ', processInfo.ID);
                        }

                        return 0;
                    }
                    else if (string.Compare(arg, "/dumpData", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        dumper.DumpData = true;
                    }
                    else if (string.Compare(arg, "/processDump", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        processDump = true;
                    }
                    else if (string.Compare(arg, "/forceGC", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        forceGC = true;
                    }
                    else if (string.Compare(arg, "/freeze", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        dumper.Freeze = true;
                    }
                    else if (string.Compare(arg, 0, "/MaxDumpCountK=", 0, 15, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        string value = arg.Substring(15);
                        if (!int.TryParse(value, out dumper.MaxDumpCountK))
                        {
                            Console.WriteLine("Could not parse MaxDumpCount argument: {0}", value);
                            goto Usage;
                        }
                    }
                    else if (string.Compare(arg, 0, "/MaxNodeCountK=", 0, 15, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        string value = arg.Substring(15);
                        if (!int.TryParse(value, out dumper.MaxNodeCountK))
                        {
                            Console.WriteLine("Could not parse MaxNodeCount argument: {0}", value);
                            goto Usage;
                        }
                    }
                    else if (string.Compare(arg, "/SaveETL", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        dumper.SaveETL = true;
                    }
                    else if (string.Compare(arg, "/UseETW", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        dumper.UseETW = true;
                    }
                    else if (arg.StartsWith("/DecayToZeroHours:", StringComparison.OrdinalIgnoreCase))
                    {
                        decayToZeroHours = double.Parse(arg.Substring(18));
                    }
                    else if (arg.StartsWith("/StopOnPerfCounter:", StringComparison.OrdinalIgnoreCase))
                    {
                        string spec = arg.Substring(19);
                        bool done = false;
                        using (var trigger = new PerformanceCounterTrigger(spec, decayToZeroHours, Console.Out, delegate (PerformanceCounterTrigger t) { done = true; }))
                        {
                            for (int i = 0; !done; i++)
                            {
                                if (i % 10 == 2)
                                {
                                    Console.WriteLine("[{0}]", trigger.Status);
                                    Console.Out.Flush();
                                }
                                Thread.Sleep(1000);
                            }
                        }
                        Console.WriteLine("[PerfCounter Triggered: {0}]", spec);
                        return 0;
                    }
                    else if (arg.StartsWith("/PromotedBytesThreshold:", StringComparison.OrdinalIgnoreCase))
                    {
                        dumper.CrossGeneration = true;
                        string spec = arg.Substring(24);
                        dumper.PromotedBytesThreshold = Convert.ToUInt64(spec);
                        Console.WriteLine("Promoted Bytes Threshold: " + dumper.PromotedBytesThreshold);
                    }
                    else if (arg.StartsWith("/GenerationToTrigger:", StringComparison.OrdinalIgnoreCase))
                    {
                        string spec = arg.Substring(21);
                        dumper.GenerationToTrigger = Convert.ToInt32(spec);
                        if (dumper.GenerationToTrigger < 0 || dumper.GenerationToTrigger > 2)
                        {
                            Console.WriteLine("Invalid value for /GenerationToTrigger.  Value must be between 0 and 2 inclusively.");
                            goto Usage;
                        }
                        Console.WriteLine("Generation To Trigger: " + dumper.GenerationToTrigger);
                    }
                    else
                    {
                        Console.WriteLine("Unknown qualifier: {0}", arg);
                        goto Usage;
                    }
                }
                else
                {
                    if (inputSpec == null)
                    {
                        inputSpec = arg;
                    }
                    else if (outputFile == null)
                    {
                        outputFile = arg;
                    }
                    else
                    {
                        Console.WriteLine("Extra parameter: {0}", arg);
                        return -1;
                    }
                }
            }

            if (inputSpec == null)
            {
                goto Usage;
            }

            if (dumper.DumpData)
            {
                Console.WriteLine("WARNING: Currently DumpData is not supported");
            }

            if (!forceGC)
            {
                if (outputFile == null)
                {
                    outputFile = Path.ChangeExtension(inputSpec, ".gcDump");
                }

                // This avoids file sharing issues, and also insures that old files are not left behind.  
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
            }

            if (processDump)
            {
                Console.WriteLine("Creating heap dump {0} from process dump {1}.", outputFile, inputSpec);

                dumper.DumpHeapFromProcessDump(inputSpec, outputFile);
                // TODO FIX NOW REMOVE GCHeap.DumpHeapFromProcessDump(inputSpec, outputFile, Console.Out);
            }
            else
            {
                var processID = GetProcessID(inputSpec);
                if (processID < 0)
                {
                    Console.WriteLine("Error: Could not find process {0}", inputSpec);
                    return 4;
                }

                if (PointerSizeForProcess(processID) != Marshal.SizeOf(typeof(IntPtr)))
                {
                    throw new ApplicationException("The debuggee process has a different bitness (32-64) than the debugger.");
                }

                if (forceGC)
                {
                    dumper.ForceGC(processID);
                    return 0;
                }

                Console.WriteLine("Dumping process {0} with id {1}.", inputSpec, processID);
                dumper.DumpLiveHeap(processID, outputFile);
            }

            if (!File.Exists(outputFile))
            {
                Console.WriteLine("No output file {0} created.", outputFile);
                return 2;
            }
            return 0;
            Usage:
            Console.WriteLine("Usage: HeapDump [/MaxDumpCountK=n /Freeze] ProcessIdOrName OutputHeapDumpFile");
            Console.WriteLine("Usage: HeapDump [/MaxDumpCountK=n] /processDump  DumpFile OutputHeapDumpFile");
            Console.WriteLine("Usage: HeapDump /forceGC ProcessIdOrName");
            return 1;
        }
        catch (Exception e)
        {
            if (e is ApplicationException)
            {
                Console.WriteLine("HeapDump Error: {0}", e.Message);
            }
            else
            {
                Console.WriteLine("HeapDump Error ({0}): {1}", e.HResult, e.ToString());
            }

            if (outputFile != null)
            {
                try { File.Delete(outputFile); }
                catch (Exception) { }
            }
        }
        return 1;
    }

    #region private
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
        Process youngestProcess = null;
        // remove .exe if present.
        if (processNameOrID.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processNameOrID = processNameOrID.Substring(0, processNameOrID.Length - 4);
        }

        foreach (var process in Process.GetProcessesByName(processNameOrID))
        {
            if (youngestProcess == null || process.StartTime > youngestProcess.StartTime)
            {
                youngestProcess = process;
            }
        }
        if (youngestProcess != null)
        {
            return youngestProcess.Id;
        }

        return -1;
    }

    private static int PointerSizeForProcess(int processID)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return 4;
        }

        var process = Process.GetProcessById(processID);
        bool is32Bit = false;
        if (!IsWow64Process(process.Handle, out is32Bit))
        {
            throw new ApplicationException("Could not access process " + processID + " to determine target process architecture.");
        }

        GC.KeepAlive(process);
        return is32Bit ? 4 : 8;
    }

    [DllImport("kernel32.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(
         [In] IntPtr processHandle,
         [Out, MarshalAs(UnmanagedType.Bool)] out bool wow64Process);
    #endregion
}

