using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;


namespace Microsoft.Diagnostics.Symbols
{
    internal class PortableSymbolModule : ManagedSymbolModule, IDisposable
    {
        public PortableSymbolModule(SymbolReader reader, string pdbFileName) : this(reader, File.Open(pdbFileName, FileMode.Open, FileAccess.Read, FileShare.Read), pdbFileName) { }

        public PortableSymbolModule(SymbolReader reader, Stream stream, string pdbFileName = "") : base(reader, pdbFileName)
        {
            _provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            _metaData = _provider.GetMetadataReader();
        }

        public void Dispose() => _provider.Dispose();

        public override Guid PdbGuid
        {
            get
            {
                // The first 16 bytes are the PDB Guid, the next 4 are the DLL timestamp.  
                var idBytes = _metaData.DebugMetadataHeader.Id;
                byte[] guidBytes = new byte[16];
                idBytes.CopyTo(0, guidBytes, 0, guidBytes.Length);
                return new Guid(guidBytes);
            }
        }

        public override SourceLocation SourceLocationForManagedCode(uint methodMetadataToken, int ilOffset)
        {
            MethodDefinitionHandle methodDefinitionHandle = (MethodDefinitionHandle)MetadataTokens.Handle((int)methodMetadataToken);
            MethodDebugInformationHandle methodDebugInfoHandle = methodDefinitionHandle.ToDebugInformationHandle();
            MethodDebugInformation methodDebugInfo = _metaData.GetMethodDebugInformation(methodDebugInfoHandle);

            int methodToken = MetadataTokens.GetToken(methodDebugInfoHandle.ToDefinitionHandle());
            SequencePoint lastSequencePoint = default(SequencePoint);
            foreach (SequencePoint sequencePoint in methodDebugInfo.GetSequencePoints())
            {
                if (sequencePoint.Offset > ilOffset)
                {
                    if (lastSequencePoint.Document.IsNil)
                    {
                        lastSequencePoint = sequencePoint;
                    }

                    break;
                }
                lastSequencePoint = sequencePoint;
            }
            if (lastSequencePoint.Document.IsNil)
            {
                return null;
            }

            return new SourceLocation(GetSourceFile(lastSequencePoint.Document), lastSequencePoint.StartLine);
        }

        #region private 

        protected override IEnumerable<string> GetSourceLinkJson()
        {
            foreach (CustomDebugInformationHandle customDebugInformationHandle in _metaData.CustomDebugInformation)
            {
                CustomDebugInformation customDebugInformation = _metaData.GetCustomDebugInformation(customDebugInformationHandle);

                EntityHandle parent = customDebugInformation.Parent;
                Guid guid = _metaData.GetGuid(customDebugInformation.Kind);
                if (guid == SourceLinkKind)
                {
                    BlobReader blobReader = _metaData.GetBlobReader(customDebugInformation.Value);
                    var ret = blobReader.ReadUTF8(blobReader.Length);
                    return new string[] { ret };
                }
            }

            return Enumerable.Empty<string>();
        }

        private SourceFile GetSourceFile(DocumentHandle documentHandle)
        {
            return new PortablePdbSourceFile(_metaData.GetDocument(documentHandle), this);
        }

        private class PortablePdbSourceFile : SourceFile
        {
            internal PortablePdbSourceFile(Document sourceFileDocument, PortableSymbolModule portablePdb) : base(portablePdb)
            {
                _sourceFileDocument = sourceFileDocument;
                _portablePdb = portablePdb;

                Guid hashAlgorithmGuid = _portablePdb._metaData.GetGuid(_sourceFileDocument.HashAlgorithm);
                if (hashAlgorithmGuid == HashAlgorithmSha1)
                {
                    _hashAlgorithm = System.Security.Cryptography.SHA1.Create();
                }
                else if (hashAlgorithmGuid == HashAlgorithmSha256)
                {
                    _hashAlgorithm = System.Security.Cryptography.SHA256.Create();
                }

                if (_hashAlgorithm != null)
                {
                    _hash = _portablePdb._metaData.GetBlobBytes(_sourceFileDocument.Hash);
                }

                BuildTimeFilePath = _portablePdb._metaData.GetString(_sourceFileDocument.Name);
                _log.WriteLine("Opened Portable Pdb Source File: {0}", BuildTimeFilePath);
            }

            #region private 
            private static Guid HashAlgorithmSha1 = Guid.Parse("ff1816ec-aa5e-4d10-87f7-6f4963833460");
            private static Guid HashAlgorithmSha256 = Guid.Parse("8829d00f-11b8-4213-878b-770e8597ac16");

            // Fields 
            private Document _sourceFileDocument;
            private PortableSymbolModule _portablePdb;
            #endregion
        }   // Class PortablePdbSourceFile

        private static Guid SourceLinkKind = Guid.Parse("CC110556-A091-4D38-9FEC-25AB9A351A6A");

        // Needed by other things to look up data
        internal MetadataReader _metaData;
        private MetadataReaderProvider _provider;
        #endregion
    }
}

