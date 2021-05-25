using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Compatibility;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Symbols { } // avoids compile errors in .NET Core build

namespace Microsoft.Diagnostics.Tracing
{
#if !NOT_WINDOWS 
    /// <summary>
    /// ZippedETLWriter is a helper class used to compress ETW data (ETL files)
    /// along with symbolic information (e.g. NGEN pdbs), as well as other optional
    /// metadata (e.g. collection log files), into a single archive ready for 
    /// transfer to another machine.   
    /// </summary>
    public class ZippedETLWriter
    {
        /// <summary>
        /// Declares the intent to write a new ZIP archive that will
        /// contain ETW file 'etlFilePath' in it as well as symbolic information (NGEN
        /// pdbs) and possibly other information.   log is a Text stream to send detailed
        /// information to.  
        /// <para>
        /// This routine assumes by default (unless Merge is set to false) that the ETL 
        /// file needs to be merged before it is archived.   It will also generate all
        /// the NGEN pdbs needed for the archive.   
        /// </para>
        /// <para>
        /// You must call the WriteArchive method before any operations actually happen. 
        /// Up to that point is is just remembering instructions for WriteArchive to
        /// follow.  
        /// </para>
        /// </summary>
        public ZippedETLWriter(string etlFilePath, TextWriter log = null)
        {
            m_etlFilePath = etlFilePath;
            Merge = true;
            NGenSymbolFiles = true;
            DeleteInputFile = true;
            Zip = true;
            Log = log;
        }

        /// <summary>
        /// This is the name of the output archive.  By default is the same as the ETL file name 
        /// with a .zip' suffix added (thus it will typically be .etl.zip).  
        /// </summary>
        public string ZipArchivePath { get; set; }

        /// <summary>
        /// If set this is where messages about progress and detailed error information goes.  
        /// While you dont; have to set this, it is a good idea to do so.  
        /// </summary>
        public TextWriter Log { get; set; }

        /// <summary>
        /// By default ZippedETL file will zip the ETL file itself and the NGEN pdbs associated with it.
        /// You can add additional files to the archive by calling AddFile.   In specififed 'archivePath' 
        /// is the path in the archive and defaults to just the file name of the original file path.  
        /// </summary>
        public void AddFile(string filePath, string archivePath = null)
        {
            // Just remember it for the WriteArchive operation.  
            if (m_additionalFiles == null)
            {
                m_additionalFiles = new List<Tuple<string, string>>();
            }

            if (archivePath == null)
            {
                archivePath = Path.GetFileName(filePath);
            }

            m_additionalFiles.Add(new Tuple<string, string>(filePath, archivePath));
        }

        /// <summary>
        /// Actually do the work specified by the ZippedETLWriter constructors and other methods.  
        /// </summary>
        public bool WriteArchive(CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            List<string> pdbFileList = PrepForWrite();

            if (!Zip)
            {
                return true;
            }

            // Any generated PDBs were written alongside the existing PDBs, so if existing PDBs are requested, produce the combined list, and overwrite the generated list.
            if (IncludeExistingPDBs)
            {
                string[] existingPDBs = Directory.GetFiles(Path.GetDirectoryName(m_etlFilePath), "*.ni.pdb", SearchOption.AllDirectories);
                pdbFileList = new List<string>(existingPDBs);
            }

            bool success = false;
            var sw = Stopwatch.StartNew();
            if (ZipArchivePath == null)
            {
                ZipArchivePath = m_etlFilePath + ".zip";
            }

            var newFileName = ZipArchivePath + ".new";
            FileUtilities.ForceDelete(newFileName);
            try
            {
#if !NETSTANDARD1_6
                if (LowPriority)
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                }
#endif
                Log.WriteLine("[Zipping ETL file {0}]", m_etlFilePath);
                using (var zipArchive = ZipFile.Open(newFileName, ZipArchiveMode.Create))
                {
                    zipArchive.CreateEntryFromFile(m_etlFilePath, Path.GetFileName(m_etlFilePath), compressionLevel);
                    if (pdbFileList != null)
                    {
                        Log.WriteLine("[Writing {0} PDBS to Zip file]", pdbFileList.Count);
                        // Add the Pdbs to the archive 
                        foreach (var pdb in pdbFileList)
                        {
                            // If the path looks like a sym server cache path, grab that chunk, otherwise just copy the file name part of the path.  
                            string relativePath;
                            var m = Regex.Match(pdb, @"\\([^\\]+.pdb\\\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\d+\\[^\\]+)$");
                            if (m.Success)
                            {
                                relativePath = m.Groups[1].Value;
                            }
                            else
                            {
                                relativePath = Path.GetFileName(pdb);
                            }

                            var archivePath = Path.Combine("symbols", relativePath);

                            // log.WriteLine("Writing PDB {0} to archive.", archivePath);
                            zipArchive.CreateEntryFromFile(pdb, archivePath, compressionLevel);
                        }

                        // This is not the very end of ZIP generation, but I want the log to be IN the ZIP so this 
                        // is the last point I can do this. 
                        Log.WriteLine("ZIP generation took {0:f3} sec", sw.Elapsed.TotalSeconds);
                        Log.WriteLine("ZIP output file {0}", ZipArchivePath);
                        Log.WriteLine("Time: {0}", DateTime.Now);
                        Log.Flush();
                    }

                    if (m_additionalFiles != null)
                    {
                        foreach (Tuple<string, string> additionalFile in m_additionalFiles)
                        {
                            // We dont use CreatEntryFromFile because it will not open files thar are open for writting.  
                            // Since a typical use of this is to write the log file, which will be open for writing, we 
                            // use File.Open and allow this case explicitly. 
                            using (Stream fs = File.Open(additionalFile.Item1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                // Item2 tells you the path in the archive.  
                                var entry = zipArchive.CreateEntry(additionalFile.Item2, compressionLevel);
                                using (Stream es = entry.Open())
                                {
                                    fs.CopyTo(es);
                                }
                            }
                        }
                    }
                }
                FileUtilities.ForceMove(newFileName, ZipArchivePath);
                if (DeleteInputFile)
                {
                    Log.WriteLine("Deleting original ETL file {0}", m_etlFilePath);
                    FileUtilities.ForceDelete(m_etlFilePath);
                }

                // We make the ZIP the same time as the original file.
                File.SetLastWriteTimeUtc(ZipArchivePath, File.GetLastWriteTimeUtc(m_etlFilePath));
                success = true;
            }
            finally
            {
#if !NETSTANDARD1_6
                Thread.CurrentThread.Priority = ThreadPriority.Normal;
#endif
                FileUtilities.ForceDelete(newFileName);
            }
            return success;
        }

        // Overriding the normal defaults.  
        /// <summary>
        /// This is the symbol reader that is used to generate the NGEN Pdbs as needed
        /// If it is not specififed one is created on the fly.  
        /// </summary>
        public SymbolReader SymbolReader { get; set; }

        /// <summary>
        /// By default the ETL file is merged before being added to the archive.  If
        /// this is not necessary, you can set this to false.   
        /// </summary>
        public bool Merge { get; set; }

        /// <summary>
        /// By default there are a number of steps to merging an ETL file.  Sometimes,
        /// it is desirable to only perform ImageID merging.  If desired, set this to true.
        /// </summary>
        public bool MergeImageIDsOnly { get; set; }

        /// <summary>
        /// Uses a compressed format for the ETL file.   Normally off.  
        /// </summary>
        public bool CompressETL { get; set; }

        /// <summary>
        /// By default the symbol files (PDBs) are included in the ZIP file.   If this
        /// is not desired for whatever reason, this property can be set to false.  
        /// </summary>
        public bool NGenSymbolFiles { get; set; }

        /// <summary>
        /// Do the work at low priority so as to avoid impacting the system. 
        /// </summary>
        public bool LowPriority { get; set; }

        /// <summary>
        /// Normally WriteArchive creates a ZIP archive.  However it is possible that you only wish
        /// to do the merging and NGEN symbol generation.   Setting this property to false
        /// will suppress the final ZIP operation.  
        /// </summary>
        public bool Zip { get; set; }

        /// <summary>
        /// Normally if you ZIP you will delete the original ETL file.  Setting this to false overrides this.  
        /// </summary>
        public bool DeleteInputFile { get; set; }

        /// <summary>
        /// When merging an ETL for the first time, we might generate some NGEN PDBs and save them as part of the archive.
        /// When merging the second time, use this option to make sure that the PDBs that were part of the original archive are included in the new archive.
        /// </summary>
        public bool IncludeExistingPDBs { get; set; }

        #region private
        private List<string> PrepForWrite()
        {
            // If the user did not specify a place to put log messages, make one for them.  
            if (Log == null)
            {
                Log = new StringWriter();
            }

            Stopwatch sw = Stopwatch.StartNew();

            // Compute input & temp files.
            var dir = Path.GetDirectoryName(m_etlFilePath);
            if (dir.Length == 0)
            {
                dir = ".";
            }

            var baseName = Path.GetFileNameWithoutExtension(m_etlFilePath);
            List<string> mergeInputs = new List<string>();
            mergeInputs.Add(m_etlFilePath);
            mergeInputs.AddRange(Directory.GetFiles(dir, baseName + ".kernel*.etl"));
            mergeInputs.AddRange(Directory.GetFiles(dir, baseName + ".clr*.etl"));
            mergeInputs.AddRange(Directory.GetFiles(dir, baseName + ".user*.etl"));
            string tempName = Path.ChangeExtension(m_etlFilePath, ".etl.new");
            List<string> pdbFileList = null;

            try
            {
                // Do the merge and NGEN pdb lookup in parallel 
                Task mergeWorker = Task.Factory.StartNew(delegate
                {
                    if (Merge)
                    {
                        var startTime = DateTime.UtcNow;
#if !NETSTANDARD1_6
                        if (LowPriority)
                        {
                            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                        }
#endif
                        try
                        {
                            Log.WriteLine("Starting Merging of {0}", m_etlFilePath);

                            var options = Session.TraceEventMergeOptions.None;
                            if (CompressETL)
                            {
                                options |= Session.TraceEventMergeOptions.Compress;
                            }
                            else if(MergeImageIDsOnly)
                            {
                                options |= Session.TraceEventMergeOptions.ImageIDsOnly;
                            }

                            // Do the merge
                            Session.TraceEventSession.Merge(mergeInputs.ToArray(), tempName, options);
                            Log.WriteLine("Merging took {0:f1} sec", (DateTime.UtcNow - startTime).TotalSeconds);
                        }
                        finally
                        {
#if !NETSTANDARD1_6
                            Thread.CurrentThread.Priority = ThreadPriority.Normal;
#endif
                        }
                    }
                    else
                    {
                        Log.WriteLine("Merge == false, skipping Merge operation.");
                    }
                });

                // If we are running low priority, don't do work in parallel, so wait merging before doing NGEN pdb generation. 
                if (LowPriority)
                {
                    Log.WriteLine("Running Low Priority, Starting merge and waiting for it to complete before doing NGEN PDB generation.");
                    mergeWorker.Wait();
                    Log.WriteLine("Running Low Priority, Finished Merging, moving on to NGEN PDB generation.");
                }

                Task pdbWorker = Task.Factory.StartNew(delegate
                {
                    if (NGenSymbolFiles)
                    {
#if !NETSTANDARD1_6
                        if (LowPriority)
                        {
                            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                        }
#endif
                        try
                        {
                            var startTime = DateTime.UtcNow;
                            Log.WriteLine("Starting Generating NGEN pdbs for {0}", m_etlFilePath);
                            var symbolReader = SymbolReader;
                            if (symbolReader == null)
                            {
                                symbolReader = new SymbolReader(Log);
                            }

                            pdbFileList = GetNGenPdbs(m_etlFilePath, symbolReader, Log);
                            Log.WriteLine("Generating NGEN Pdbs took {0:f1} sec", (DateTime.UtcNow - startTime).TotalSeconds);
                        }
                        finally
                        {
#if !NETSTANDARD1_6
                            Thread.CurrentThread.Priority = ThreadPriority.Normal;
#endif
                        }
                    }
                    else
                    {
                        Log.WriteLine("NGenSymbolFiles == false, skipping NGEN pdb generation");
                    }
                });
                Task.WaitAll(mergeWorker, pdbWorker);

                if (File.Exists(tempName))
                {
                    // Delete/move the original files after the two worker threads finished execution to avoid races.
                    foreach (var mergeInput in mergeInputs)
                    {
                        FileUtilities.ForceDelete(mergeInput);
                    }

                    Log.WriteLine("Moving {0} to {1}", tempName, m_etlFilePath);
                    // Place the output in its final resting place.  
                    FileUtilities.ForceMove(tempName, m_etlFilePath);
                }
            }
            finally
            {
                Log.WriteLine("Deleting temp file");
                if (File.Exists(tempName))
                {
                    File.Delete(tempName);
                }
            }

            sw.Stop();
            Log.WriteLine("Merge and NGEN PDB Generation took {0:f3} sec.", sw.Elapsed.TotalSeconds);
            Log.WriteLine("Merge output file {0}", m_etlFilePath);

            return pdbFileList;
        }

        /// <summary>
        /// Returns the list of path names to the NGEN pdbs for any NGEN image in 'etlFile' that has
        /// any samples in it.   
        /// </summary>
        internal static List<string> GetNGenPdbs(string etlFile, SymbolReader symbolReader, TextWriter log)
        {
            // Generate the NGen images for any NGEN image needing symbolic information.   
            var pdbFileList = new List<string>(100);
            foreach (var imageName in ETWTraceEventSource.GetModulesNeedingSymbols(etlFile))
            {
                var sw = Stopwatch.StartNew();
                var pdbName = symbolReader.GenerateNGenSymbolsForModule(imageName);
                if (pdbName != null)
                {
                    pdbFileList.Add(pdbName);
                    log.WriteLine("Found NGEN pdb {0}", pdbName);
                }
                log.WriteLine("NGEN PDB creation for {0} took {1:n2} Sec", imageName, sw.Elapsed.TotalSeconds);
            }
            return pdbFileList;
        }

        private List<Tuple<string, string>> m_additionalFiles;
        private string m_etlFilePath;
        #endregion // private
    }
#endif

    /// <summary>
    /// ZippedETLReader is a helper class that unpacks the ZIP files generated
    /// by the ZippedETLWriter class.    It can be smart about placing the 
    /// symbolic information in these files on the SymbolReader's path so that
    /// symbolic lookup 'just works'.  
    /// </summary>
    public class ZippedETLReader
    {
        /// <summary>
        /// Declares the intent to unzip an .ETL.ZIP file that contain an compressed ETL file 
        /// (and NGEN pdbs) from the archive at 'zipFilePath'.   If present, messages about
        /// the unpacking go to 'log'.   Note that this unpacking only happens when the
        /// UnpackArchive() method is called.  
        /// </summary>
        public ZippedETLReader(string zipFilePath, TextWriter log = null)
        {
            m_zipFilePath = zipFilePath;
            Log = log;
        }

        /// <summary>
        /// If set messages about unpacking go here. 
        /// </summary>
        public TextWriter Log { get; set; }

        /// <summary>
        /// The name of the ETL file to extract (it is an error if there is not exactly 1).  
        /// If not present it is derived by changing the extension of the zip archive. 
        /// </summary>
        public string EtlFileName { get; set; }

        /// <summary>
        /// Where to put the symbols.  
        /// </summary>
        public string SymbolDirectory { get; set; }

        /// <summary>
        /// After setting any properties to override default behavior, calling this method
        /// will actually do the unpacking.  
        /// </summary>
        public void UnpackArchive()
        {
            if (Log == null)
            {
                Log = new StringWriter();
            }

            if (EtlFileName == null)
            {
                if (m_zipFilePath.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
                {
                    EtlFileName = m_zipFilePath.Substring(0, m_zipFilePath.Length - 4);
                }
                else
                {
                    EtlFileName = Path.ChangeExtension(m_zipFilePath, ".etl");
                }
            }

            if (SymbolDirectory == null)
            {
                SymbolDirectory = new SymbolPath(SymbolPath.SymbolPathFromEnvironment).DefaultSymbolCache();
            }

            Stopwatch sw = Stopwatch.StartNew();
            Log.WriteLine("[Decompressing {0}]", m_zipFilePath);
            Log.WriteLine("Generating output file {0}", EtlFileName);
            using (var zipArchive = ZipFile.OpenRead(m_zipFilePath))
            {
                bool seenEtlFile = false;
                foreach (var entry in zipArchive.Entries)
                {
                    if (entry.Length == 0)  // Skip directories. 
                    {
                        continue;
                    }

                    var archivePath = entry.FullName;
                    if (archivePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        archivePath = archivePath.Replace('/', '\\');     // normalize separator convention 
                        string pdbRelativePath = null;
                        if (archivePath.StartsWith(@"symbols\", StringComparison.OrdinalIgnoreCase))
                        {
                            pdbRelativePath = archivePath.Substring(8);
                        }
                        else if (archivePath.StartsWith(@"ngenpdbs\", StringComparison.OrdinalIgnoreCase))
                        {
                            pdbRelativePath = archivePath.Substring(9);
                        }
                        else
                        {
                            var m = Regex.Match(archivePath, @"^[^\\]+\.ngenpdbs?\\(.*)", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                pdbRelativePath = m.Groups[1].Value;
                            }
                            else
                            {
                                // .diagsession files (created by the Visual Studio Diagnostic Hub) put PDBs in a path like
                                // 194BAE98-C4ED-470E-9204-1F9389FC9DC1\symcache\xyz.pdb
                                m = Regex.Match(archivePath, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\(?:sym|pdb)cache\\(.*)", RegexOptions.IgnoreCase);
                                if (m.Success)
                                {
                                    pdbRelativePath = m.Groups[1].Value;
                                }
                                else
                                {
                                    Log.WriteLine("WARNING: found PDB file that was not in a symbol server style directory, skipping extraction");
                                    Log.WriteLine("         Unzip this ETL and PDB by hand to use this PDB.");
                                    continue;
                                }
                            }
                        }

                        var pdbTargetPath = Path.Combine(SymbolDirectory, pdbRelativePath);
                        var pdbTargetName = Path.GetFileName(pdbTargetPath);
                        if (File.Exists(pdbTargetPath) && (new System.IO.FileInfo(pdbTargetPath).Length == entry.Length))
                        {
                            Log.WriteLine("PDB {0} exists, skipping", pdbRelativePath);
                            continue;
                        }

                        // There is a possibility that you want to put symbol file using symbol server conventions
                        // (in which case it is X.pdb\NNNN\X.pdb, but you already have a file named X.pdb)  detect
                        // this and delete the file if necessary.  
                        var firstNameInRelativePath = pdbRelativePath;
                        var sepIdx = firstNameInRelativePath.IndexOf('\\');
                        if (sepIdx >= 0)
                        {
                            firstNameInRelativePath = firstNameInRelativePath.Substring(0, sepIdx);
                        }

                        var firstNamePath = Path.Combine(SymbolDirectory, firstNameInRelativePath);
                        if (File.Exists(firstNamePath))
                        {
                            Log.WriteLine("Deleting pdb file that is in the way {0}", firstNamePath);
                            FileUtilities.ForceDelete(firstNamePath);
                        }

                        Log.WriteLine("Extracting PDB {0}", pdbRelativePath);
                        AtomicExtract(entry, pdbTargetPath);
                    }
                    else if (archivePath.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenEtlFile)
                        {
                            throw new ApplicationException("The ZIP file does not have exactly 1 ETL file in it, can't auto-extract.");
                        }

                        seenEtlFile = true;
                        AtomicExtract(entry, EtlFileName);
                        Log.WriteLine("Extracting {0} Zipped size = {1:f3} MB Unzipped = {2:f3} MB", EtlFileName,
                            entry.CompressedLength / 1000000.0, entry.Length / 1000000.0);
                    }
                    else if (archivePath == "PerfViewLogFile.txt" || archivePath == "LogFile.txt")      // TODO we can remove the PerfViewLogFile.txt eventually (say in 2019)
                    {
                        string logFilePath = Path.ChangeExtension(EtlFileName, ".LogFile.txt");
                        Log.WriteLine("Extracting LogFile.txt to {0}", logFilePath);
                        AtomicExtract(entry, logFilePath);
                    }
                    else
                    {
                        Log.WriteLine("Skipping unknown file {0}", archivePath);
                        // TODO do something with these?
                    }
                }
                if (!seenEtlFile)
                {
                    throw new ApplicationException("The ZIP file does not have any ETL files in it!");
                }

                Log.WriteLine("Finished decompression, took {0:f0} sec", sw.Elapsed.TotalSeconds);
            }
        }

        #region private
        // Extract to a temp file and move so we get atomic update.   Otherwise if things are
        // interrupted half way through we confuse algorithms that do nothing if a file is 
        // already present.  
        private static void AtomicExtract(ZipArchiveEntry zipEntry, string targetPath)
        {
            // Insure directory exists. 
            var dirName = Path.GetDirectoryName(targetPath);
            if (dirName.Length != 0)
            {
                Directory.CreateDirectory(dirName);
            }

            var extractPath = targetPath + ".new";
            try
            {
                zipEntry.ExtractToFile(extractPath, true);
                File.SetLastWriteTime(extractPath, DateTime.Now);              // Touch the file
                FileUtilities.ForceMove(extractPath, targetPath);
            }
            finally
            {
                FileUtilities.ForceDelete(extractPath);
            }
        }

        private string m_zipFilePath;
        #endregion // private
    }
}