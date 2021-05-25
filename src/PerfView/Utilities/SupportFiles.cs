using Microsoft.Diagnostics.Utilities;
using PerfView;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Utilities
{
    /// <summary>
    /// SupportFiles is a class that manages the unpacking of DLLs and other resources.  
    /// This allows you to make your EXE the 'only' file in the distribution, and all other
    /// files are unpacked from that EXE.   
    /// 
    /// To add a file to the EXE as a resource you need to add the following lines to the .csproj
    /// In the example below we added the TraceEvent.dll file (relative to the project directory).
    /// LogicalName must start with a .\ and is the relative path from the SupportFiles directory
    /// where the file will be placed.  Adding the Link tag makes it show up in a pretty way in
    /// solution explorer.  
    /// 
    /// <ItemGroup>
    ///  <EmbeddedResource Include="..\TraceEvent\$(OutDir)Microsoft.Diagnostics.Tracing.TraceEvent.dll">
    ///   <Type>Non-Resx</Type>
    ///   <WithCulture>false</WithCulture>
    ///   <LogicalName>.\TraceEvent.dll</LogicalName>
    ///  </EmbeddedResource>
    /// </ItemGroup>
    /// 
    /// By default SupportFiles registers an Assembly resolve helper so that if you reference
    /// any DLLs in the project the .NET runtime will look for them in the support files directory. 
    /// 
    /// You just need to be careful to call 'UnpackResourcesIfNeeded' in your Main method and 
    /// don't use any of your support DLLs in the main method itself (you can use it in any method
    /// called from main).   If necessary, put everything in a 'MainWorker' method except the 
    /// call to UnpackResourcesIfNeeded.
    /// 
    /// Everything you deploy goes in its own version directory where the version is the timestamp
    /// of the EXE.   Thus newer version of your EXE can run with an older version and they don't
    /// cobber each other.    Newer version WILL delete older version by only if the directory
    /// is unlocked (no-one is using it).   Thus there tends to only be one version. 
    /// 
    /// While UnpackResourcesIfNeeded will keep only one version it will not clean it up to
    /// zero.  You have to write your own '/uninstall' or 'Cleanup' that deletes SupportFileDir
    /// if you want this.  
    /// </summary>
    internal static class SupportFiles
    {
        /// <summary>
        /// Unpacks any resource that beginning with a .\ (so it looks like a relative path name)
        /// Such resources are unpacked into their relative position in SupportFileDir. 
        /// 'force' will force an update even if the files were unpacked already (usually not needed)
        /// The function returns true if files were unpacked.  
        /// </summary>
        public static bool UnpackResourcesIfNeeded(bool force = false)
        {
            var unpacked = false;
            if (Directory.Exists(SupportFileDir))
            {
                if (force)
                {
                    Directory.Delete(SupportFileDir);
                    UnpackResources();
                    unpacked = true;
                }
            }
            else
            {
                UnpackResources();
                unpacked = true;
            }

            // Register a Assembly resolve event handler so that we find our support dlls in the support dir.
            AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs args)
            {
                var simpleName = args.Name;
                var commaIdx = simpleName.IndexOf(',');
                if (0 <= commaIdx)
                {
                    simpleName = simpleName.Substring(0, commaIdx);
                }

                string fileName = Path.Combine(SupportFileDir, simpleName + ".dll");
                if (File.Exists(fileName))
                {
                    return System.Reflection.Assembly.LoadFrom(fileName);
                }

                // Also look in processor specific location
                fileName = Path.Combine(SupportFileDir, ProcessArchitectureDirectory, simpleName + ".dll");
                if (File.Exists(fileName))
                {
                    return System.Reflection.Assembly.LoadFrom(fileName);
                }

                // And look for an exe (we need this for HeapDump.exe)
                fileName = Path.Combine(SupportFileDir, ProcessArchitectureDirectory, simpleName + ".exe");
                if (File.Exists(fileName))
                {
                    return System.Reflection.Assembly.LoadFrom(fileName);
                }

                // If asked, look in other locations as well. 
                if (s_managedDllSearchPaths != null)
                {
                    foreach (var dir in s_managedDllSearchPaths)
                    {
                        fileName = Path.Combine(dir, simpleName + ".dll");
                        if (File.Exists(fileName))
                        {
                            return System.Reflection.Assembly.LoadFrom(fileName);
                        }
                    }
                }
                return null;
            };

            // Do we need to cleanup old files?
            // Note we do this AFTER setting up the Assemble Resolve event because we use FileUtiltities that
            // may not be in the EXE itself.  
            if (unpacked || File.Exists(Path.Combine(SupportFileDirBase, "CleanupNeeded")))
            {
                Cleanup();
            }

            return unpacked;
        }
        /// <summary>
        /// SupportFileDir is a directory that is reserved for CURRENT VERSION of the software (if a later version is installed)
        /// It gets its own directory).   This is the directory where files in the EXE get unpacked to.  
        /// </summary>
        public static string SupportFileDir
        {
            get
            {
                {
                    var exeLastWriteTime = File.GetLastWriteTime(MainAssemblyPath);
                    var version = exeLastWriteTime.ToString("VER.yyyy'-'MM'-'dd'.'HH'.'mm'.'ss.fff");
                    s_supportFileDir = Path.Combine(SupportFileDirBase, version);
                }
                return s_supportFileDir;
            }
        }
        /// <summary>
        /// You must have write access to this directory.  It does not need to exist, but 
        /// if not, users have to have permission to create it.   This directory should only
        /// be used for this app only (not shared with other things).    By default we choose
        /// %APPDATA%\APPNAME where APPNAME is the name of the application (EXE file name 
        /// without the extension). 
        /// </summary>
        public static string SupportFileDirBase
        {
            get
            {
                if (s_supportFileDirBase == null)
                {
                    string appName = Path.GetFileNameWithoutExtension(MainAssemblyPath);

                    string appData = Environment.GetEnvironmentVariable(appName + "_APPDATA");
                    if (appData == null)
                    {
                        appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        if (appData == null)
                        {
                            appData = Path.GetFileName(MainAssemblyPath);
                        }
                    }
                    s_supportFileDirBase = Path.Combine(appData, appName);
                }
                return s_supportFileDirBase;
            }
            set { s_supportFileDirBase = value; }
        }
        /// <summary>
        /// The path to the assembly containing <see cref="SupportFiles"/>. You should not be writing here! that is what
        /// <see cref="SupportFileDir"/> is for.
        /// </summary>
        public static string MainAssemblyPath
        {
            get
            {
                if (s_mainAssemblyPath == null)
                {
                    var mainAssembly = Assembly.GetExecutingAssembly();
                    s_mainAssemblyPath = mainAssembly.ManifestModule.FullyQualifiedName;
                }

                return s_mainAssemblyPath;
            }
        }
        /// <summary>
        /// The path to the entry executable.
        /// </summary>
        public static string ExePath
        {
            get
            {
                if (s_exePath == null)
                {
                    s_exePath = Assembly.GetEntryAssembly().ManifestModule.FullyQualifiedName;
                }

                return s_exePath;
            }
        }

        /// <summary>
        /// Get the name of the architecture of the current process
        /// </summary>
        public static ProcessorArchitecture ProcessArch
        {
            get
            {
                if (s_ProcessArch == ProcessorArchitecture.None)
                {
                    var processorArchitecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                    if (!Enum.TryParse(processorArchitecture, ignoreCase: true, result: out s_ProcessArch))
                    {
                        s_ProcessArch = Environment.Is64BitProcess ? ProcessorArchitecture.Amd64 : ProcessorArchitecture.X86;
                    }
                }

                return s_ProcessArch;
            }
        }

        /// <summary>
        /// Gets the name of the directory containing compiled binaries (DLLs) which have the same architecture as the
        /// currently executing process.
        /// </summary>
        public static string ProcessArchitectureDirectory
        {
            get
            {
                if (s_ProcessArchDirectory == null)
                {
                    s_ProcessArchDirectory = ProcessArch.ToString().ToLowerInvariant();
                }

                return s_ProcessArchDirectory;
            }
        }

        /// <summary>
        /// If you need to load an unmanaged DLL that is part of your distribution
        /// This routine will do the load library using the correct architecture
        /// </summary>
        /// <param name="relativePath"></param>
        public static void LoadNative(string relativePath)
        {
            var archPath = Path.Combine(ProcessArchitectureDirectory, relativePath);
            var fullPath = Path.Combine(SupportFileDir, archPath);
            var ret = LoadLibrary(fullPath);
            if (ret == IntPtr.Zero)
            {
                if (!File.Exists(fullPath))
                {
                    if (ProcessArch != ProcessorArchitecture.X86)
                    {
                        var x86FullPath = Path.Combine(SupportFileDir, "x86", relativePath);
                        if (File.Exists(x86FullPath))
                        {
                            throw new ApplicationException("This operation is not supported for the " + ProcessArch + " architecture.");
                        }
                    }
                    throw new ApplicationException("Could not find DLL " + archPath + " in distribution.  Application Error.");
                }
            }
        }

        /// <summary>
        /// This allows you to add a directory that will be searched when looking up managed DLLs.
        /// It is useful to do this to find DLLs used for extensibility.  
        /// </summary>
        public static void AddManagedDllSearchPath(string directory)
        {
            Debug.Assert(Directory.Exists(directory));
            if (s_managedDllSearchPaths == null)
            {
                s_managedDllSearchPaths = new List<string>();
            }

            if (s_managedDllSearchPaths.Contains(directory))
            {
                return;
            }

            s_managedDllSearchPaths.Add(directory);
        }

        #region private
        private static void UnpackResources()
        {
            // We don't unpack into the final directory so we can be transactional (all or nothing).  
            string prepDir = SupportFileDir + ".new";
            Directory.CreateDirectory(prepDir);

            // Unpack the files.  
            // We used to used GetEntryAssembly, but that makes using PerfView as a component of a larger EXE
            // problematic.   Instead use GetExecutingAssembly, which means that you have to put SupportFiles.cs
            // in your main program 
            var resourceAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            foreach (var resourceName in resourceAssembly.GetManifestResourceNames())
            {
                if (resourceName.StartsWith(@".\") || resourceName.StartsWith(@"./"))
                {
                    // Unpack everything, inefficient, but insures ldr64 works.  
                    string targetPath = Path.Combine(prepDir, resourceName);
                    if (!ResourceUtilities.UnpackResourceAsFile(resourceName, targetPath, resourceAssembly))
                    {
                        throw new ApplicationException("Could not unpack support file " + resourceName);
                    }
                }
            }

            // Commit the unpack, we try several times since antiviruses often lock the directory
            for (int retries = 0; ; retries++)
            {
                try
                {
                    Directory.Move(prepDir, SupportFileDir);
                    break;
                }
                catch (Exception)
                {
                    if (retries > 5)
                    {
                        throw;
                    }
                }
                System.Threading.Thread.Sleep(100);
            }
        }

        private static void Cleanup()
        {
            string cleanupMarkerFile = Path.Combine(SupportFileDirBase, "CleanupNeeded");
            var dirs = Directory.GetDirectories(SupportFileDirBase, "VER.*");
            if (dirs.Length > 1)
            {
                // We will assume we should come and check again on our next launch.  
                File.WriteAllText(cleanupMarkerFile, "");
                foreach (string dir in Directory.GetDirectories(s_supportFileDirBase))
                {
                    // Don't clean up myself
                    if (string.Compare(dir, s_supportFileDir, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        continue;
                    }

                    // We first try to move the directory and only delete it if that succeeds.  
                    // That way directories that are in use don't get cleaned up.    
                    try
                    {
                        var deletingName = dir + ".deleting";
                        if (dir.EndsWith(".deleting"))
                        {
                            deletingName = dir;
                        }
                        else
                        {
                            Directory.Move(dir, deletingName);
                        }

                        DirectoryUtilities.Clean(deletingName);
                    }
                    catch (Exception) { }
                }
            }
            else
            {
                // No cleanup needed, mark that fact
                FileUtilities.ForceDelete(cleanupMarkerFile);
            }
        }

        /// <summary>
        /// This is a convinience function.  If you unpack native dlls, you may want to simply LoadLibary them
        /// so that they are guaranteed to be found when needed.  
        /// </summary>
        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);


        private static ProcessorArchitecture s_ProcessArch;
        private static string s_ProcessArchDirectory;


        private static string s_supportFileDir;
        private static string s_supportFileDirBase;
        private static string s_mainAssemblyPath;
        private static string s_exePath;
        private static List<string> s_managedDllSearchPaths;
        #endregion
    }
}
