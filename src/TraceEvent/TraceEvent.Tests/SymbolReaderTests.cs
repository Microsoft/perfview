using Microsoft.Diagnostics.Symbols;
using PerfView.TestUtilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TraceEventTests
{
    [UseCulture("en-US")]
    public class SymbolReaderTests : TestBase
    {
        private const string FileName_CsPortablePdb1 = "CsPortablePdb1.pdb";
        private const string FileName_CppConPdb = "CppCon.pdb";
        private const string FileName_CsDesktopPdbWithSourceLink = "CsDesktopWithSourceLink.pdb";

        private static readonly object s_fileLock = new object();
        private static string s_inputPdbDir;

        readonly SymbolReader _symbolReader = new SymbolReader(TextWriter.Null);

        public SymbolReaderTests(ITestOutputHelper output)
            : base(output)
        {
            PrepareTestData();
        }


        [Fact]
        public void NativeCppPdbHasValidSourceInfo()
        {
            var pdbFile = _symbolReader.OpenNativeSymbolFile(Path.Combine(s_inputPdbDir, FileName_CppConPdb));
            using (pdbFile as IDisposable)
            {
                SourceLocation sourceLocation = pdbFile.SourceLocationForRva(4096);
                Assert.NotNull(sourceLocation);

                var sourceFile = sourceLocation.SourceFile;
                Assert.NotNull(sourceFile);
                Assert.Equal(@"C:\PerfViewTestData\CppCon\CppCon.cpp", sourceFile.BuildTimeFilePath, StringComparer.OrdinalIgnoreCase);
                Assert.Equal("MD5", sourceFile.ChecksumAlgorithm);
                Assert.Equal("4385B143F32D90E0D9F5340774D6873D", ChecksumToString(sourceFile.ChecksumValue));
                Assert.True(sourceFile.GetSourceLinkInfo(out string url, out string relativePath));
                Assert.Equal("https:://contoso.com/fake-source-link-url/CppCon.cpp", url);
                Assert.Equal("CppCon.cpp", relativePath);

                Assert.Equal(7, sourceLocation.LineNumber);
            }
        }

        [Fact]
        public void ManagedWinPdbHasValidSourceInfo1()
        {
            var pdbFile = _symbolReader.OpenSymbolFile(Path.Combine(s_inputPdbDir, "CsDesktopProj.pdb"));
            using (pdbFile as IDisposable)
            {
                Assert.IsType<NativeSymbolModule>(pdbFile);

                const int tokenMain = 0x06000001;
                SourceLocation sourceLocation = pdbFile.SourceLocationForManagedCode(tokenMain, ilOffset: 0);
                Assert.NotNull(sourceLocation);

                var sourceFile = sourceLocation.SourceFile;
                Assert.NotNull(sourceFile);
                Assert.Equal(@"C:\PerfViewTestData\CsDesktopProj\Program.cs", sourceFile.BuildTimeFilePath, StringComparer.OrdinalIgnoreCase);
                Assert.Equal("SHA256", sourceFile.ChecksumAlgorithm);
                Assert.Equal("5705741A5E2D7F01C9502134B5948607E8BB01AEB107BE055B496386E493BA75", ChecksumToString(sourceFile.ChecksumValue));
                Assert.False(sourceFile.GetSourceLinkInfo(out string url, out string relativePath));

                Assert.Equal(9, sourceLocation.LineNumber);
            }
        }

        [Fact]
        public void ManagedWinPdbHasValidSourceInfo2()
        {
            var pdbFile = _symbolReader.OpenSymbolFile(Path.Combine(s_inputPdbDir, FileName_CsDesktopPdbWithSourceLink));
            using (pdbFile as IDisposable)
            {
                Assert.IsType<NativeSymbolModule>(pdbFile);

                const int tokenMain = 0x06000001;
                SourceLocation sourceLocation = pdbFile.SourceLocationForManagedCode(tokenMain, ilOffset: 0);
                Assert.NotNull(sourceLocation);

                var sourceFile = sourceLocation.SourceFile;
                Assert.NotNull(sourceFile);
                Assert.Equal(@"C:\PerfViewTestData\CsDesktopWithSourceLink\Program.cs", sourceFile.BuildTimeFilePath, StringComparer.OrdinalIgnoreCase);
                Assert.Equal("SHA256", sourceFile.ChecksumAlgorithm);
                Assert.Equal("A2706557AB7C40E142355E4C6811CA9FBD6344E98CC795B6EEA76FB39A44E915", ChecksumToString(sourceFile.ChecksumValue));
                Assert.True(sourceFile.GetSourceLinkInfo(out string url, out string relativePath));
                Assert.Equal("https:://contoso.com/fake-source-link-url/CsDesktopWithSourceLink/Program.cs", url);
                Assert.Equal("CsDesktopWithSourceLink/Program.cs", relativePath);

                Assert.Equal(13, sourceLocation.LineNumber);
            }
        }

        [Fact]
        public void PortablePdbHasValidSourceInfo()
        {
            var pdbFile = _symbolReader.OpenSymbolFile(Path.Combine(s_inputPdbDir, FileName_CsPortablePdb1));
            using (pdbFile as IDisposable)
            {
                Assert.IsType<PortableSymbolModule>(pdbFile);

                const int tokenMain = 0x06000001;
                SourceLocation sourceLocation = pdbFile.SourceLocationForManagedCode(tokenMain, ilOffset: 0);
                Assert.NotNull(sourceLocation);

                var sourceFile = sourceLocation.SourceFile;
                Assert.NotNull(sourceFile);
                Assert.Equal(@"C:\PerfViewTestData\CsPortablePdb1\Program.cs", sourceFile.BuildTimeFilePath, StringComparer.OrdinalIgnoreCase);
                Assert.Equal("SHA256", sourceFile.ChecksumAlgorithm);
                Assert.Equal("134F2D3A27BE69575627B7A69E96610052738A10017FCA12A299BE28B1C47F76", ChecksumToString(sourceFile.ChecksumValue));
                Assert.True(sourceFile.GetSourceLinkInfo(out string url, out string relativePath));
                Assert.Equal("https:://contoso.com/fake-source-link-url/Program.cs", url);
                Assert.Equal("Program.cs", relativePath);

                Assert.Equal(9, sourceLocation.LineNumber);
            }
        }

        [Theory]
        [InlineData(FileName_CppConPdb)]
        [InlineData(FileName_CsDesktopPdbWithSourceLink)]
        [InlineData(FileName_CsPortablePdb1)]
        public void PdbFileLockingIsCorrect(string pdbFileName)
        {
            const int E_WIN32_ERROR_SHARING_VIOLATION = -2147024864;

            string tempPdbPath = Path.Combine(Path.GetTempPath(), pdbFileName);
            File.Copy(Path.Combine(s_inputPdbDir, pdbFileName), tempPdbPath, overwrite: true);

            try
            {
                bool CanWrite()
                {
                    try
                    {
                        var stream = new FileStream(tempPdbPath, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Write | FileShare.Delete);
                        stream.Dispose();
                        return true;
                    }
                    catch (IOException ioException) when (ioException.HResult == E_WIN32_ERROR_SHARING_VIOLATION)
                    {
                        return false;
                    }
                }

                Assert.True(CanWrite());
                var symbolFile = _symbolReader.OpenSymbolFile(tempPdbPath);
                var disposableSymbolFile = symbolFile as IDisposable;
                Assert.NotNull(disposableSymbolFile);
                Assert.False(CanWrite());

                disposableSymbolFile.Dispose();
                disposableSymbolFile = null;

                Assert.True(CanWrite());
            }
            finally
            {
                try
                {
                    File.Delete(tempPdbPath);
                }
                catch
                {
                }
            }
        }

        private string ChecksumToString(IReadOnlyCollection<byte> checksum)
        {
            if (checksum == null)
            {
                return "<none>";
            }
            else
            {
                // Convert the byte array to hex string
                var sb = new StringBuilder(checksum.Count * 2);
                foreach (byte b in checksum)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", b);
                }

                return sb.ToString();
            }
        }

        protected void PrepareTestData()
        {
            lock (s_fileLock)
            {
                if (s_inputPdbDir is null)
                {
                    Assert.True(Directory.Exists(TestDataDir));
                    TestDataDir = Path.GetFullPath(TestDataDir);

                    UnZippedDataDir = Path.GetFullPath(UnZippedDataDir);
                    Directory.CreateDirectory(UnZippedDataDir);

                    string zipFile = Path.Combine(TestDataDir, "SymbolReaderTestInput.zip");
                    string symbolReaderDataDir = Path.Combine(UnZippedDataDir, Path.GetFileNameWithoutExtension(zipFile));

                    bool symbolReaderDataDirExists = Directory.Exists(symbolReaderDataDir);
                    if (!symbolReaderDataDirExists || File.GetLastWriteTimeUtc(symbolReaderDataDir) < File.GetLastWriteTimeUtc(zipFile))
                    {
                        if (symbolReaderDataDirExists)
                        {
                            Directory.Delete(symbolReaderDataDir, recursive:true);
                        }
                        ZipFile.ExtractToDirectory(zipFile, symbolReaderDataDir);
                    }

                    var inputPdbDir = Path.Combine(symbolReaderDataDir, "bin", "Release");
                    Assert.True(Directory.Exists(inputPdbDir));

                    s_inputPdbDir = inputPdbDir;
                }
            }
        }
    }
}
