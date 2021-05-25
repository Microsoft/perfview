using Microsoft.Diagnostics.Tracing;
using PerfView.TestUtilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace TraceEventTests
{
    [UseCulture("en-US")]
    public abstract class EtlTestBase : TestBase
    {
        protected EtlTestBase(ITestOutputHelper output)
            : base(output)
        {
        }

        public static IEnumerable<object[]> TestEtlFiles
        {
            get
            {
                // The test data is contained in files of the same name, but with a .zip extension.
                // Only the names are returned since the extracted files will be in a different directory.
                return from file in Directory.EnumerateFiles(TestDataDir, "*.etl.zip")
                       select new[] { Path.GetFileNameWithoutExtension(file) };
            }
        }

        private static bool s_fileUnzipped;

        [MethodImpl(MethodImplOptions.Synchronized)]
        private static void UnzipDataFiles()
        {
            if (s_fileUnzipped)
            {
                return;
            }

            Trace.WriteLine(string.Format("Current Directory: {0}", Environment.CurrentDirectory));
            Trace.WriteLine(string.Format("TestDataDir Directory: {0}", Path.GetFullPath(TestDataDir)));
            Trace.WriteLine(string.Format("Unzipped Directory: {0}", Path.GetFullPath(UnZippedDataDir)));

            foreach (var dataFile in Directory.EnumerateFiles(TestDataDir, "*.etl.zip"))
            {
                string etlFilePath = Path.Combine(UnZippedDataDir, Path.GetFileNameWithoutExtension(dataFile));
                if (!File.Exists(etlFilePath) || File.GetLastWriteTimeUtc(etlFilePath) < File.GetLastWriteTimeUtc(dataFile))
                {
                    Trace.WriteLine(string.Format("Unzipping File {0} -> {1}", dataFile, etlFilePath));
                    var zipReader = new ZippedETLReader(dataFile);
                    zipReader.SymbolDirectory = Path.Combine(UnZippedDataDir, "Symbols");
                    zipReader.EtlFileName = etlFilePath;
                    zipReader.UnpackArchive();
                }
                else
                {
                    Trace.WriteLine(string.Format("using cached ETL file {0}", etlFilePath));
                }

                Assert.True(File.Exists(etlFilePath));
            }

            foreach (var folderZip in Directory.EnumerateFiles(TestDataDir, "*-diagsession.zip"))
            {
                var zipFile = ZipFile.OpenRead(folderZip);
                var dir = Path.Combine(UnZippedDataDir, Path.GetFileNameWithoutExtension(folderZip));
                if (!Directory.Exists(dir) || Directory.GetLastWriteTimeUtc(dir) < File.GetLastWriteTimeUtc(folderZip))
                {
                    zipFile.ExtractToDirectory(UnZippedDataDir);
                }
                else
                {
                    Trace.WriteLine(string.Format("using cached ETL files {0}", dir));
                }
            }

            Trace.WriteLine("Finished unzipping data");
            s_fileUnzipped = true;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        protected void PrepareTestData()
        {
            Assert.True(Directory.Exists(TestDataDir));
            TestDataDir = Path.GetFullPath(TestDataDir);
            Assert.True(Directory.Exists(OriginalBaselineDir));
            OriginalBaselineDir = Path.GetFullPath(OriginalBaselineDir);

            // This is all atomic because this method is synchronized.  
            Assert.True(Directory.Exists(TestDataDir));
            UnzipDataFiles();
            if (Directory.Exists(OutputDir))
            {
                Directory.Delete(OutputDir, true);
            }

            Directory.CreateDirectory(OutputDir);
            Output.WriteLine(string.Format("OutputDir: {0}", OutputDir));
            Assert.True(Path.GetFullPath(OutputDir) == OutputDir);

            Directory.CreateDirectory(NewBaselineDir);
            NewBaselineDir = Path.GetFullPath(NewBaselineDir);
            Output.WriteLine(string.Format("NewBaselineDir: {0}", NewBaselineDir));

            Assert.True(Directory.Exists(UnZippedDataDir));
            UnZippedDataDir = Path.GetFullPath(UnZippedDataDir);
            Assert.True(Directory.Exists(BaseOutputDir));
            BaseOutputDir = Path.GetFullPath(BaseOutputDir);
            Assert.True(Directory.Exists(NewBaselineDir));
        }
    }
}
