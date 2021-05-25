using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Utilities;

namespace Microsoft.Diagnostics.Symbols
{
    /// <summary>
    /// A symbol reader represents something that can FIND pdbs (either on a symbol server or via a symbol path)
    /// Its job is to find a full path a PDB.  Then you can use OpenSymbolFile to get a SymbolReaderModule and do more. 
    /// </summary>
    public sealed unsafe class SymbolReader : IDisposable
    {
        /// <summary>
        /// Opens a new SymbolReader.   All diagnostics messages about symbol lookup go to 'log'.  
        /// </summary>
        public SymbolReader(TextWriter log, string nt_symbol_path = null)
        {
            m_log = log;
#if SYNC_SYMBOLREADER_LOG
            // Make sure that accesses to the log are synchronized to avoid races due to the fact that System.Diagnostics.Process
            // uses AsyncStreamReader to read from the stdout/stderr and so it's possible to have concurrent writes to this log.
            m_log = TextWriter.Synchronized(log);
#endif
            m_symbolModuleCache = new Cache<string, ManagedSymbolModule>(10);
            m_pdbPathCache = new Cache<PdbSignature, string>(10);

            m_symbolPath = nt_symbol_path;
            if (m_symbolPath == null)
            {
                m_symbolPath = Microsoft.Diagnostics.Symbols.SymbolPath.SymbolPathFromEnvironment;
            }

            m_log.WriteLine("Created SymbolReader with SymbolPath {0}", m_symbolPath);

            // TODO FIX NOW.  the code below does not support probing a file extension directory.  
            // we work around this by adding more things to the symbol path
            var symPath = new SymbolPath(SymbolPath);
            var newSymPath = new SymbolPath();
            foreach (var symElem in symPath.Elements)
            {
                newSymPath.Add(symElem);
                if (!symElem.IsSymServer)
                {
                    var probe = Path.Combine(symElem.Target, "dll");
                    if (Directory.Exists(probe))
                    {
                        newSymPath.Add(probe);
                    }

                    probe = Path.Combine(symElem.Target, "exe");
                    if (Directory.Exists(probe))
                    {
                        newSymPath.Add(probe);
                    }
                }
            }
            var newSymPathStr = newSymPath.ToString();
            m_symbolPath = newSymPathStr;
        }

        // These routines find a PDB based on something (either an DLL or a PDB 'signature')
        /// <summary>
        /// Finds the symbol file for 'exeFilePath' that exists on the current machine (we open
        /// it to find the needed info).   Uses the SymbolReader.SymbolPath (including Symbol servers) to 
        /// look up the PDB, and will download the PDB to the local cache if necessary.   It will also
        /// generate NGEN pdbs into the local symbol cache unless SymbolReaderFlags.NoNGenPDB is set.   
        /// 
        /// By default for NGEN images it returns the NGEN pdb.  However if 'ilPDB' is true it returns
        /// the IL PDB.  
        /// 
        /// Returns null if the pdb can't be found.  
        /// </summary>
        public string FindSymbolFilePathForModule(string dllFilePath, bool ilPDB = false)
        {
            m_log.WriteLine("FindSymbolFilePathForModule: searching for PDB for DLL {0}.", dllFilePath);
            try
            {
                dllFilePath = BypassSystem32FileRedirection(dllFilePath);
                if (File.Exists(dllFilePath))
                {
                    using (var peFile = new PEFile.PEFile(dllFilePath))
                    {
                        string pdbName;
                        Guid pdbGuid;
                        int pdbAge;
                        if (peFile.GetPdbSignature(out pdbName, out pdbGuid, out pdbAge, !ilPDB))
                        {
                            string fileVersionString = null;
                            var fileVersion = peFile.GetFileVersionInfo();
                            if (fileVersion != null)
                            {
                                fileVersionString = fileVersion.FileVersion;
                            }

                            var ret = FindSymbolFilePath(pdbName, pdbGuid, pdbAge, dllFilePath, fileVersionString);
                            if (ret == null && (0 <= dllFilePath.IndexOf(".ni.", StringComparison.OrdinalIgnoreCase) || peFile.IsManagedReadyToRun))
                            {
                                if ((Options & SymbolReaderOptions.NoNGenSymbolCreation) != 0)
                                {
                                    m_log.WriteLine("FindSymbolFilePathForModule: Could not find NGEN image, NoNGenPdb set, giving up.");
                                }
                                else
                                {
                                    m_log.WriteLine("FindSymbolFilePathForModule: Could not find PDB for NGEN image, Trying to generate it.");
                                    ret = GenerateNGenSymbolsForModule(Path.GetFullPath(dllFilePath));
                                }
                            }
                            m_log.WriteLine("FindSymbolFilePathForModule returns {0} for {1} {2} {3} {4}", ret ?? "NULL", pdbName, pdbGuid, pdbAge, fileVersionString ?? "NULL");
                            return ret;
                        }
                        else
                        {
                            m_log.WriteLine("FindSymbolFilePathForModule: {0} does not have a codeview debug signature.", dllFilePath);
                        }
                    }
                }
                else
                {
                    m_log.WriteLine("FindSymbolFilePathForModule: {0} does not exist.", dllFilePath);
                }
            }
            catch (Exception e)
            {
                m_log.WriteLine("FindSymbolFilePathForModule: Failure opening PE file: {0}", e.Message);
            }

            m_log.WriteLine("[Failed to find PDB file for DLL {0}]", dllFilePath);
            return null;
        }
        /// <summary>
        /// Find the complete PDB path, given just the simple name (filename + pdb extension) as well as its 'signature', 
        /// which uniquely identifies it (on symbol servers).   Uses the SymbolReader.SymbolPath (including Symbol servers) to 
        /// look up the PDB, and will download the PDB to the local cache if necessary.  
        /// 
        /// A Guid of Empty, means 'unknown' and will match the first PDB that matches simple name.  Thus it is unsafe. 
        /// 
        /// Returns null if the PDB could  not be found
        /// </summary>
        /// <param name="pdbFileName">The name of the PDB file (we only use the file name part)</param>
        /// <param name="pdbIndexGuid">The GUID that is embedded in the DLL in the debug information that allows matching the DLL and the PDB</param>
        /// <param name="pdbIndexAge">Tools like BBT transform a DLL into another DLL (with the same GUID) the 'pdbAge' is a small integers
        /// that indicates how many transformations were done</param>
        /// <param name="dllFilePath">If you know the path to the DLL for this pdb add it here.  That way we can probe next to the DLL
        /// for the PDB file.</param>
        /// <param name="fileVersion">This is an optional string that identifies the file version (the 'Version' resource information.  
        /// It is used only to provided better error messages for the log.</param>
        public string FindSymbolFilePath(string pdbFileName, Guid pdbIndexGuid, int pdbIndexAge, string dllFilePath = null, string fileVersion = "", bool portablePdbMatch = false)
        {
            m_log.WriteLine("FindSymbolFilePath: *{{ Locating PDB {0} GUID {1} Age {2} Version {3}", pdbFileName, pdbIndexGuid, pdbIndexAge, fileVersion);
            if (dllFilePath != null)
                m_log.WriteLine("FindSymbolFilePath: Pdb is for DLL {0}", dllFilePath);

            PdbSignature pdbSig = new PdbSignature() { Name = pdbFileName, ID = pdbIndexGuid, Age = pdbIndexAge };
            string pdbPath = null;
            if (m_pdbPathCache.TryGet(pdbSig, out pdbPath))
            {
                m_log.WriteLine("FindSymbolFilePath: }} Hit Cache, returning {0}", pdbPath != null ? pdbFileName : "NULL");
                return pdbPath;
            }

            string pdbIndexPath = null;
            string pdbSimpleName = Path.GetFileName(pdbFileName);        // Make sure the simple name is really a simple name

            // If we have a dllPath, look right beside it, or in a directory symbols.pri\retail\dll
            if (pdbPath == null && dllFilePath != null)        // Check next to the file. 
            {
                m_log.WriteLine("FindSymbolFilePath: Checking relative to DLL path {0}", dllFilePath);
                string pdbPathCandidate = Path.Combine(Path.GetDirectoryName(dllFilePath), Path.GetFileName(pdbFileName));
                if (PdbMatches(pdbPathCandidate, pdbIndexGuid, pdbIndexAge))
                {
                    pdbPath = pdbPathCandidate;
                }

                // Also try the symbols.pri\retail\dll convention that windows and devdiv use
                if (pdbPath == null)
                {
                    pdbPathCandidate = Path.Combine(
                        Path.GetDirectoryName(dllFilePath), @"symbols.pri\retail\dll\" +
                        Path.GetFileName(pdbFileName));
                    if (PdbMatches(pdbPathCandidate, pdbIndexGuid, pdbIndexAge))
                    {
                        pdbPath = pdbPathCandidate;
                    }
                }

                if (pdbPath == null)
                {
                    pdbPathCandidate = Path.Combine(
                        Path.GetDirectoryName(dllFilePath), @"symbols\retail\dll\" +
                        Path.GetFileName(pdbFileName));
                    if (PdbMatches(pdbPathCandidate, pdbIndexGuid, pdbIndexAge))
                    {
                        pdbPath = pdbPathCandidate;
                    }
                }
            }

            // If the pdbPath is a full path, see if it exists 
            if (pdbPath == null && 0 < pdbFileName.IndexOf('\\'))
            {
                if (PdbMatches(pdbFileName, pdbIndexGuid, pdbIndexAge))
                {
                    pdbPath = pdbFileName;
                }
            }

            // Did not find it locally, 
            if (pdbPath == null)
            {
                SymbolPath path = new SymbolPath(SymbolPath);
                foreach (SymbolPathElement element in path.Elements)
                {
                    // TODO can do all of these concurrently now.   
                    if (element.IsSymServer)
                    {
                        if (pdbIndexPath == null)
                        {
                            // symbolsource.org and nuget.smbsrc.net only support upper case of pdbIndexGuid
                            pdbIndexPath = pdbSimpleName + @"\" + pdbIndexGuid.ToString("N").ToUpper() + pdbIndexAge.ToString() + @"\" + pdbSimpleName;
                        }

                        string cache = element.Cache;
                        if (cache == null)
                        {
                            cache = path.DefaultSymbolCache();
                        }

                        pdbPath = GetFileFromServer(element.Target, pdbIndexPath, Path.Combine(cache, pdbIndexPath));

                        if (pdbPath == null && portablePdbMatch)
                        {
                            // pdb key will look like:
                            // Assuming 1bc56133-5645-4d28-90dd-6f12c66240ac as the index guid
                            // Foo.pdb/1bc5613356454d2890dd6f12c66240acFFFFFFFF/Foo.pdb will be the path
                            pdbPath = GetFileFromServer(element.Target, pdbSimpleName + @"\" + pdbIndexGuid.ToString("N").ToUpper() + "FFFFFFFF" + @"\" + pdbSimpleName, Path.Combine(cache, pdbIndexPath));
                        }
                    }
                    else
                    {
                        string filePath = Path.Combine(element.Target, pdbSimpleName);
                        if ((Options & SymbolReaderOptions.CacheOnly) == 0 || !element.IsRemote)
                        {
                            // TODO can stall if the path is a remote path.   
                            if (PdbMatches(filePath, pdbIndexGuid, pdbIndexAge, false))
                            {
                                pdbPath = filePath;
                            }
                        }
                        else
                        {
                            m_log.WriteLine("FindSymbolFilePath: location {0} is remote and cacheOnly set, giving up.", filePath);
                        }
                    }
                    if (pdbPath != null)
                    {
                        break;
                    }
                }
            }

            if (pdbPath != null)
            {
                if (OnSymbolFileFound != null)
                {
                    OnSymbolFileFound(pdbPath, pdbIndexGuid, pdbIndexAge);
                }

                m_log.WriteLine("FindSymbolFilePath: *}} Successfully found PDB {0} GUID {1} Age {2} Version {3}", pdbPath, pdbIndexGuid, pdbIndexAge, fileVersion);
            }
            else
            {
                string where = "";
                if ((Options & SymbolReaderOptions.CacheOnly) != 0)
                {
                    where = " in local cache";
                }

                m_log.WriteLine("FindSymbolFilePath: *}} Failed to find PDB {0}{1} GUID {2} Age {3} Version {4}", pdbSimpleName, where, pdbIndexGuid, pdbIndexAge, fileVersion);
            }

            m_pdbPathCache.Add(pdbSig, pdbPath);
            return pdbPath;
        }

        // Find an executable file path (not a PDB) based on information about the file image.  
        /// <summary>
        /// This API looks up an executable file, by its build-timestamp and size (on a symbol server),  'fileName' should be 
        /// a simple name (no directory), and you need the buildTimeStamp and sizeOfImage that are found in the PE header.
        /// 
        /// Returns null if it cannot find anything.  
        /// </summary>
        public string FindExecutableFilePath(string fileName, int buildTimestamp, int sizeOfImage, bool sybmolServerOnly = false)
        {
            string exeIndexPath = null;
            SymbolPath path = new SymbolPath(SymbolPath);
            foreach (SymbolPathElement element in path.Elements)
            {
                if (element.IsSymServer)
                {
                    if (exeIndexPath == null)
                    {
                        exeIndexPath = fileName + @"\" + buildTimestamp.ToString("x") + sizeOfImage.ToString("x") + @"\" + fileName;
                    }

                    string cache = element.Cache;
                    if (cache == null)
                    {
                        cache = path.DefaultSymbolCache();
                    }

                    string targetPath = GetFileFromServer(element.Target, exeIndexPath, Path.Combine(cache, exeIndexPath));
                    if (targetPath != null)
                    {
                        return targetPath;
                    }
                }
                else if (!sybmolServerOnly)
                {
                    string filePath = Path.Combine(element.Target, fileName);
                    m_log.WriteLine("Probing file {0}", filePath);
                    if (File.Exists(filePath))
                    {
                        using (PEFile.PEFile file = new PEFile.PEFile(filePath))
                        {
                            if ((file.Header.TimeDateStampSec == buildTimestamp) && (file.Header.SizeOfImage == sizeOfImage))
                            {
                                return filePath;
                            }

                            m_log.WriteLine("Found file {0} but file timestamp:size {1}:{2} != desired {3}:{4}, rejecting.",
                                filePath, file.Header.TimeDateStampSec, file.Header.SizeOfImage, buildTimestamp, sizeOfImage);
                        }
                    }
                }
            }
            return null;
        }

        // Once you have a file path to a PDB file, you can open it with this method
        /// <summary>
        /// Given the path name to a particular PDB file, load it so that you can resolve symbols in it.  
        /// </summary>
        /// <param name="pdbFilePath">The name of the PDB file to open.</param>
        /// <returns>The SymbolReaderModule that represents the information in the symbol file (PDB)</returns>
        public ManagedSymbolModule OpenSymbolFile(string pdbFilePath)
        {
            if (!m_symbolModuleCache.TryGet(pdbFilePath, out ManagedSymbolModule ret))
            {
                Stream stream = File.Open(pdbFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] firstBytes = new byte[4];
                if (stream.Read(firstBytes, 0, firstBytes.Length) != 4)
                {
                    throw new InvalidOperationException("PDB corrupted (too small) " + pdbFilePath);
                }

                if (firstBytes[0] == 'B' && firstBytes[1] == 'S' && firstBytes[2] == 'J' && firstBytes[3] == 'B')
                {
                    stream.Seek(0, SeekOrigin.Begin);   // Start over
                    ret = new PortableSymbolModule(this, stream, pdbFilePath);
                }
                else
                {
                    stream.Dispose();
                    ret = new NativeSymbolModule(this, pdbFilePath);
                }
                m_symbolModuleCache.Add(pdbFilePath, ret);
            }
            return ret;
        }

        /// <summary>
        /// Like OpenSymbolFile, which opens a PDB, but this version will fail (return null)
        /// if it is not WindowsSymbolModule.  It is a shortcut for OpenSymbolFile as NativeSymbolModule
        /// </summary>
        public NativeSymbolModule OpenNativeSymbolFile(string pdbFileName)
        {
            return OpenSymbolFile(pdbFileName) as NativeSymbolModule;
        }

        // Various state that controls symbol and source file lookup.  
        /// <summary>
        /// The symbol path used to look up PDB symbol files.   Set when the reader is initialized.  
        /// </summary>
        public string SymbolPath
        {
            get { return m_symbolPath; }
            set
            {
                m_symbolPath = value;
                m_symbolModuleCache.Clear();
                m_pdbPathCache.Clear();
                m_log.WriteLine("Symbol Path Updated to {0}", m_symbolPath);
                m_log.WriteLine("Symbol Path update forces clearing Pdb lookup cache");
            }
        }
        /// <summary>
        /// The paths used to look up source files.  defaults to _NT_SOURCE_PATH.  
        /// </summary>
        public string SourcePath
        {
            get
            {
                if (m_SourcePath == null)
                {
                    m_SourcePath = Environment.GetEnvironmentVariable("_NT_SOURCE_PATH");
                    if (m_SourcePath == null)
                    {
                        m_SourcePath = "";
                    }
                }
                return m_SourcePath;
            }
            set
            {
                m_SourcePath = value;
                m_parsedSourcePath = null;
            }
        }
        /// <summary>
        /// Where symbols are downloaded if needed.   Derived from symbol path.  It is the first
        /// directory on the local machine in a SRV*DIR*LOC spec, and %TEMP%\SymbolCache otherwise.  
        /// </summary>
        public string SymbolCacheDirectory
        {
            get
            {
                if (m_SymbolCacheDirectory == null)
                {
                    m_SymbolCacheDirectory = new SymbolPath(SymbolPath).DefaultSymbolCache();
                }

                return m_SymbolCacheDirectory;
            }
        }
        /// <summary>
        /// Authorization header to be ued when making requests to source server (only for SourceLink)
        /// </summary>
        public string AuthorizationHeaderForSourceLink { get; set; }
        /// <summary>
        /// The place where source is downloaded from a source server.  
        /// </summary>
        public string SourceCacheDirectory
        {
            get
            {
                if (m_SourceCacheDirectory == null)
                {
                    m_SourceCacheDirectory = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "SrcCache");
                }

                return m_SourceCacheDirectory;
            }
            set
            {
                m_SourceCacheDirectory = value;
            }
        }
        /// <summary>
        /// Is this symbol reader limited to just the local machine cache or not?
        /// </summary>
        public SymbolReaderOptions Options
        {
            get => _Options;
            set
            {
                _Options = value;
                m_pdbPathCache.Clear();
                m_log.WriteLine("Setting SYmbolReaderOptions forces clearing Pdb lookup cache");
            }
        }
        private SymbolReaderOptions _Options;

        /// <summary>
        /// We call back on this when we find a PDB by probing in 'unsafe' locations (like next to the EXE or in the Built location)
        /// If this function returns true, we assume that it is OK to use the PDB.  
        /// </summary>
        public Func<string, bool> SecurityCheck { get; set; }

        /// <summary>
        /// If set OnSymbolFileFound will be called when a PDB file is found.  
        /// It is passed the complete local file path, the PDB Guid (may be Guid.Empty) and PDB age.
        /// </summary>
        public event Action<string, Guid, int> OnSymbolFileFound;

        /// <summary>
        /// A place to log additional messages 
        /// </summary>
        public TextWriter Log { get { return m_log; } }

        /// <summary>
        /// Given a full filename path to an NGEN image, insure that there is an NGEN image for it
        /// in the symbol cache.  If one already exists, this method simply returns that.   If not
        /// it is generated and placed in the symbol cache.  When generating the PDB this routine
        /// attempt to resolve line numbers, which DOES require looking up the PDB for the IL image. 
        /// Thus routine may do network accesses (to download IL PDBs).  
        /// 
        /// Note that FindSymbolFilePathForModule calls this, so normally you don't need to call 
        /// this method directly.  
        /// 
        /// By default it places the PDB in the SymbolCacheDirectory using normal symbol server 
        /// cache conventions (PDBNAME\Guid-AGE\Name).   You can override this by specifying
        /// the outputDirectory parameter.  
        /// 
        /// <returns>The full path name of the PDB generated for the NGEN image.</returns>
        /// </summary>
        public string GenerateNGenSymbolsForModule(string ngenImageFullPath, string outputDirectory = null)
        {
            if (outputDirectory == null)
            {
                outputDirectory = SymbolCacheDirectory;
            }

            if (!File.Exists(ngenImageFullPath))
            {
                m_log.WriteLine("Warning, NGEN image does not exist: {0}", ngenImageFullPath);
                return null;
            }

            string pdbFileName;
            Guid pdbGuid;
            int pdbAge;
            using (var peFile = new PEFile.PEFile(ngenImageFullPath))
            {
                if (!peFile.GetPdbSignature(out pdbFileName, out pdbGuid, out pdbAge, true))
                {
                    m_log.WriteLine("Could not get PDB signature for {0}", ngenImageFullPath);
                    return null;
                }
            }

            // Fast path, the file already exists.
            pdbFileName = Path.GetFileName(pdbFileName);
            string relDirPath = pdbFileName + "\\" + pdbGuid.ToString("N") + pdbAge.ToString();
            string pdbDir = Path.Combine(outputDirectory, relDirPath);
            var pdbPath = Path.Combine(pdbDir, pdbFileName);
            if (File.Exists(pdbPath))
            {
                return pdbPath;
            }

            // We only handle cases where we generate NGEN pdbs.  
            if (!pdbPath.EndsWith(".ni.pdb", StringComparison.OrdinalIgnoreCase))
            {
                m_log.WriteLine("Pdb does not have .ni.pdb suffix");
                return null;
            }

            string privateRuntimeVerString;
            var clrDir = GetClrDirectoryForNGenImage(ngenImageFullPath, m_log, out privateRuntimeVerString);
            if (clrDir == null)
            {
                m_log.WriteLine("Could not find CLR directory for NGEN image {0}, Trying .NET Core", ngenImageFullPath);
                return HandleNetCorePdbs(ngenImageFullPath, pdbPath);
            }

            // See if this is a V4.5 CLR, if so we can do line numbers too.l  
            var lineNumberArg = "";
            var ngenexe = Path.Combine(clrDir, "ngen.exe");
            m_log.WriteLine("Checking for V4.5 for NGEN image {0}", ngenexe);
            if (!File.Exists(ngenexe))
            {
                return null;
            }

            var isV4_5Runtime = false;

            Match m;
            using (var peFile = new PEFile.PEFile(ngenexe))
            {
                var fileVersionInfo = peFile.GetFileVersionInfo();
                if (fileVersionInfo != null)
                {
                    var clrFileVersion = fileVersionInfo.FileVersion;
                    m_log.WriteLine("Got NGEN image file version number: {0}", clrFileVersion);

                    m = Regex.Match(clrFileVersion, @"(\d+).(\d+)((\d|\.)*)");
                    if (m.Success)
                    {
                        var majorVersion = int.Parse(m.Groups[1].Value);
                        var minorVersion = int.Parse(m.Groups[2].Value);
                        var majorMinor = majorVersion * 10 + minorVersion;
                        if (majorMinor >= 46)
                        {
                            m_log.WriteLine("Is a V4.6 or beyond");
                            isV4_5Runtime = true;
                        }
                        else if (majorMinor == 40)
                        {
                            // 4.0.30319.16000 == V4.5 We need a build number >= 16000) to be a V4.5 runtime.  
                            m = Regex.Match(m.Groups[3].Value, @"(\d+)$");
                            if (m.Success && int.Parse(m.Groups[1].Value) >= 16000)
                            {
                                isV4_5Runtime = true;
                            }
                        }
                    }
                }
            }

            var options = new CommandOptions();
            options.AddEnvironmentVariable("_NT_SYMBOL_PATH", SymbolPath);
            options.AddOutputStream(m_log);
            options.AddNoThrow();

            options.AddEnvironmentVariable("COMPLUS_NGenEnableCreatePdb", "1");

            // NGenLocalWorker is needed for V4.0 runtimes but interferes on V4.5 runtimes.  
            if (!isV4_5Runtime)
            {
                options.AddEnvironmentVariable("COMPLUS_NGenLocalWorker", "1");
            }

            var newPath = "%PATH%;" + clrDir;
            options.AddEnvironmentVariable("PATH", newPath);

            // For Win8 Store Auto-NGEN images we need to use a location where the app can write the PDB file
            var outputPdbPath = pdbPath;
            var ngenOutputDirectory = outputDirectory;

            // Find the tempDir where we can write.  
            string tempDir = null;
            m = Regex.Match(ngenImageFullPath, @"(.*)\\Microsoft\\CLR_v(\d+)\.\d+(_(\d\d))?\\NativeImages", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                tempDir = Path.Combine(m.Groups[1].Value, @"Temp\NGenPdb");
                DirectoryUtilities.Clean(tempDir);
                Directory.CreateDirectory(tempDir);
                ngenOutputDirectory = tempDir;
                outputPdbPath = Path.Combine(tempDir, relDirPath, pdbFileName);
                m_log.WriteLine("Updating NGEN createPdb output file to {0}", outputPdbPath); // TODO FIX NOW REMOVE (for debugging)
            }

            // TODO: Hack.   V4.6.1 has both these characteristics, which leads to the issue
            //      1) NGEN CreatePDB requires the path to be in the NIC or it fails. 
            //      2) It uses links to some NGEN images, which means that the OS may give you path that is not in the NIC.  
            // Should be fixed by 12/2015
            if (isV4_5Runtime)
            {
                InsurePathIsInNIC(m_log, ref ngenImageFullPath);
            }

            try
            {
                for (; ; ) // Loop for retrying without /lines 
                {
                    if (!string.IsNullOrEmpty(privateRuntimeVerString))
                    {
                        m_log.WriteLine("Ngen will run for private runtime ", privateRuntimeVerString);
                        m_log.WriteLine("set COMPLUS_Version=" + privateRuntimeVerString);
                        options.AddEnvironmentVariable("COMPLUS_Version", privateRuntimeVerString);
                    }
                    // TODO FIX NOW: there is a and ugly problem with persistence of suboptimal PDB files
                    // This is made pretty bad because the not finding the IL PDBs is enough to make it fail.  

                    // TODO we need to figure out a convention show we know that we have fallen back to no-lines
                    // and we should regenerate it if we ultimately get the PDB information 
                    var cmdLine = string.Format(@"{0}\ngen.exe createpdb {1} {2} {3}",
                        clrDir, Command.Quote(ngenImageFullPath), Command.Quote(ngenOutputDirectory), lineNumberArg);
                    // TODO FIX NOW REMOVE after V4.5 is out a while
                    m_log.WriteLine("set COMPLUS_NGenEnableCreatePdb=1");
                    if (!isV4_5Runtime)
                    {
                        m_log.WriteLine("set COMPLUS_NGenLocalWorker=1");
                    }

                    m_log.WriteLine("set PATH=" + newPath);
                    m_log.WriteLine("set _NT_SYMBOL_PATH={0}", SymbolPath);
                    m_log.WriteLine("*** NGEN  CREATEPDB cmdline: {0}\r\n", cmdLine);
                    var cmd = Command.Run(cmdLine, options);
                    m_log.WriteLine("*** NGEN CREATEPDB returns: {0}", cmd.ExitCode);

                    if (cmd.ExitCode != 0)
                    {
                        // ngen might make a bad PDB, so if it returns failure delete it.  
                        if (File.Exists(outputPdbPath))
                        {
                            File.Delete(outputPdbPath);
                        }

                        // We may have failed because we could not get the PDB.  
                        if (lineNumberArg.Length != 0)
                        {
                            m_log.WriteLine("Ngen failed to generate pdb for {0}, trying again without /lines", ngenImageFullPath);
                            lineNumberArg = "";
                            continue;
                        }
                    }

                    if (cmd.ExitCode != 0 || !File.Exists(outputPdbPath))
                    {
                        m_log.WriteLine("ngen failed to generate pdb for {0} at expected location {1}", ngenImageFullPath, outputPdbPath);
                        return null;
                    }

                    // Copy the file to where we want the PDB to live (if we could not create there in the first place).    
                    if (outputPdbPath != pdbPath)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(pdbPath));        // Make sure the destination directory exists.
                        File.Copy(outputPdbPath, pdbPath);
                    }
                    return pdbPath;
                }
            }
            finally
            {
                // Insure we have cleaned up any temporary files.  
                if (tempDir != null)
                {
                    DirectoryUtilities.Clean(tempDir);
                }
            }
        }

        /// <summary>
        /// Given a NGEN (or ReadyToRun) imge 'ngenImageFullPath' and the PDB path
        /// that we WANT it to generate generate the PDB.  Returns either pdbPath 
        /// on success or null on failure.  
        /// 
        /// TODO can be removed when we properly publish the NGEN pdbs as part of build.  
        /// </summary>
        private string HandleNetCorePdbs(string ngenImageFullPath, string pdbPath)
        {
            // We only handle NGEN PDB. 
            if (!pdbPath.EndsWith(".ni.pdb", StringComparison.OrdinalIgnoreCase))
            {
                m_log.WriteLine("Not a crossGen PDB {0}", pdbPath);
                return null;
            }

            var ngenImageDir = Path.GetDirectoryName(ngenImageFullPath);
            var pdbDir = Path.GetDirectoryName(pdbPath);

            // We need Crossgen, and there are several options, see what we can do. 
            string crossGen = GetCrossGenExePath(ngenImageFullPath);
            if (crossGen == null)
            {
                m_log.WriteLine("Could not find Crossgen.exe to generate PDBs, giving up.");
                return null;
            }

            var winDir = Environment.GetEnvironmentVariable("winDir");
            if (winDir == null)
            {
                return null;
            }

            // Make sure the output dir exists.  
            Directory.CreateDirectory(pdbDir);

            // Are these readyToRun images
            string crossGenInputName = ngenImageFullPath;
            if (!crossGenInputName.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase))
            {
                // Note that the PDB does not pick the correct PDB signature unless the name
                // of the PDB matches the name of the DLL (with suffixes removed).  

                crossGenInputName = pdbPath.Substring(0, pdbPath.Length - 3) + "dll";
                File.Copy(ngenImageFullPath, crossGenInputName);
            }

            var cmdLine = Command.Quote(crossGen) +
                " /CreatePdb " + Command.Quote(pdbDir) +
                " /Platform_Assemblies_Paths " + Command.Quote(ngenImageDir) +
                " " + Command.Quote(crossGenInputName);

            var options = new CommandOptions();
            options.AddOutputStream(m_log);
            options.AddNoThrow();

            // Needs diasymreader.dll to be on the path.  
            var newPath = winDir + @"\Microsoft.NET\Framework\v4.0.30319" + ";" +
                winDir + @"\Microsoft.NET\Framework64\v4.0.30319" + ";%PATH%";
            options.AddEnvironmentVariable("PATH", newPath);
            options.AddCurrentDirectory(ngenImageDir);
            m_log.WriteLine("**** Running CrossGen");
            m_log.WriteLine("set PATH=" + newPath);
            m_log.WriteLine("{0}\r\n", cmdLine);
            var cmd = Command.Run(cmdLine, options);

            // Delete the temporary file if necessary
            if (crossGenInputName != ngenImageFullPath)
            {
                FileUtilities.ForceDelete(crossGenInputName);
            }

            if (cmd.ExitCode != 0 || !File.Exists(pdbPath))
            {
                m_log.WriteLine("CrossGen failed to generate {0} exit code {0}", pdbPath, cmd.ExitCode);
                return null;
            }

            return pdbPath;
        }

        private static string getNugetPackageDir()
        {
            string homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            if (homeDrive == null)
            {
                return null;
            }

            string homePath = Environment.GetEnvironmentVariable("HOMEPATH");
            if (homePath == null)
            {
                return null;
            }

            var nugetPackageDir = homeDrive + homePath + @"\.nuget\packages";
            if (!Directory.Exists(nugetPackageDir))
            {
                return null;
            }

            return nugetPackageDir;
        }

        private string GetCrossGenExePath(string ngenImageFullPath)
        {
            var imageDir = Path.GetDirectoryName(ngenImageFullPath);
            string crossGen = Path.Combine(imageDir, "crossGen.exe");

            m_log.WriteLine("Checking for CoreCLR case, looking for CrossGen at {0}", crossGen);
            if (File.Exists(crossGen))
            {
                return crossGen;
            }

            string coreclr = Path.Combine(imageDir, "coreclr.dll");
            if (File.Exists(coreclr))
            {
                DateTime coreClrTimeStamp = File.GetLastWriteTimeUtc(coreclr);
                m_log.WriteLine("Found coreclr: at  {0}, timestamp {1}", coreclr, coreClrTimeStamp);
                string nugetDir = getNugetPackageDir();
                if (nugetDir != null)
                {
                    m_log.WriteLine("Found nuget package dir: at  {0}", nugetDir);
                    foreach (var runtimeDir in Directory.GetDirectories(nugetDir, "runtime.win*.microsoft.netcore.runtime.coreclr"))
                    {
                        foreach (var runtimeVersionDir in Directory.GetDirectories(runtimeDir))
                        {
                            foreach (var osarchDir in Directory.GetDirectories(Path.Combine(runtimeVersionDir, "runtimes"), "win*"))
                            {
                                string packageCoreCLR = Path.Combine(osarchDir, @"native\coreclr.dll");
                                DateTime packageCoreClrTimeStamp = File.GetLastWriteTimeUtc(packageCoreCLR);
                                m_log.WriteLine("Checking timestamp of file {0} = {1}", packageCoreCLR, packageCoreClrTimeStamp);
                                if (File.Exists(packageCoreCLR) && packageCoreClrTimeStamp == coreClrTimeStamp)
                                {
                                    crossGen = Path.Combine(runtimeVersionDir, @"tools\crossgen.exe");
                                    m_log.WriteLine("Found matching CoreCLR, probing for crossgen at {0}", crossGen);
                                    if (File.Exists(crossGen))
                                    {
                                        return crossGen;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Check if you are running the runtime out of the nuget directory itself 
            var m = Regex.Match(imageDir, @"^(.*)\\runtimes\\win.*\\native$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                crossGen = Path.Combine(m.Groups[1].Value, "tools", "crossGen.exe");
                if (File.Exists(crossGen))
                {
                    return crossGen;
                }
            }

            m_log.WriteLine("Could not find crossgen, giving up");
            return null;
        }

        // TODO remove after 12/2015
        private void InsurePathIsInNIC(TextWriter log, ref string ngenImageFullPath)
        {
            // We only get called if we are 4.5. or beyond, so we should have AUX files if we are in the nic.  
            string auxFilePath = ngenImageFullPath + ".aux";
            if (File.Exists(auxFilePath))
            {
                log.WriteLine("Path has a AUX file in NIC: {0}", ngenImageFullPath);
                return;
            }
            string ngenFileName = Path.GetFileName(ngenImageFullPath);
            long ngenFileSize = (new FileInfo(ngenImageFullPath)).Length;
            log.WriteLine("Path is not in NIC, trying to put it in the NIC: Size {0} {1}", ngenFileSize, ngenImageFullPath);

            string windir = Environment.GetEnvironmentVariable("WinDir");
            if (windir == null)
            {
                return;
            }

            string candidate = null;
            string assemblyDir = Path.Combine(windir, "Assembly");
            foreach (string nicBase in Directory.GetDirectories(assemblyDir, "NativeImages_v4*"))
            {
                foreach (string file in Directory.EnumerateFiles(nicBase, ngenFileName, SearchOption.AllDirectories))
                {
                    long fileLen = (new FileInfo(file)).Length;
                    if (fileLen == ngenFileSize)
                    {
                        if (candidate != null)      // Ambiguity, give up.  
                        {
                            log.WriteLine("There is more than one file in the NIC with matching name and size! giving up.");
                            return;
                        }
                        candidate = file;
                    }
                }
            }

            if (candidate != null)
            {
                ngenImageFullPath = candidate;
                log.WriteLine("Updating path to be in NIC: {0}", ngenImageFullPath);
            }
        }

        /// <summary>
        ///  Called when you are done with the symbol reader.
        ///  Closes all opened symbol files.
        /// </summary>
        public void Dispose()
        {
            m_symbolModuleCache.Clear();
        }

        #region private
        /// <summary>
        /// Returns true if 'filePath' exists and is a PDB that has pdbGuid and pdbAge.  
        /// if pdbGuid == Guid.Empty, then the pdbGuid and pdbAge checks are skipped. 
        /// </summary>
        private bool PdbMatches(string filePath, Guid pdbGuid, int pdbAge, bool checkSecurity = true)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    if (checkSecurity && !CheckSecurity(filePath))
                    {
                        m_log.WriteLine("FindSymbolFilePath: Aborting, security check failed on {0}", filePath);
                        return false;
                    }

                    if (pdbGuid == Guid.Empty)
                    {
                        m_log.WriteLine("FindSymbolFilePath: No PDB Guid = Guid.Empty provided, assuming an unsafe PDB match for {0}", filePath);
                        return true;
                    }
                    ManagedSymbolModule module = OpenSymbolFile(filePath);
                    if ((module.PdbGuid == pdbGuid) && (module.PdbAge == pdbAge))
                    {
                        return true;
                    }
                    else
                    {
                        m_log.WriteLine("FindSymbolFilePath: ************ FOUND PDB File {0} has Guid {1} age {2} != Desired Guid {3} age {4}",
                            filePath, module.PdbGuid, module.PdbAge, pdbGuid, pdbAge);
                    }
                }
                else
                {
                    m_log.WriteLine("FindSymbolFilePath: Probed file location {0} does not exist", filePath);
                }
            }
            catch (Exception e)
            {
                m_log.WriteLine("FindSymbolFilePath: Aborting pdbMatch of {0} Exception thrown: {1}", filePath, e.Message);
            }
            return false;
        }

        /// <summary>
        /// Fetches a file from the server 'serverPath' with pdb signature path 'pdbSigPath' (concatinate them with a / or \ separator
        /// to form a complete URL or path name).   It will place the file in 'fullDestPath'   It will return true if successful
        /// If 'contentTypeFilter is present, this predicate is called with the URL content type (e.g. application/octet-stream)
        /// and if it returns false, it fails.   This insures that things that are the wrong content type (e.g. redirects to 
        /// some sort of login) fail cleanly.  
        /// 
        /// You should probably be using GetFileFromServer
        /// </summary>
        /// <param name="serverPath">path to server (e.g. \\symbols\symbols or http://symweb) </param>
        /// <param name="pdbIndexPath">pdb path with signature (e.g clr.pdb/1E18F3E494DC464B943EA90F23E256432/clr.pdb)</param>
        /// <param name="fullDestPath">the full path of where to put the file locally </param>
        /// <param name="contentTypeFilter">if present this allows you to filter out urls that dont match this ContentType.</param>
        internal bool GetPhysicalFileFromServer(string serverPath, string pdbIndexPath, string fullDestPath, Predicate<string> contentTypeFilter = null)
        {
            if (File.Exists(fullDestPath))
            {
                return true;
            }

            var sw = Stopwatch.StartNew();

            if (m_deadServers != null)
            {
                // Try again after 5 minutes.  
                if ((DateTime.UtcNow - m_lastDeadTimeUtc).TotalSeconds > 300)
                {
                    m_deadServers = null;
                }
            }

            if (m_deadServers != null && m_deadServers.Contains(serverPath))
            {
                m_log.WriteLine("FindSymbolFilePath: Skipping server {0} because it was unreachable in the past, will try again in 5 min.", serverPath);
                return false;
            }

            bool canceled = false;        // Are we trying to cancel the task
            bool alive = false;           // Has the task ever been shown to be alive (worth giving them time)
            bool successful = false;      // The task was successful
            try
            {
                // This implements a timeout 
                Task task = Task.Factory.StartNew(delegate
                {
                    if (Uri.IsWellFormedUriString(serverPath, UriKind.Absolute))
                    {
                        var fullUri = BuildFullUri(serverPath, pdbIndexPath);
                        try
                        {
                            m_log.WriteLine("FindSymbolFilePath: In task, sending HTTP request {0}", fullUri);

                            var req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(fullUri);
#if !NETSTANDARD1_6
                            // Some symbol servers want a user agent and simply fail if they don't have one (see https://github.com/Microsoft/perfview/issues/571)
                            // So set it (this is what the symsrv code on Windows sets).   On NetStandard1.6 we give up since we dont' have this API available.  
                            req.UserAgent = "Microsoft-Symbol-Server/6.13.0009.1140";
#endif
                            var responseTask = req.GetResponseAsync();
                            responseTask.Wait();
                            var response = responseTask.Result;

                            alive = true;
                            if (!canceled)
                            {
                                if (contentTypeFilter != null && !contentTypeFilter(response.ContentType))
                                {
                                    throw new InvalidOperationException("Bad File Content type " + response.ContentType + " for " + fullDestPath);
                                }

                                using (var fromStream = response.GetResponseStream())
                                {
                                    if (CopyStreamToFile(fromStream, fullUri, fullDestPath, ref canceled) == 0)
                                    {
                                        File.Delete(fullDestPath);
                                        throw new InvalidOperationException("Illegal Zero sized file " + fullDestPath);
                                    }
                                }

                                successful = true;
                            }
                        }
                        catch (Exception e)
                        {
                            if (!canceled)
                            {
                                var asWeb = e as WebException;
                                var sentMessage = false;
                                if (asWeb != null)
                                {
                                    var asHttpResonse = asWeb.Response as HttpWebResponse;
                                    if (asHttpResonse != null && asHttpResonse.StatusCode == HttpStatusCode.NotFound)
                                    {
                                        sentMessage = true;
                                        m_log.WriteLine("FindSymbolFilePath: Probe of {0} was not found.", fullUri);
                                    }
                                }
                                if (!sentMessage)
                                {
                                    m_log.WriteLine("FindSymbolFilePath: Probe of {0} failed: {1}", fullUri, e.Message);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Use CopyStreamToFile instead of File.Copy because it is interruptible and displays status.  
                        var fullSrcPath = Path.Combine(serverPath, pdbIndexPath);
                        if (File.Exists(fullSrcPath))
                        {
                            alive = true;
                            if (!canceled)
                            {
                                using (var fromStream = File.OpenRead(fullSrcPath))
                                {
                                    if (CopyStreamToFile(fromStream, fullSrcPath, fullDestPath, ref canceled) == 0)
                                    {
                                        File.Delete(fullDestPath);
                                        throw new InvalidOperationException("Illegal Zero sized file " + fullDestPath);
                                    }
                                }

                                successful = true;
                            }
                        }
                        else
                        {
                            alive = true;
                            if (!canceled)
                            {
                                m_log.WriteLine("FindSymbolFilePath: Probe of {0}, file not present", fullSrcPath);
                            }
                        }
                    }
                });

                // Wait 10 seconds allowing for interruptions.  
                var limit = 100;
                if (serverPath.StartsWith(@"\\symbols", StringComparison.OrdinalIgnoreCase))     // This server is pretty slow.  
                {
                    limit = 250;
                }

                for (int i = 0; i < limit; i++)
                {
                    if (i == 10)
                    {
                        m_log.WriteLine("\r\nFindSymbolFilePath: Waiting for initial connection to {0}/{1}.", serverPath, pdbIndexPath);
                    }

                    if (task.Wait(100))
                    {
                        break;
                    }

                    Thread.Sleep(0);
                }

                if (alive)
                {
                    if (!task.Wait(100))
                    {
                        m_log.WriteLine("\r\nFindSymbolFilePath: Copy in progress on {0}/{1}, waiting for completion.", serverPath, pdbIndexPath);
                    }

                    // Let it complete, however we do sleep so we can be interrupted.  
                    while (!task.Wait(100))
                    {
                        Thread.Sleep(0);        // TO allow interruption
                    }
                }
                // If we did not complete, set the dead server information.  
                else if (!task.IsCompleted)
                {
                    canceled = true;
                    m_log.WriteLine("FindSymbolFilePath: Time {0} sec.  Timeout of {1} seconds exceeded for {2}.  Setting as dead server",
                            sw.Elapsed.TotalSeconds, limit / 10, serverPath);
                    if (m_deadServers == null)
                    {
                        m_deadServers = new List<string>();
                    }

                    m_deadServers.Add(serverPath);
                    m_lastDeadTimeUtc = DateTime.UtcNow;
                }
            }
            finally
            {
                canceled = true;
            }

            return successful && File.Exists(fullDestPath);
        }

        /// <summary>
        /// Build the full uri from server path and pdb index path
        /// </summary>
        private string BuildFullUri(string serverPath, string pdbIndexPath)
        {
            var tail = pdbIndexPath.Replace('\\', '/');
            if (!tail.StartsWith("/", StringComparison.Ordinal))
            {
                tail = "/" + tail;
            }

            // The server path can contain query parameters (eg, Azure storage SAS token).
            // Append the pdb index to the path part.
            var query = serverPath.IndexOf('?');
            if (query > 0)
            {
                return serverPath.Insert(query, tail);
            }
            else
            {
                return serverPath + tail;
            }
        }

        /// <summary>
        /// This just copies a stream to a file path with logging.  
        /// </summary>
        private int CopyStreamToFile(Stream fromStream, string fromUri, string fullDestPath, ref bool canceled)
        {
            bool completed = false;
            int byteCount = 0;
            var copyToFileName = fullDestPath + ".new";
            try
            {
                var dirName = Path.GetDirectoryName(fullDestPath);
                Directory.CreateDirectory(dirName);
                m_log.WriteLine("CopyStreamToFile: Copying {0} to {1}", fromUri, copyToFileName);
                var sw = Stopwatch.StartNew();
                int lastMeg = 0;
                int last10K = 0;
                using (Stream toStream = File.Create(copyToFileName))
                {
                    byte[] buffer = new byte[8192];
                    for (; ; )
                    {
                        int count = fromStream.Read(buffer, 0, buffer.Length);
                        if (count == 0)
                        {
                            break;
                        }

                        toStream.Write(buffer, 0, count);
                        byteCount += count;

                        if (byteCount - last10K >= 10000)
                        {
                            m_log.Write(".");
                            last10K += 10000;
                        }
                        if (byteCount - lastMeg >= 1000000)
                        {
                            m_log.WriteLine(" {0:f1} Meg", byteCount / 1000000.0);
                            m_log.Flush();
                            lastMeg += 1000000;
                        }
                        if (sw.Elapsed.TotalMilliseconds > 100)
                        {
                            m_log.Flush();
                            System.Threading.Thread.Sleep(0);       // allow interruption.
                            sw.Restart();
                        }

                        if (canceled)
                        {
                            break;
                        }
                    }
                }
                if (!canceled)
                {
                    completed = true;
                }
            }
            finally
            {
                m_log.WriteLine();
                if (completed)
                {
                    FileUtilities.ForceMove(copyToFileName, fullDestPath);
                    m_log.WriteLine("CopyStreamToFile: Copy Done, moving to {0}", fullDestPath);
                }
                else
                {
                    m_log.WriteLine("CopyStreamToFile: Copy not complete, deleting temp copy to file");
                    FileUtilities.ForceDelete(copyToFileName);
                }
            }
            return byteCount;
        }

        /// <summary>
        /// Looks up 'fileIndexPath' on the server 'urlForServer' (concatenate to form complete URL) copying the file to 
        /// 'targetPath' and returning targetPath name there (thus it is always a local file).  Unlike  GetPhysicalFileFromServer, 
        /// GetFileFromServer understands how to deal with compressed files and file.ptr (redirection).  
        /// </summary>
        /// <returns>targetPath or null if the file cannot be found.</returns>
        private string GetFileFromServer(string urlForServer, string fileIndexPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                m_log.WriteLine("FindSymbolFilePath: Found in cache {0}", targetPath);
                return targetPath;
            }

            // Fail quickly if instructed to  
            if ((Options & SymbolReaderOptions.CacheOnly) != 0)
            {
                m_log.WriteLine("FindSymbolFilePath: no file at cache location {0} and cacheOnly set, giving up.", targetPath);
                return null;
            }

            // We just had a symbol cache with no target.   
            if (urlForServer == null)
            {
                return null;
            }

            // Allows us to reject files that are not binary (sometimes you get redirected to a 
            // login script and we don't want to blindly accept that).  
            Predicate<string> onlyBinaryContent = delegate (string contentType)
            {
                bool ret = contentType.EndsWith("octet-stream");
                if (!ret)
                {
                    m_log.WriteLine("FindSymbolFilePath: expecting 'octet-stream' (Binary) data, got '{0}' (are you redirected to a login page?)", contentType);
                }

                return ret;
            };

            // Just try to fetch the file directly
            m_log.WriteLine("FindSymbolFilePath: Searching Symbol Server {0}.", urlForServer);
            if (GetPhysicalFileFromServer(urlForServer, fileIndexPath, targetPath, onlyBinaryContent))
            {
                return targetPath;
            }

            // The rest of this compressed file/file pointers stuff is only for remote servers.  
            if (!urlForServer.StartsWith(@"\\") && !Uri.IsWellFormedUriString(urlForServer, UriKind.Absolute))
            {
                return null;
            }

            // See if it is a compressed file by replacing the last character of the name with an _
            var compressedSigPath = fileIndexPath.Substring(0, fileIndexPath.Length - 1) + "_";
            var compressedFilePath = targetPath.Substring(0, targetPath.Length - 1) + "_";
            if (GetPhysicalFileFromServer(urlForServer, compressedSigPath, compressedFilePath, onlyBinaryContent))
            {
                // Decompress it
                m_log.WriteLine("FindSymbolFilePath: Expanding {0} to {1}", compressedFilePath, targetPath);
                var commandline = "Expand " + Command.Quote(compressedFilePath) + " " + Command.Quote(targetPath);
                var options = new CommandOptions().AddNoThrow();
                var command = Command.Run(commandline, options);
                if (command.ExitCode != 0)
                {
                    m_log.WriteLine("FindSymbolFilePath: Failure executing: {0}", commandline);
                    return null;
                }
                File.Delete(compressedFilePath);
                return targetPath;
            }

            // See if we have a file that tells us to redirect elsewhere. 
            var filePtrSigPath = Path.Combine(Path.GetDirectoryName(fileIndexPath), "file.ptr");
            var filePtrFilePath = Path.Combine(Path.GetDirectoryName(targetPath), "file.ptr");
            FileUtilities.ForceDelete(filePtrFilePath);
            var goodSeparator = urlForServer.StartsWith(@"\\") ? @"\\" : "/";
            if (GetPhysicalFileFromServer(urlForServer, filePtrSigPath, filePtrFilePath))
            {
                var filePtrData = File.ReadAllText(filePtrFilePath).Trim();
                FileUtilities.ForceDelete(filePtrFilePath);
                if (filePtrData.StartsWith("MSG"))
                {
                    m_log.WriteLine("FindSymbolFilePath: Probe of {0}{1}{2} fails with message '{3}'", urlForServer, goodSeparator, filePtrSigPath, filePtrData);
                    m_log.WriteLine("FindSymbolFilePath: target File.ptr file {0} ", filePtrFilePath);
                    return null;
                }
                if (filePtrData.StartsWith("PATH:"))
                {
                    filePtrData = filePtrData.Substring(5);
                }
                else
                {
                    m_log.WriteLine("FindSymbolFilePath: file.ptr data: {0}", filePtrData);
                }

                if (filePtrData.EndsWith(fileIndexPath, StringComparison.OrdinalIgnoreCase))
                {
                    var redirectedServer = filePtrData.Substring(0, filePtrData.Length - fileIndexPath.Length - 1);
                    var goodSeparatorForRedirectedServer = redirectedServer.StartsWith(@"\\") ? @"\\" : "/";

                    m_log.WriteLine("FindSymbolFilePath: Probe of {0}{1}{2} redirecting to {3}{4}{5}",
                        urlForServer, goodSeparator, filePtrSigPath,
                        redirectedServer, goodSeparatorForRedirectedServer, fileIndexPath);
                    return GetFileFromServer(redirectedServer, fileIndexPath, targetPath);
                }
                else
                {
                    if (filePtrData.StartsWith(@"\\"))
                    {
                        var bangIdx = filePtrData.IndexOf('\\', 2);
                        if (0 <= bangIdx)
                        {
                            m_log.WriteLine("FindSymbolFilePath: Looking up UNC path {0}", filePtrData);
                            var server = filePtrData.Substring(0, bangIdx);
                            var path = filePtrData.Substring(bangIdx + 1);
                            return GetFileFromServer(server, path, targetPath);
                        }
                    }
                    else if (filePtrData.StartsWith(@"..\") || filePtrData.StartsWith(@".\"))
                    {
                        m_log.WriteLine("FindSymbolFilePath: Probe of {0}{1}{2} redirecting to {3}\\{4}",
                            urlForServer, goodSeparator, filePtrSigPath,
                            urlForServer, filePtrData);
                        return GetFileFromServer(urlForServer, filePtrData, targetPath);
                    }
                    m_log.WriteLine("FindSymbolFilePath: Error, Don't know how to look up redirected location {0}", filePtrData);
                }
            }

            // See if we already have a prefix XX/ prefix.  
            if (2 < fileIndexPath.Length && fileIndexPath[2] != '/')
            {
                m_log.WriteLine("Trying the XX/XXYYY.PDB convention");
                // See if it is following the XX/XXYYYY.PDB convention (used to make directories smaller).  
                var prefixedPath = fileIndexPath.Substring(0, 2) + "/" + fileIndexPath;
                return GetFileFromServer(urlForServer, prefixedPath, targetPath);
            }

            return null;
        }

        /// <summary>
        /// Deduce the path to where CLR.dll (and in particular NGEN.exe live for the NGEN image 'ngenImagepath')
        /// Returns null if it can't be found.  If the NGEN image is associated with a private runtime return 
        /// that value in 'privateVerStr'
        /// </summary>
        private static string GetClrDirectoryForNGenImage(string ngenImagePath, TextWriter log, out string privateRuntimeVerStr)
        {
            string majorVersion;            // a small integer (e.g. 4)
            privateRuntimeVerStr = null;
            // Set the default bitness
            string bitness;            // Either "64" or ""
            var m = Regex.Match(ngenImagePath, @"^(.*)\\assembly\\NativeImages_(v(\d+)[\dA-Za-z.]*)_(\d\d)\\", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var basePath = m.Groups[1].Value;
                var version = m.Groups[2].Value;
                majorVersion = m.Groups[3].Value;
                bitness = m.Groups[4].Value;

                // See if this NGEN image was in a NIC associated with a private runtime.  
                if (basePath.EndsWith(version))
                {
                    if (Directory.Exists(basePath))
                    {
                        privateRuntimeVerStr = version;
                        return basePath;
                    }
                }
            }
            else
            {
                // Per-user NGEN Image Caches.   
                m = Regex.Match(ngenImagePath, @"\\Microsoft\\CLR_v(\d+)\.\d+(_(\d\d))?\\NativeImages", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    majorVersion = m.Groups[1].Value;
                    bitness = m.Groups[3].Value;
                }
                else
                {
                    // Pre-generated native images.  
                    m = Regex.Match(ngenImagePath, @"\\Microsoft.NET\\Framework((\d\d)?)\\v(\d+).*\\NativeImages", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        majorVersion = m.Groups[3].Value;
                        bitness = m.Groups[1].Value;
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            // Only version 4.0 of the runtime has NGEN PDB support 
            if (int.Parse(majorVersion) < 4)
            {
                log.WriteLine("Pre V4.0 native image, skipping: {0}", ngenImagePath);
                return null;
            }

            var winDir = Environment.GetEnvironmentVariable("winDir");

            if (bitness != "64")
            {
                bitness = "";
            }

            Debug.Assert(bitness == "64" || bitness == "");

            var frameworkDir = Path.Combine(winDir, @"Microsoft.NET\Framework" + bitness);
            var candidates = Directory.GetDirectories(frameworkDir, "v" + majorVersion + ".*");
            if (candidates.Length != 1)
            {
                log.WriteLine("Warning: Could not find Version {0} of the .NET Framework in {1}", majorVersion, frameworkDir);
                return null;
            }
            return candidates[0];
        }

        private string m_SourcePath;
        internal List<string> ParsedSourcePath
        {
            get
            {
                if (m_parsedSourcePath == null)
                {
                    m_parsedSourcePath = new List<string>();
                    foreach (var path in SourcePath.Split(';'))
                    {
                        var normalizedPath = path.Trim();
                        if (normalizedPath.EndsWith(@"\"))
                        {
                            normalizedPath = normalizedPath.Substring(0, normalizedPath.Length - 1);
                        }

                        if (Directory.Exists(normalizedPath))
                        {
                            m_parsedSourcePath.Add(normalizedPath);
                        }
                        else
                        {
                            m_log.WriteLine("Path {0} in source path does not exist, skipping.", normalizedPath);
                        }
                    }
                }
                return m_parsedSourcePath;
            }
        }
        internal List<string> m_parsedSourcePath;

        private bool CheckSecurity(string pdbName)
        {
            if (SecurityCheck == null)
            {
                m_log.WriteLine("Found PDB {0}, however this is in an unsafe location.", pdbName);
                m_log.WriteLine("If you trust this location, place this directory the symbol path to correct this (or use the SecurityCheck method to override)");
                return false;
            }
            if (!SecurityCheck(pdbName))
            {
                m_log.WriteLine("Found PDB {0}, but failed security check.", pdbName);
                return false;
            }
            return true;
        }

        /// <summary>
        /// We may be a 32 bit app which has File system redirection turned on
        /// Morph System32 to SysNative in that case to bypass file system redirection         
        /// </summary>
        internal static string BypassSystem32FileRedirection(string path)
        {
            if (0 <= path.IndexOf("System32\\", StringComparison.OrdinalIgnoreCase))
            {
                var winDir = Environment.GetEnvironmentVariable("WinDir");
                if (winDir != null)
                {
                    var system32 = Path.Combine(winDir, "System32");
                    if (path.StartsWith(system32, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") != null)
                        {
                            var sysNative = Path.Combine(winDir, "Sysnative");
                            var newPath = Path.Combine(sysNative, path.Substring(system32.Length + 1));
                            if (File.Exists(newPath))
                            {
                                path = newPath;
                            }
                        }
                    }
                }
            }
            return path;
        }

        // Used as the key to the m_pdbPathCache.  
        private struct PdbSignature : IEquatable<PdbSignature>
        {
            public override int GetHashCode() { return Name.GetHashCode() + ID.GetHashCode(); }
            public bool Equals(PdbSignature other) { return ID == other.ID && Name == other.Name && Age == other.Age; }
            public string Name;
            public Guid ID;
            public int Age;
        }

        internal TextWriter m_log;
        private List<string> m_deadServers;     // What servers can't be reached right now
        private DateTime m_lastDeadTimeUtc;     // The last time something went dead.  
        private string m_SymbolCacheDirectory;
        private string m_SourceCacheDirectory;
        private Cache<string, ManagedSymbolModule> m_symbolModuleCache;
        private Cache<PdbSignature, string> m_pdbPathCache;
        private string m_symbolPath;

        #endregion
    }

    /// <summary>
    /// A SymbolModule represents a file that contains symbolic information 
    /// (a Windows PDB or Portable PDB).  This is the interface that is independent 
    /// of what kind of symbolic file format you use.  Becase portable PDBs only
    /// support managed code, this shared interface is by necessity the interface
    /// for managed code only (currently only Windows PDBs support native code).  
    /// </summary>
    public abstract class ManagedSymbolModule
    {
        /// <summary>
        /// This is the EXE associated with the Pdb.  It may be null or an invalid path.  It is used
        /// to help look up source code (it is implicitly part of the Source Path search) 
        /// </summary>
        public string ExePath { get; set; }

        /// <summary>
        /// The path name to the PDB itself.  Might be empty if the symbol information is in memory.  
        /// </summary>
        public string SymbolFilePath { get { return _pdbPath; } }

        /// <summary>
        /// The Guid that is used to uniquely identify the DLL-PDB pair (used for symbol servers)
        /// </summary>
        public virtual Guid PdbGuid { get { return Guid.Empty; } }

        public virtual int PdbAge { get { return 1; } }

        /// <summary>
        ///  Fetches the SymbolReader associated with this SymbolModule.  This is where shared
        ///  attributes (like SourcePath, SymbolPath etc) are found.  
        /// </summary>
        public SymbolReader SymbolReader { get { return _reader; } }

        /// <summary>
        /// Given a method and an IL offset, return a source location (line number and file).   
        /// Returns null if it could not find it.  
        /// </summary>
        public abstract SourceLocation SourceLocationForManagedCode(uint methodMetadataToken, int ilOffset);

        /// <summary>
        /// If the symbol file format supports SourceLink JSON this routine should be overriden
        /// to return it.  
        /// </summary>
        protected virtual IEnumerable<string> GetSourceLinkJson() { return Enumerable.Empty<string>(); }

        #region private 

        protected ManagedSymbolModule(SymbolReader reader, string path) { _pdbPath = path; _reader = reader; }

        internal TextWriter _log { get { return _reader.m_log; } }

        /// <summary>
        /// Return a URL for 'buildTimeFilePath' using the source link mapping (that 'GetSourceLinkJson' fetched)
        /// Returns null if there is URL using the SourceLink 
        /// </summary>
        /// <param name="buildTimeFilePath">The path to the source file at build time</param>
        /// <param name="url">The source link URL</param>
        /// <param name="relativeFilePath"></param>
        /// <returns>true if a source link file could be found</returns>
        internal bool GetUrlForFilePathUsingSourceLink(string buildTimeFilePath, out string url, out string relativeFilePath)
        {
            if (!_sourceLinkMappingInited)
            {
                _sourceLinkMappingInited = true;
                IEnumerable<string> sourceLinkJson = GetSourceLinkJson();
                if (sourceLinkJson.Any())
                {
                    _sourceLinkMapping = ParseSourceLinkJson(sourceLinkJson);
                }
            }

            if (_sourceLinkMapping != null)
            {
                foreach (Tuple<string, string> map in _sourceLinkMapping)
                {
                    string path = map.Item1;
                    string urlReplacement = map.Item2;

                    if (buildTimeFilePath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    {
                        relativeFilePath = buildTimeFilePath.Substring(path.Length, buildTimeFilePath.Length - path.Length).Replace('\\', '/');
                        url = urlReplacement.Replace("*", relativeFilePath);
                        return true;
                    }
                }
            }

            url = null;
            relativeFilePath = null;
            return false;
        }

        /// <summary>
        /// Parses SourceLink information and returns a list of filepath -> url Prefix tuples.  
        /// </summary>  
        private List<Tuple<string, string>> ParseSourceLinkJson(IEnumerable<string> sourceLinkContents)
        {
            List<Tuple<string, string>> ret = null;
            foreach (string sourceLinkJson in sourceLinkContents)
            {
                // TODO this is not right for corner cases (e.g. file paths with " or , } in them)
                Match m = Regex.Match(sourceLinkJson, @"documents.?\s*:\s*{(.*?)}", RegexOptions.Singleline);
                if (m.Success)
                {
                    string mappings = m.Groups[1].Value;
                    while (!string.IsNullOrWhiteSpace(mappings))
                    {
                        m = Regex.Match(m.Groups[1].Value, "^\\s*\"(.*?)\"\\s*:\\s*\"(.*?)\"\\s*,?(.*)", RegexOptions.Singleline);
                        if (m.Success)
                        {
                            if (ret == null)
                            {
                                ret = new List<Tuple<string, string>>();
                            }

                            string pathSpec = m.Groups[1].Value.Replace("\\\\", "\\");
                            if (pathSpec.EndsWith("*"))
                            {
                                pathSpec = pathSpec.Substring(0, pathSpec.Length - 1);      // Remove the *
                                ret.Add(new Tuple<string, string>(pathSpec, m.Groups[2].Value));
                            }
                            else
                            {
                                _log.WriteLine("Warning: {0} does not end in *, skipping this mapping.", pathSpec);
                            }

                            mappings = m.Groups[3].Value;
                        }
                        else
                        {
                            _log.WriteLine("Error: Could not parse SourceLink Mapping: {0}", mappings);
                            break;
                        }
                    }
                }
                else
                {
                    _log.WriteLine("Error: Could not parse SourceLink Json: {0}", sourceLinkJson);
                }
            }

            return ret;
        }

        private string _pdbPath;
        private SymbolReader _reader;
        private List<Tuple<string, string>> _sourceLinkMapping;      // Used by SourceLink to map build paths to URLs (see GetUrlForFilePath)
        private bool _sourceLinkMappingInited;                       // Lazy init flag. 
        #endregion
    }

    /// <summary>
    /// A SourceLocation represents a point in the source code.  That is the file and the line number.  
    /// </summary>
    public class SourceLocation
    {
        /// <summary>
        /// The source file for the code
        /// </summary>
        public SourceFile SourceFile { get; private set; }
        /// <summary>
        /// The line number for the code.
        /// </summary>
        public int LineNumber { get; private set; }

        #region private
        internal SourceLocation(SourceFile sourceFile, int lineNumber)
        {
            // The library seems to see FEEFEE for the 'unknown' line number.  0 seems more intuitive
            if (0xFEEFEE <= lineNumber)
            {
                lineNumber = 0;
            }

            SourceFile = sourceFile;
            LineNumber = lineNumber;
        }
        #endregion
    }

    /// <summary>
    /// SymbolReaderFlags indicates preferences on how aggressively symbols should be looked up.  
    /// </summary>
    [Flags]
    public enum SymbolReaderOptions
    {
        /// <summary>
        /// No options this is the common case, where you want to look up everything you can. 
        /// </summary>
        None = 0,
        /// <summary>
        /// Only fetch the PDB if it lives in the symbolCacheDirectory (is local an is generated).  
        /// This will generate NGEN pdbs unless the NoNGenPDBs flag is set. 
        /// </summary>
        CacheOnly = 1,
        /// <summary>
        /// No NGEN PDB generation.  
        /// </summary>
        NoNGenSymbolCreation = 2,
    }

    public abstract class SourceFile
    {
        /// <summary>
        /// The path of the file at the time the source file was built.   We also look here when looking for the source.  
        /// </summary>
        public string BuildTimeFilePath { get; protected set; }

        /// <summary>
        /// If the source file is directly available on the web (that is there is a Url that 
        /// can be used to fetch it with HTTP Get), then return that Url.   If no such publishing 
        /// point exists this property will return null.   
        /// </summary>
        public virtual string Url 
        { 
            get 
            {
                this.GetSourceLinkInfo(out string url, out _);
                return url;
            } 
        }

        /// <summary>
        /// This may fetch things from the source server, and thus can be very slow, which is why it is not a property. 
        /// returns a path to the file on the local machine (often in some machine local cache). 
        /// If requireChecksumMatch == false then you can see if you have an exact match by calling ChecksumMatches
        /// (and if there is a checksum with HasChecksum). 
        /// </summary>
        public virtual string GetSourceFile(bool requireChecksumMatch = false)
        {
            if (!_getSourceCalled)
            {
                _getSourceCalled = true;
                if (BuildTimeFilePath == null)
                {
                    _log.WriteLine("No BuildTimeFilePath, giving up looking for source file");
                    return null;
                }

                // Check the build location
                if (ProbeForBestMatch(BuildTimeFilePath))
                {
                    return _filePath;
                }

                // Look on the source server next.   
                _log.WriteLine("Looking up {0} in the source server (or URL)", BuildTimeFilePath);
                string srcServerLocation = GetSourceFromSrcServer();
                if (srcServerLocation != null)
                {
                    if (ProbeForBestMatch(srcServerLocation))
                    {
                        return _filePath;
                    }
                    else
                    {
                        _log.WriteLine("Warning. Source file from source server {0} did not match checksum", srcServerLocation);
                    }
                }

                // Try _NT_SOURCE_PATH
                var locations = _symbolModule.SymbolReader.ParsedSourcePath;
                _log.WriteLine("Not present on source server, looking on NT_SOURCE_PATH.");
                _log.WriteLine("_NT_SOURCE_PATH={0}", _symbolModule.SymbolReader.SourcePath);

                // If we know the exe path, add that to the search path.   
                if (_symbolModule.ExePath != null)
                {
                    var exeDir = Path.GetDirectoryName(_symbolModule.ExePath);
                    if (Directory.Exists(exeDir))
                    {
                        locations.Insert(0, exeDir);
                        _log.WriteLine("Adding Exe directory to source search path {0}", exeDir);
                    }
                }

                var curIdx = 0;
                char[] seps = new char[] { '\\', '/' };
                for (; ; )
                {
                    var sepIdx = BuildTimeFilePath.IndexOfAny(seps, curIdx);
                    if (sepIdx < 0)
                    {
                        break;
                    }

                    curIdx = sepIdx + 1;
                    var tail = BuildTimeFilePath.Substring(sepIdx);

                    _log.WriteLine("Probing Path Tail {0}", tail);

                    foreach (string location in locations)
                    {
                        var probe = location + tail;
                        if (ProbeForBestMatch(probe))
                        {
                            return _filePath;
                        }
                    }
                }
            }
            if (requireChecksumMatch && !_checksumMatches)
            {
                return null;
            }

            return _filePath;
        }

        /// <summary>
        /// true if the PDB has a checksum for the data in the source file. 
        /// </summary>
        public bool HasChecksum { get { return _hashAlgorithm != null; } }

        /// <summary>
        /// Gets the name of the algorithm used to compute the source file hash. Values should be from System.Security.Cryptography.HashAlgorithmName.
        /// This is null if there is no checksum.
        /// </summary>
        public string ChecksumAlgorithm
        {
            get
            {
                if (_hashAlgorithm == null)
                {
                    return null;
                }
                else if (_hashAlgorithm is SHA256)
                {
                    return "SHA256";
                }
                else if (_hashAlgorithm is SHA1)
                {
                    return "SHA1";
                }
                else if (_hashAlgorithm is MD5)
                {
                    return "MD5";
                }
                else
                {
                    Debug.Fail("Missing case in get_ChecksumAlgorithm");
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the bytes of the source files checksum. This is null if there is no checksum.
        /// </summary>
        public IReadOnlyCollection<byte> ChecksumValue => _hash;

        /// <summary>
        /// If GetSourceFile is called and 'requireChecksumMatch' == false then you can call this property to 
        /// determine if the checksum actually matched or not.   This will return true if the original
        /// PDB does not have a checksum (HasChecksum == false)
        /// </summary>; 
        public bool ChecksumMatches { get { return _checksumMatches; } }

        /// <summary>
        /// Obtains information used to download the source file file source link
        /// </summary>
        /// <param name="url">The URL to hit to download the source file</param>
        /// <param name="relativePath">relative file path for the Source Link entry. For example, if the SourceLink map contains 'C:\foo\*' and this maps to 
        /// 'C:\foo\bar\baz.cs', the relativeFilePath is 'bar\baz.cs'. For absolute SourceLink mappings, relativeFilePath will simply be the name of the file.</param>
        /// <returns>true if SourceLink info can be found for this file</returns>
        public virtual bool GetSourceLinkInfo(out string url, out string relativePath)
        {
            return _symbolModule.GetUrlForFilePathUsingSourceLink(BuildTimeFilePath, out url, out relativePath);
        }


        #region private 
        protected SourceFile(ManagedSymbolModule symbolModule) { _symbolModule = symbolModule; }

        protected TextWriter _log { get { return _symbolModule._log; } }

        /// <summary>
        /// Look up the source from the source server.  Returns null if it can't find the source
        /// By default this simply uses the Url to look it up on the web.   If 'Url' returns null
        /// so does this.   
        /// </summary>
        protected virtual string GetSourceFromSrcServer()
        {
            // Search the SourceLink url location 
            string url = Url;
            if (url != null)
            {
                using (var httpClient = new HttpClient())
                {
                    var authorizationHeader = this._symbolModule.SymbolReader.AuthorizationHeaderForSourceLink;
                    if (authorizationHeader != null)
                    {
                        httpClient.DefaultRequestHeaders.Add("Authorization", authorizationHeader);
                    }

                    HttpResponseMessage response = httpClient.GetAsync(url).Result;

                    response.EnsureSuccessStatusCode();
                    Stream content = response.Content.ReadAsStreamAsync().Result;

                    if (this._sha256 == null)
                    {
                        this._sha256 = SHA256.Create();
                    }

                    string cachedLocation = Path.Combine(
                        _symbolModule.SymbolReader.SourceCacheDirectory,
                        BitConverter.ToString(this._sha256.ComputeHash(Encoding.UTF8.GetBytes(url.ToUpperInvariant())))
                            .Replace("-", string.Empty));
                    if (cachedLocation != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cachedLocation));
                        using (FileStream file = File.Create(cachedLocation))
                        {
                            content.CopyTo(file);
                        }

                        return cachedLocation;
                    }
                    else
                    {
                        _log.WriteLine("Warning: SourceCache not set, giving up fetching source from the network.");
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Given 'fileName' which is a path to a file (which may  not exist), set 
        /// _filePath and _checksumMatches appropriately.    Namely _filePath should
        /// always be the 'best' candidate for the source file path (matching checksum
        /// wins, otherwise first existing file wins).  
        /// 
        /// Returns true if we have a perfect match (no additional probing needed).  
        /// </summary>
        private bool ProbeForBestMatch(string filePath)
        {
            // We already have a perfect match, this one can't be better.  
            if (_filePath != null && _checksumMatches)
            {
                return false;
            }

            // If this candidate does not even exist, we can't do anything.  
            if (filePath == null || !File.Exists(filePath))
            {
                _log.WriteLine("  Probe failed, file does not exist {0}", filePath);
                return false;
            }

            if (ComputeChecksumMatch(filePath))
            {
                _checksumMatches = true;
                _filePath = filePath;
                _log.WriteLine("Checksum matches for {0}", filePath);
                return true;
            }

            // If we don't match but we have nothinging better, remember it.   Otherwise do nothing as first hit is better.  
            if (_filePath == null)
            {
                _filePath = filePath;
                _log.WriteLine("Checksum does NOT match for {0}, but it is our best guess.", filePath);
            }
            else
            {
                _log.WriteLine("Checksum does NOT match for {0} but we already have a non-ideal match so discarding this probe.", filePath);
            }

            // We did not get a perfect match.  
            return false;
        }

        /// <summary>
        /// Returns true if 'filePath' matches the checksum OR we don't have a checkdum
        /// (thus if we pass what validity check we have).    
        /// </summary>
        private bool ComputeChecksumMatch(string filePath)
        {
            if (_hashAlgorithm == null && _hash == null)
            {
                return true;
            }

            using (var fileStream = File.OpenRead(filePath))
            {
                byte[] computedHash = _hashAlgorithm.ComputeHash(fileStream);
                return ArrayEquals(computedHash, _hash);
            }
        }

        // Should be in the framework, but I could  not find it quickly.  
        private static bool ArrayEquals(byte[] bytes1, byte[] bytes2)
        {
            if (bytes1.Length != bytes2.Length)
            {
                return false;
            }

            for (int i = 0; i < bytes1.Length; i++)
            {
                if (bytes1[i] != bytes2[i])
                {
                    return false;
                }
            }
            return true;
        }

        // TO be filled in by superclass on construction.  
        protected byte[] _hash;
        protected System.Security.Cryptography.HashAlgorithm _hashAlgorithm;
        protected ManagedSymbolModule _symbolModule;
        protected SHA256 _sha256;

        // Filled in when GetSource() is called.  
        protected string _filePath;
        private bool _getSourceCalled;
        private bool _checksumMatches;
        #endregion
    }
}

