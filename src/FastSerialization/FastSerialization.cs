//     Copyright (c) Microsoft Corporation.  All rights reserved.
/* This file is best viewed using outline mode (Ctrl-M Ctrl-O) */
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
/* If you uncomment this line, log.serialize.xml and log.deserialize.xml are created, which allow debugging */
// #define DEBUG_SERIALIZE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;      // For StringBuilder.

// see #Introduction and #SerializerIntroduction
namespace FastSerialization
{
    // #Introduction
    // 
    // Sadly, System.Runtime.Serialization has a serious performance flaw. In the scheme created there, the
    // basic contract between an object and the serializer is fundamentally heavy. For serialization the
    // contract is for the object to implement System.Runtime.Serialization.ISerializable.GetObjectData
    // and this should a series of AddValue() APIs on System.Runtime.Serialization.SerializationInfo
    // which are given field names and values. The AddValue APIs box the values and place them in a table, It
    // is then the serializers job to actually send out the bits given this table. The REQUIRED work of
    // serializing an integers copying 4 bytes to some output buffer (a few instructions), however the
    // protocol above requires 1000s.
    // 
    // The classes in Serialize.cs are an attempt to create really light weight serialization. At the heart
    // of the design are two interfaces IStreamReader and IStreamWriter. They are a simplified
    // INTERFACE much like System.IO.BinaryReader and System.IO.BinaryWriter, that know how to
    // write only the most common data types (integers and strings). They also fundamentally understand that
    // they are a stream of bytes, and thus have the concept of StreamLabel which is a 'pointer' to a
    // spot in the stream. This is critically important as it allows the serialized form to create a
    // complicated graph of objects in the stream. While IStreamWriter does not have the ability to seek,
    // the IStreamReader does (using StreamLabel), because it is expected that the reader will want
    // to follow StreamLabel 'pointers' to traverse the serialized data in a more random access way.
    // 
    // However, in general, an object needs more than a MemoryStreamWriter to serialize itself. When an object
    // graph could have cycles, it needs a way of remembering which objects it has already serialized. It
    // also needs a way encoding types, because in general the type of an object cannot always be inferred
    // from its context. This is the job of the Serializer class. A Serializer holds all the state
    // needed to represent a partially serialized object graph, but the most important part of a
    // Serializer is its Serializer.writer property, which holds the logical output stream.
    // 
    // Similarly a Deserializer holds all the 'in flight' information needed to deserialize a complete
    // object graph, and its most important property is its Deserializer.reader that holds the logical
    // input stream.
    // 
    // An object becomes serializable by doing two things
    //     * implementing the IFastSerializable interface and implementing the
    //         IFastSerializable.ToStream and IFastSerializable.FromStream methods.
    //     * implementing a public constructor with no arguments (default constructor). This is needed because
    //         an object needs to be created before IFastSerializable.FromStream can be called.
    // 
    // The IFastSerializable.ToStream method that the object implements is passed a Serializer, and
    // the object is free to take advantage of all the facilities (like its serialized object table) to help
    // serialize itself, however at its heart, the ToStream method tends to fetch the Serialier.writer
    // and write out the primitive fields in order. Similarly at the heart of the
    // IFastSerializable.FromStream method is fetching the Deserializer.reader and reading in a
    // series of primitive types.
    // 
    // Now the basic overhead of serializing a object in the common case is
    // 
    //     * A interface call to IFastSerializable.ToStream.
    //     * A fetch of IStreamWriter from the Serialier.writer field
    //     * a series of IStreamWriter.Write operations which is an interface call, plus the logic to
    //         store the actual data to the stream (the real work).
    //         
    // This is MUCH leaner, and now dominated by actual work of copying the data to the output buffer.

    /// <summary>
    /// A StreamLabel represents a position in a IStreamReader or IStreamWriter.
    /// In memory it is represented as a 64 bit signed value but to preserve compat 
    /// with the FastSerializer.1 format it is a 32 bit unsigned value when
    /// serialized in a file. FastSerializer can parse files exceeding 32 bit sizes
    /// as long as the format doesn't persist a StreamLabel in the content. NetTrace 
    /// is an example of this.
    /// During writing it is generated by the IStreamWriter.GetLabel method an
    /// consumed by the IStreamWriter.WriteLabel method. On reading you can use
    /// IStreamReader.Current and and IStreamReader. 
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    enum StreamLabel : long
    {
        /// <summary>
        /// Represents a stream label that is not a valid value
        /// </summary>
        Invalid = (long)-1
    };

    /// <summary>
    /// IStreamWriter is meant to be a very simple streaming protocol. You can write integral types,
    /// strings, and labels to the stream itself.  
    /// 
    /// IStreamWrite can be thought of a simplified System.IO.BinaryWriter, or maybe the writer
    /// part of a System.IO.Stream with a few helpers for primitive types.
    /// 
    /// See also IStreamReader
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    interface IStreamWriter : IDisposable
    {
        /// <summary>
        /// Write a byte to a stream
        /// </summary>
        void Write(byte value);
        /// <summary>
        /// Write a short to a stream
        /// </summary>
        void Write(short value);
        /// <summary>
        /// Write an int to a stream
        /// </summary>
        void Write(int value);
        /// <summary>
        /// Write a long to a stream
        /// </summary>
        void Write(long value);
        /// <summary>
        /// Write a StreamLabel (a pointer to another part of the stream) to a stream
        /// </summary>
        void Write(StreamLabel value);
        /// <summary>
        /// Write a string to a stream (supports null values).  
        /// </summary>
        void Write(string value);
        /// <summary>
        /// Get the stream label for the current position (points at whatever is written next
        /// </summary>
        /// <returns></returns>
        StreamLabel GetLabel();
        /// <summary>
        /// Write a SuffixLabel it must be the last thing written to the stream.   The stream 
        /// guarantees that this value can be efficiently read at any time (probably by seeking
        /// back from the end of the stream)).   The idea is that when you generate a 'tableOfContents'
        /// you can only do this after processing the data (and probably writing it out), If you
        /// remember where you write this table of contents and then write a suffix label to it
        /// as the last thing in the stream using this API, you guarantee that the reader can 
        /// efficiently seek to the end, read the value, and then goto that position.  (See
        /// IStreamReader.GotoSuffixLabel for more)
        /// </summary>
        void WriteSuffixLabel(StreamLabel value);
    }


    /// IStreamReader is meant to be a very simple streaming protocol. You can read integral types,
    /// strings, and labels to the stream itself.  You can also goto labels you have read from the stream. 
    /// 
    /// IStreamReader can be thought of a simplified System.IO.BinaryReder, or maybe the reader
    /// part of a System.IO.Stream with a few helpers for primitive types.
    /// 
    /// See also IStreamWriter
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    interface IStreamReader : IDisposable
    {
        /// <summary>
        /// Read a byte from the stream
        /// </summary>
        byte ReadByte();
        /// <summary>
        /// Read a short from the stream
        /// </summary>
        short ReadInt16();
        /// <summary>
        /// Read an int from the stream
        /// </summary>
        int ReadInt32();
        /// <summary>
        /// Read a long from the stream
        /// </summary>
        long ReadInt64();
        /// <summary>
        /// Read a string from the stream.   Can represent null strings 
        /// </summary>
        string ReadString();
        /// <summary>
        /// Read a span of bytes from the stream.
        /// </summary>
        void Read(byte[] data, int offset, int length);
        /// <summary>
        /// Read a StreamLabel (pointer to some other part of the stream) from the stream
        /// </summary>
        StreamLabel ReadLabel();

        /// <summary>
        /// Goto a location in the stream
        /// </summary>
        void Goto(StreamLabel label);
        /// <summary>
        /// Returns the current position in the stream.  
        /// </summary>
        StreamLabel Current { get; }

        /// <summary>
        /// Sometimes information is only known after writing the entire stream.  This information can be put
        /// on the end of the stream, but there needs to be a way of finding it relative to the end, rather
        /// than from the beginning.   A IStreamReader, however, does not actually let you go 'backwards' easily
        /// because it does not guarantee the size what it writes out (it might compress).  
        /// 
        /// The solution is the concept of a 'suffixLabel' which is location in the stream where you can always 
        /// efficiently get to.
        /// 
        /// It is written with a special API (WriteSuffixLabel that must be the last thing written.   It is 
        /// expected that it simply write an uncompressed StreamLabel.   It can then be used by using the
        /// GotoSTreamLabel() method below.   This goes to this well know position in the stream.   We expect
        /// this is implemented by seeking to the end of the stream, reading the uncompressed streamLabel, 
        /// and then seeking to that position.  
        /// </summary>
        void GotoSuffixLabel();
    }

#if !DOTNET_V35
    /// <summary>
    /// Support for higher level operations on IStreamWriter and IStreamReader
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    static class IStreamWriterExentions
    {
        /// <summary>
        /// Writes a Guid to stream 'writer' as sequence of 8 bytes
        /// </summary>
        public static void Write(this IStreamWriter writer, Guid guid)
        {
            byte[] bytes = guid.ToByteArray();
            for (int i = 0; i < bytes.Length; i++)
            {
                writer.Write(bytes[i]);
            }
        }
        /// <summary>
        /// Reads a Guid to stream 'reader' as sequence of 8 bytes and returns it
        /// </summary>
        public static Guid ReadGuid(this IStreamReader reader)
        {
            byte[] bytes = new byte[16];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = reader.ReadByte();
            }

            return new Guid(bytes);
        }

        public static string ReadNullTerminatedUnicodeString(this IStreamReader reader, StringBuilder sb = null)
        {
            if (sb == null)
            {
                sb = new StringBuilder();
            }

            short value = reader.ReadInt16();
            while (value != 0)
            {
                sb.Append(Convert.ToChar(value));
                value = reader.ReadInt16();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns a StreamLabel that is the sum of label + offset.  
        /// </summary>
        public static StreamLabel Add(this StreamLabel label, int offset)
        {
            return (StreamLabel)((long)label + offset);
        }

        /// <summary>
        /// Returns the difference between two stream labels 
        /// </summary>
        public static long Sub(this StreamLabel label, StreamLabel other)
        {
            return (long)label - (long)other;
        }

        /// <summary>
        /// Convenience method for skipping a a certain number of bytes in the stream.  
        /// </summary>
        public static void Skip(this IStreamReader reader, int byteCount)
        {
            reader.Goto((StreamLabel)((long)reader.Current + byteCount));
        }
    }
#endif

    /// <summary>
    /// Like a StreamLabel, a ForwardReference represents a pointer to a location in the stream.  
    /// However unlike a StreamLabel, the exact value in the stream does not need to be known at the
    /// time the forward references is written.  Instead the ID is written, and later that ID is 
    /// associated with the target location (using DefineForwardReference).   
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    enum ForwardReference : int
    {
        /// <summary>
        /// Returned when no appropriate ForwardReference exists.  
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// #SerializerIntroduction see also #StreamLayout
    /// 
    /// The Serializer class is a general purpose object graph serializer helper. While it does not have
    /// any knowledge of the serialization format of individual object, it does impose conventions on how to
    /// serialize support information like the header (which holds versioning information), a trailer (which
    /// holds deferred pointer information), and how types are versioned. However these conventions are
    /// intended to be very generic and thus this class can be used for essentially any serialization need.
    /// 
    /// Goals:
    ///     * Allows full range of serialization, including subclassing and cyclic object graphs.
    ///     * Can be serialized and deserialized efficiently sequentially (no seeks MANDATED on read or
    ///         write). This allows the serializer to be used over pipes and other non-seekable devices).
    ///     * Pay for play (thus very efficient in simple cases (no subclassing or cyclic graphs).
    ///     * Ideally self-describing, and debuggable (output as XML if desired?)
    /// 
    /// Versioning:
    ///     * We want the ability for new formats to accept old versions if objects wish to support old
    ///         formats
    ///     * Also wish to allow new formats to be read by OLD version if the new format is just an
    ///         'extension' (data added to end of objects). This makes making new versions almost pain-free.
    ///         
    /// Concepts:
    ///     * No-seek requirement
    ///     
    ///         The serialized form should be such that it can be deserialized efficiently in a serial fashion
    ///         (no seeks). This means all information needed to deserialize has to be 'just in time' (can't
    ///         be some table at the end). Pragmatically this means that type information (needed to create
    ///         instances), has to be output on first use, so it is available for the deserializer.
    ///         
    ///     * Laziness requirement
    ///     
    ///         While is should be possible to read the serialized for sequentially, we should also not force
    ///         it. It should be possible to have a large file that represents a persisted structure that can
    ///         be lazily brought into memory on demand. This means that all information needed to
    ///         deserialize must also be 'randomly available' and not depend on reading from the beginning.
    ///         Pragmatically this means that type information, and forward forwardReference information needs to
    ///         have a table in a well known Location at the end so that it can be found without having to
    ///         search the file sequentially.
    ///     
    ///     * Versioning requirement
    ///         
    ///         To allow OLD code to access NEW formats, it must be the case that the serialized form of
    ///         every instance knows how to 'skip' past any new data (even if it does not know its exact
    ///         size). To support this, objects have 'begin' and 'end' tags, which allows the deserializer to
    ///         skip the next object.
    ///         
    ///     * Polymorphism requirement
    ///     
    ///         Because the user of a filed may not know the exact instance stored there, in general objects
    ///         need to store the exact type of the instance. Thus they need to store a type identifier, this
    ///         can be folded into the 'begin' tag.
    ///         
    ///     * Arbitrary object graph (circularity) requirement (Forward references)
    ///     
    ///         The serializer needs to be able to serialize arbitrary object graphs, including those with
    ///         cycles in them. While you can do this without forward references, the system is more flexible
    ///         if it has the concept of a forward reference. Thus whenever a object reference is required, a
    ///         'forward forwardReference' can be given instead. What gets serialized is simply an unique forward
    ///         reference index (index into an array), and at some later time that index is given its true
    ///         value. This can either happen with the target object is serialized (see
    ///         Serializer.Tags.ForwardDefintion) or at the end of the serialization in a forward
    ///         reference table (which allows forward references to be resolved without scanning then entire
    ///         file.
    ///         
    ///     * Contract between objects IFastSerializable.ToStream:
    ///     
    ///         The heart of the serialization and deserialization process the IFastSerializable
    ///         interface, which implements just two methods: ToStream (for serializing an object), and
    ///         FromStream (for deserializing and object). This interfaces is the mechanism by which objects
    ///         tell the serializer what data to store for an individual instance. However this core is not
    ///         enough. An object that implements IFastSerializable must also implement a default
    ///         constructor (constructor with no args), so that that deserializer can create the object (and
    ///         then call FromStream to populated it).
    ///         
    ///         The ToStream method is only responsible for serializing the data in the object, and by itself
    ///         is not sufficient to serialize an interconnected, polymorphic graph of objects. It needs
    ///         help from the Serializer and Deserialize to do this. Serializer takes on the
    ///         responsibility to deal with persisting type information (so that Deserialize can create
    ///         the correct type before IFastSerializable.FromStream is called). It is also the
    ///         serializer's responsibility to provide the mechanism for dealing with circular object graphs
    ///         and forward references.
    ///     
    ///     * Layout of a serialized object: A serialized object has the following basic format
    ///     
    ///         * If the object is the definition of a previous forward references, then the definition must
    ///             begin with a Serializer.Tags.ForwardDefintion tag followed by a forward forwardReference
    ///             index which is being defined.
    ///         * Serializer.Tags.BeginObject tag
    ///         * A reference to the SerializationType for the object. This reference CANNOT be a
    ///             forward forwardReference because its value is needed during the deserialization process before
    ///             forward references are resolved.
    ///         * All the data that that objects 'IFastSerializable.ToStream method wrote. This is the
    ///             heart of the deserialized data, and the object itself has a lot of control over this
    ///             format.
    ///         * Serializer.Tags.EndObject tag. This marks the end of the object. It quickly finds bugs
    ///             in ToStream FromStream mismatches, and also allows for V1 deserializers to skip past
    ///             additional fields added since V1.
    ///         
    ///     * Serializing Object references:
    ///       When an object forwardReference is serialized, any of the following may follow in the stream
    ///       
    ///         * Serializer.Tags.NullReference used to encode a null object forwardReference.
    ///         * Serializer.Tags.BeginObject or Serializer.Tags.ForwardDefintion, which indicates
    ///             that this the first time the target object has been referenced, and the target is being
    ///             serialized on the spot.
    ///         * Serializer.Tags.ObjectReference which indicates that the target object has already
    ///             been serialized and what follows is the StreamLabel of where the definition is.
    ///         * Serializer.Tags.ForwardReference followed by a new forward forwardReference index. This
    ///             indicates that the object is not yet serialized, but the serializer has chosen not to
    ///             immediately serialize the object. Ultimately this object will be defined, but has not
    ///             happened yet.
    ///            
    ///     * Serializing Types:
    ///       Types are simply objects of type SerializationType which contain enough information about
    ///       the type for the Deserializer to do its work (it full name and version number).   They are
    ///       serialized just like all other types.  The only thing special about it is that references to
    ///       types after the BeginObject tag must not be forward references.  
    ///  
    /// #StreamLayout:
    ///     The structure of the file as a whole is simply a list of objects.  The first and last objects in
    ///     the file are part of the serialization infrastructure.  
    ///     
    /// Layout Synopsis
    ///     * Signature representing Serializer format
    ///     * EntryObject (most of the rest of the file)
    ///         * BeginObject tag
    ///         * Type for This object (which is a object of type SerializationType)
    ///             * BeginObject tag
    ///             * Type for SerializationType  POSITION1
    ///                 * BeginObject tag
    ///                 * Type for SerializationType
    ///                      * ObjectReference tag           // This is how our recursion ends.  
    ///                      * StreamLabel for POSITION1
    ///                 * Version Field for SerializationType
    ///                 * Minimum Version Field for SerializationType
    ///                 * FullName string for SerializationType                
    ///                 * EndObject tag
    ///             * Version field for EntryObject's type
    ///             * Minimum Version field for EntryObject's type
    ///             * FullName string for EntryObject's type
    ///             * EndObject tag
    ///         * Field1  
    ///         * Field2 
    ///         * V2_Field (this should be tagged so that it can be skipped by V1 deserializers.  
    ///         * EndObject tag
    ///     * ForwardReferenceTable pseudo-object
    ///         * Count of forward references
    ///         * StreamLabel for forward ref 0
    ///         * StreamLabel for forward ref 1.
    ///         * ...
    ///     * SerializationTrailer pseudo-object
    ///         * StreamLabel ForwardReferenceTable
    ///     * StreamLabel to SerializationTrailer
    ///     * End of stream
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    sealed class Serializer : IDisposable
    {
        /// <summary>
        /// Create a serializer writes 'entryObject' to a file.  
        /// </summary>
        public Serializer(string filePath, IFastSerializable entryObject) : this(new IOStreamStreamWriter(filePath), entryObject) { }

        /// <summary>
        /// Create a serializer that writes <paramref name="entryObject"/> to a <see cref="Stream"/>. The serializer
        /// will close the stream when it closes.
        /// </summary>
        public Serializer(Stream outputStream, IFastSerializable entryObject)
            : this(outputStream, entryObject, false)
        {
        }

        /// <summary>
        /// Create a serializer that writes <paramref name="entryObject"/> to a <see cref="Stream"/>. The
        /// <paramref name="leaveOpen"/> parameter determines whether the serializer will close the stream when it
        /// closes.
        /// </summary>
        public Serializer(Stream outputStream, IFastSerializable entryObject, bool leaveOpen)
            : this(new IOStreamStreamWriter(outputStream, leaveOpen: leaveOpen), entryObject)
        {
        }

        /// <summary>
        /// Create a serializer that writes 'entryObject' another IStreamWriter 
        /// </summary>
        public Serializer(IStreamWriter writer, IFastSerializable entryObject)
        {
            bool succeeded = false;
            try
            {
                TypesInGraph = new Dictionary<RuntimeTypeHandle, SerializationType>();
                ObjectsInGraph = new Dictionary<IFastSerializable, StreamLabel>();
                ObjectsWithForwardReferences = new Dictionary<IFastSerializable, ForwardReference>();
                this.writer = writer;

                Log("<Serializer>");
                // Write the header. 
                Write("!FastSerialization.1");

                // Write the main object.  This is recursive and does most of the work. 
                Write(entryObject);

                // Write any forward references. 
                WriteDeferedObjects();

                // Write an unbalanced EndObject tag to represent the end of objects. 
                WriteTag(Tags.EndObject);

                // Write the forward forwardReference table (for random access lookup)  
                StreamLabel forwardRefsLabel = writer.GetLabel();
                Log("<ForwardRefTable StreamLabel=\"0x" + forwardRefsLabel.ToString("x") + "\">");
                if (forwardReferenceDefinitions != null)
                {
                    Write(forwardReferenceDefinitions.Count);
                    for (int i = 0; i < forwardReferenceDefinitions.Count; i++)
                    {
                        Debug.Assert(forwardReferenceDefinitions[i] != StreamLabel.Invalid);
                        Log("<ForwardDefEntry index=\"" + i + "\" StreamLabelRef=\"0x" + forwardReferenceDefinitions[i].ToString("x") + "\"/>");
                        writer.Write(forwardReferenceDefinitions[i]);
                    }
                }
                else
                {
                    Write(0);
                }

                Log("</ForwardRefTable>");

                // Write the trailer currently it has only one item in it, however it is expandable. 
                // items.  
                StreamLabel trailerLabel = writer.GetLabel();
                Log("<Trailer StreamLabel=\"0x" + trailerLabel.ToString("x") + "\">");
                Write(forwardRefsLabel);
                // More stuff goes here in future versions. 
                Log("</Trailer>");

                Log("<WriteSuffixLabel StreamLabelRef=\"0x" + trailerLabel.ToString("x") + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
                writer.WriteSuffixLabel(trailerLabel);
                Log("</Serializer>");
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    writer.Dispose();
                }
            }
        }

        // Convenience functions. 
        /// <summary>
        /// Write a bool to a stream
        /// </summary>
        public void Write(bool value)
        {
            Write((byte)(value ? 1 : 0));
        }
        /// <summary>
        /// Write a byte to a stream
        /// </summary>
        public void Write(byte value)
        {
            Log("<Write Type=\"byte\" Value=\"" + value + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            writer.Write(value);
        }
        /// <summary>
        /// Write a short to a stream
        /// </summary>
        public void Write(short value)
        {
            Log("<Write Type=\"short\" Value=\"" + value + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            writer.Write(value);
        }
        /// <summary>
        /// Write an int to a stream
        /// </summary>
        public void Write(int value)
        {
            Log("<Write Type=\"int\" Value=\"" + value + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            writer.Write(value);
        }
        /// <summary>
        /// Write a long to a stream
        /// </summary>
        public void Write(long value)
        {
            Log("<Write Type=\"long\" Value=\"" + value + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            writer.Write(value);
        }
        /// <summary>
        /// Write a Guid to a stream
        /// </summary>
        public void Write(Guid value)
        {
            Log("<Write Type=\"Guid\" Value=\"" + value + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            byte[] bytes = value.ToByteArray();
            for (int i = 0; i < bytes.Length; i++)
            {
                writer.Write(bytes[i]);
            }
        }
        /// <summary>
        /// Write a string to a stream
        /// </summary>
        public void Write(string value)
        {
#if DEBUG
            if (value == null)
                Log("<Write Type=\"null string\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            else
                Log("<Write Type=\"string\" Value=" + value + " StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
#endif
            writer.Write(value);
        }
        /// <summary>
        /// Write a float to a stream
        /// </summary>
        public unsafe void Write(float value)
        {
            int* intPtr = (int*)&value;
            writer.Write(*intPtr);
        }
        /// <summary>
        /// Write a double to a stream
        /// </summary>
        public unsafe void Write(double value)
        {
            long* longPtr = (long*)&value;
            writer.Write(*longPtr);
        }
        /// <summary>
        /// Write a StreamLabel (pointer to some other part of the stream whose location is current known) to the stream
        /// </summary>
        public void Write(StreamLabel value)
        {
            Log("<Write Type=\"StreamLabel\" StreamLabelRef=\"0x" + value.ToString("x") + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            writer.Write(value);
        }
        /// <summary>
        /// Write a ForwardReference (pointer to some other part of the stream that whose location is not currently known) to the stream
        /// </summary>
        public void Write(ForwardReference value)
        {
            Log("<Write Type=\"ForwardReference\" indexRef=\"" + value + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            writer.Write((int)value);
        }
        /// <summary>
        /// If the object is potentially aliased (multiple references to it), you should write it with this method.
        /// </summary>
        public void Write(IFastSerializable obj) { WriteObjectRef(obj, false); }
        /// <summary>
        /// To tune working set (or disk seeks), or to make the dump of the format more readable, it is
        /// valuable to have control over which of several references to an object will actually cause it to
        /// be serialized (by default the first encountered does it).
        /// 
        /// WriteDefered allows you to write just a forwardReference to an object with the expectation that
        /// somewhere later in the serialization process the object will be serialized. If no call to
        /// WriteObject() occurs, then the object is serialized automatically before the stream is closed
        /// (thus dangling references are impossible).        
        /// </summary>
        public void WriteDefered(IFastSerializable obj) { WriteObjectRef(obj, true); }
        /// <summary>
        /// This is an optimized version of WriteObjectReference that can be used in some cases.
        /// 
        /// If the object is not aliased (it has an 'owner' and only that owner has references to it (which
        /// implies its lifetime is strictly less than its owners), then the serialization system does not
        /// need to put the object in the 'interning' table. This saves a space (entries in the intern table
        /// as well as 'SyncEntry' overhead of creating hash codes for object) as well as time (to create
        /// that bookkeeping) for each object that is treated as private (which can add up if because it is
        /// common that many objects are private).  The private instances are also marked in the serialized
        /// format so on reading there is a similar bookkeeping savings. 
        /// 
        /// The ultimate bits written by WritePrivateObject are the same as WriteObject.
        /// 
        /// TODO Need a DEBUG mode where we detect if others besides the owner reference the object.
        /// </summary>
        public void WritePrivate(IFastSerializable obj)
        {
            Log("<WritePrivateObject obj=\"0x" + obj.GetHashCode().ToString("x") +
                "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\">");
            WriteObjectData(obj, Tags.BeginPrivateObject);
            Log("</WritePrivateObject>");
        }

        // forward reference support 
        /// <summary>
        /// Create a ForwardReference.   At some point before the end of the serialization, DefineForwardReference must be called on this value 
        /// </summary>
        /// <returns></returns>
        public ForwardReference GetForwardReference()
        {
            if (forwardReferenceDefinitions == null)
            {
                forwardReferenceDefinitions = new List<StreamLabel>();
            }

            ForwardReference ret = (ForwardReference)forwardReferenceDefinitions.Count;
            forwardReferenceDefinitions.Add(StreamLabel.Invalid);
            return ret;
        }
        /// <summary>
        /// Define the ForwardReference forwardReference to point at the current write location.  
        /// </summary>
        /// <param name="forwardReference"></param>
        public void DefineForwardReference(ForwardReference forwardReference)
        {
            forwardReferenceDefinitions[(int)forwardReference] = writer.GetLabel();
        }

        // data added after V1 needs to be tagged so that V1 deserializers can skip it. 

        /// <summary>
        /// Write a byte preceded by a tag that indicates its a byte.  These should be read with the corresponding TryReadTagged operation
        /// </summary>
        public void WriteTagged(bool value) { WriteTag(Tags.Byte); Write(value ? (byte)1 : (byte)0); }
        /// <summary>
        /// Write a byte preceded by a tag that indicates its a byte.  These should be read with the corresponding TryReadTagged operation
        /// </summary>
        public void WriteTagged(byte value) { WriteTag(Tags.Byte); Write(value); }
        /// <summary>
        /// Write a byte preceded by a tag that indicates its a short.  These should be read with the corresponding TryReadTagged operation
        /// </summary>
        public void WriteTagged(short value) { WriteTag(Tags.Int16); Write(value); }
        /// <summary>
        /// Write a byte preceded by a tag that indicates its a int.  These should be read with the corresponding TryReadTagged operation
        /// </summary>
        public void WriteTagged(int value) { WriteTag(Tags.Int32); Write(value); }
        /// <summary>
        /// Write a byte preceded by a tag that indicates its a long.  These should be read with the corresponding TryReadTagged operation
        /// </summary>
        public void WriteTagged(long value) { WriteTag(Tags.Int64); Write(value); }
        /// <summary>
        /// Write a byte preceded by a tag that indicates its a string.  These should be read with the corresponding TryReadTagged operation
        /// </summary>
        public void WriteTagged(string value) { WriteTag(Tags.String); Write(value); }
        /// <summary>
        /// Write a byte preceded by a tag that indicates its a object.  These should be read with the corresponding TryReadTagged operation
        /// </summary>
        public void WriteTagged(IFastSerializable value)
        {
            WriteTag(Tags.SkipRegion);
            ForwardReference endRegion = GetForwardReference();
            Write(endRegion);        // Allow the reader to skip this. 
            Write(value);            // Write the data we can skip
            DefineForwardReference(endRegion);  // This is where the forward reference refers to 
        }

        /// <summary>
        /// Writes the header for a skipping an arbitrary blob.   THus it writes a Blob
        /// tag and the size, and the caller must then write 'sizes' bytes of data in 
        /// some way.   This allows you to create regions of arbitrary size that can
        /// be skipped by old as well as new parsers.  
        /// </summary>
        /// <param name="size"></param>
        public void WriteTaggedBlobHeader(int size)
        {
            WriteTag(Tags.Blob);
            Write(size);
        }

        /// <summary>
        /// Writes an end tag (which is different from all others).   This is useful 
        /// when you have a deferred region of tagged items.  
        /// </summary>
        public void WriteTaggedEnd() { WriteTag(Tags.EndObject); }

        /// <summary>
        /// Retrieve the underlying stream we are writing to.  Generally the Write* methods are enough. 
        /// </summary>
        public IStreamWriter Writer { get { return writer; } }
        /// <summary>
        /// Completes the writing of the stream. 
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        #region private
        private StreamWriter log;

        /// <summary>
        /// To help debug any serialization issues, you can write data to a side file called 'log.serialize.xml'
        /// which can track exactly what serialization operations occurred.  
        /// </summary>
        [Conditional("DEBUG_SERIALIZE")]
        public void Log(string str)
        {
            if (log == null)
            {
                log = File.CreateText("log.serialize.xml");
            }

            log.WriteLine(str);
        }

        private void WriteTag(Tags tag)
        {
            Log("<WriteTag Type=\"" + tag + "\" Value=\"" + ((int)tag).ToString() + "\" StreamLabel=\"0x" + writer.GetLabel().ToString("x") + "\"/>");
            writer.Write((byte)tag);
        }
        private void WriteObjectRef(IFastSerializable obj, bool defered)
        {

            if (obj == null)
            {
                Log("<WriteNullReference>");
                WriteTag(Tags.NullReference);
                Log("</WriteNullReference>");
                return;
            }

            StreamLabel reference;
            if (ObjectsInGraph.TryGetValue(obj, out reference))
            {
                Log("<WriteReference streamLabelRef=\"0x" + reference.ToString("x") +
                    "\" objRef=\"0x" + obj.GetHashCode().ToString("x") + "\">");
                WriteTag(Tags.ObjectReference);
                Write(reference);
                Log("</WriteReference>");
                return;
            }

            // If we have a forward forwardReference to this, get it. 
            ForwardReference forwardReference;
            if (defered)
            {
                if (ObjectsWithForwardReferences == null)
                {
                    ObjectsWithForwardReferences = new Dictionary<IFastSerializable, ForwardReference>();
                }

                if (!ObjectsWithForwardReferences.TryGetValue(obj, out forwardReference))
                {
                    forwardReference = GetForwardReference();
                    ObjectsWithForwardReferences.Add(obj, forwardReference);
                }
                Log("<WriteForwardReference indexRef=\"0x" + ((int)forwardReference).ToString("x") +
                    "\" objRef=\"0x" + obj.GetHashCode().ToString("x") +
                    "\" type=\"" + obj.GetType().Name + "\">");
                WriteTag(Tags.ForwardReference);

                // Write the forward forwardReference index
                Write((int)forwardReference);
                // And its type. 
                WriteTypeForObject(obj);
                Log("</WriteForwardReference>");
                return;
            }

            // At this point we are writing an actual object and not a reference. 
            // 
            StreamLabel objLabel = writer.GetLabel();
            Log("<WriteObject obj=\"0x" + obj.GetHashCode().ToString("x") +
                "\" StreamLabel=\"0x" + objLabel.ToString("x") +
                "\" type=\"" + obj.GetType().Name + "\">");
            // Have we just defined an object that has a forward forwardReference to it?
            if (ObjectsWithForwardReferences != null &&
                ObjectsWithForwardReferences.TryGetValue(obj, out forwardReference))
            {
                Log("<WriteForwardReferenceDefinition index=\"0x" + ((int)forwardReference).ToString("x") + "\">");
                // OK, tag the definition with the forward forwardReference index
                WriteTag(Tags.ForwardDefinition);
                Write((int)forwardReference);

                // And also put it in the ForwardReferenceTable.  
                forwardReferenceDefinitions[(int)forwardReference] = objLabel;
                // And we can remove it from the ObjectsWithForwardReferences table
                ObjectsWithForwardReferences.Remove(obj);
                Log("</WriteForwardReferenceDefinition>");
            }

            // Add to object graph before calling ToStream (for recursive objects)
            ObjectsInGraph.Add(obj, objLabel);
            WriteObjectData(obj, Tags.BeginObject);
            Log("</WriteObject>");
        }
        private void WriteTypeForObject(IFastSerializable obj)
        {
            // Write the type of the forward forwardReference. 
            RuntimeTypeHandle handle = obj.GetType().TypeHandle;
            SerializationType type;
            if (!TypesInGraph.TryGetValue(handle, out type))
            {
                type = CreateTypeForObject(obj);
                TypesInGraph.Add(handle, type);
            }
            Log("<WriteTypeForObject TypeName=\"" + type + "\">");
            WriteObjectRef(type, false);
            Log("</WriteTypeForObject>");

        }
        private void WriteObjectData(IFastSerializable obj, Tags beginTag)
        {
            Debug.Assert(beginTag == Tags.BeginObject || beginTag == Tags.BeginPrivateObject);
            WriteTag(beginTag);
            WriteTypeForObject(obj);
            obj.ToStream(this);

            WriteTag(Tags.EndObject);
        }
        private void WriteDeferedObjects()
        {
            if (ObjectsWithForwardReferences == null)
            {
                return;
            }

            Log("<WriteDeferedObjects>");
            List<IFastSerializable> objs = new List<IFastSerializable>();
            while (ObjectsWithForwardReferences.Count > 0)
            {
                // Copy the objects out because the calls to WriteObjectReference updates the collection.  
                objs.AddRange(ObjectsWithForwardReferences.Keys);
                foreach (IFastSerializable obj in objs)
                {
                    Write(obj);
                    Debug.Assert(!ObjectsWithForwardReferences.ContainsKey(obj));
                }
                objs.Clear();
            }
            Log("</WriteDeferedObjects>");
        }
        private SerializationType CreateTypeForObject(IFastSerializable instance)
        {
            Type type = instance.GetType();

            // Special case: the SerializationType for SerializationType itself is null.  This avoids
            // recursion.  
            if (type == typeof(SerializationType))
            {
                return null;
            }

            SerializationType ret = new SerializationType(type);
            IFastSerializableVersion versionInstance = instance as IFastSerializableVersion;
            if (versionInstance != null)
            {
                ret.minimumReaderVersion = versionInstance.MinimumReaderVersion;
                ret.version = versionInstance.Version;
            }
            return ret;
        }
        /// <summary>
        /// Dispose pattern
        /// </summary>
        public void Dispose()
        {
            writer.Dispose();
            if (log != null)
            {
                log.Dispose();
                log = null;
            }
        }

        internal IStreamWriter writer;
        internal IDictionary<RuntimeTypeHandle, SerializationType> TypesInGraph;
        internal IDictionary<IFastSerializable, StreamLabel> ObjectsInGraph;
        internal IDictionary<IFastSerializable, ForwardReference> ObjectsWithForwardReferences;
        internal List<StreamLabel> forwardReferenceDefinitions;
        #endregion
    };

    /// <summary>
    /// Deserializer is a helper class that holds all the information needed to deserialize an object
    /// graph as a whole (things like the table of objects already deserialized, and the list of types in
    /// the object graph.  
    /// 
    /// see #SerializerIntroduction for more
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    sealed class Deserializer : IDisposable
    {
        /// <summary>
        /// Create a Deserializer that reads its data from a given file
        /// </summary>
        public Deserializer(string filePath) : this(new IOStreamStreamReader(filePath), filePath) { }

        /// <summary>
        /// Create a Deserializer that reads its data from a given System.IO.Stream.   The stream will be closed when the Deserializer is done with it.  
        /// </summary>
        public Deserializer(Stream inputStream, string streamName)
            : this(new IOStreamStreamReader(inputStream), streamName)
        {
        }

        /// <summary>
        /// Create a Deserializer that reads its data from a given System.IO.Stream. The
        /// <paramref name="leaveOpen"/> parameter determines whether the deserializer will close the stream when it
        /// closes.
        /// </summary>
        public Deserializer(Stream inputStream, string streamName, bool leaveOpen)
            : this(new IOStreamStreamReader(inputStream, leaveOpen: leaveOpen), streamName)
        {
        }

        /// <summary>
        /// Create a Deserializer that reads its data from a given IStreamReader.   The stream will be closed when the Deserializer is done with it.  
        /// </summary>
        public Deserializer(IStreamReader reader, string streamName)
        {
            ObjectsInGraph = new Dictionary<StreamLabel, IFastSerializable>();
            this.reader = reader;
            allowLazyDeserialization = true;
            Name = streamName;
            factories = new Dictionary<string, Func<IFastSerializable>>();
            RegisterFactory(typeof(SerializationType), delegate { return new SerializationType(); });

            Log("<Deserialize>");
            // We don't do this with ReadString() because it is a likely point of failure (a completely wrong file)
            // and we want to not read garbage if we don't have to
            var expectedSig = "!FastSerialization.1";
            int sigLen = reader.ReadInt32();
            if (sigLen != expectedSig.Length)
            {
                goto ThrowException;
            }

            for (int i = 0; i < sigLen; i++)
            {
                if (reader.ReadByte() != expectedSig[i])
                {
                    goto ThrowException;
                }
            }

            return;
            ThrowException:
            throw new SerializationException("Not a understood file format: " + streamName);
        }

#if false 
        /// <summary>
        /// On by default.  If off, then you read the whole file from beginning to the end and never have to
        /// seek.  This should only be used when you have this requirement (you are reading from an unseekable
        /// stream 
        /// 
        /// TODO remove? we have not tested this with AllowLazyDeserialzation==false. 
        /// </summary>
        public bool AllowLazyDeserialization
        {
            get { return allowLazyDeserialization; }
            set { allowLazyDeserialization = value; }
        }
#endif 

        /// <summary>
        /// Returns the full name of the type of the entry object without actually creating it.
        /// Will return null on failure.  
        /// </summary>  
        public string GetEntryTypeName()
        {
            StreamLabel origPosition = reader.Current;
            SerializationType objType = null;
            try
            {
                Tags tag = ReadTag();
                if (tag == Tags.BeginObject)
                {
                    objType = (SerializationType)ReadObject();
                }
            }
            catch (Exception) { }
            reader.Goto(origPosition);
            if (objType == null)
            {
                return null;
            }

            return objType.FullName;
        }
        /// <summary>
        /// GetEntryObject is the main deserialization entry point.  The serialization stream always has an object that represents the stream as
        /// a whole, called the entry object and this returns it and places it in 'ret'
        /// </summary>
        public void GetEntryObject<T>(out T ret)
        {
            ret = (T)GetEntryObject();
        }
        /// <summary>
        /// GetEntryObject is the main deserialization entry point.  The serialization stream always has an object that represents the stream as
        /// a whole, called the entry object and this returns it and returns it
        /// </summary>
        public IFastSerializable GetEntryObject()
        {
            if (entryObject == null)
            {
                // If you are going to deserialize the world, better to do it in order, which means deferring
                // forward references (since you will get to them eventually).  
                if (!allowLazyDeserialization)
                {
                    deferForwardReferences = true;
                }

                Log("<GetEntryObject deferForwardReferences=\"" + deferForwardReferences + "\">");
                entryObject = ReadObjectDefintion();

                // If we are reading sequentially, read the position of the objects (will be marked by a
                // unmatched EndObject tag. 
                if (!allowLazyDeserialization)
                {
                    for (; ; )
                    {
                        StreamLabel objectLabel = reader.Current;
                        Tags tag = ReadTag();
                        if (tag == Tags.EndObject)
                        {
                            break;
                        }

                        ReadObjectDefinition(tag, objectLabel);
                    }
                }
                Debug.Assert(unInitializedForwardReferences == null || unInitializedForwardReferences.Count == 0);
                Log("</GetEntryObject>");
            }
            return entryObject;
        }

        // For FromStream method bodies.  
        public void Read(byte[] buffer, int offset, int length)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            reader.Read(buffer, offset, length);
#if DEBUG
            Log("<Read Value=\"" + "[...]" + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }

        /// <summary>
        /// Read a bool from the stream
        /// </summary>
        public void Read(out bool ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ret = reader.ReadByte() != 0;
#if DEBUG
            Log("<ReadByte Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }
        /// <summary>
        /// Read a byte from the stream
        /// </summary>
        public void Read(out byte ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ret = reader.ReadByte();
#if DEBUG
            Log("<ReadByte Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }
        /// <summary>
        /// Read a short from the stream
        /// </summary>
        public void Read(out short ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ret = reader.ReadInt16();
#if DEBUG
            Log("<ReadInt16 Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }
        /// <summary>
        /// Read an int from the stream
        /// </summary>
        public void Read(out int ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ret = reader.ReadInt32();
#if DEBUG
            Log("<ReadInt32 Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }
        /// <summary>
        /// Read a long from the stream
        /// </summary>
        public void Read(out long ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ret = reader.ReadInt64();
#if DEBUG
            Log("<ReadInt64 Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }
        /// <summary>
        /// Read a Guid from the stream
        /// </summary>
        public unsafe void Read(out Guid ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            byte* bytes = stackalloc byte[16];
            for (int i = 0; i < 16; i++)
            {
                bytes[i] = reader.ReadByte();
            }

            ret = *((Guid*)bytes);
#if DEBUG
            Log("<ReadGuid Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }
        /// <summary>
        /// Read a float from the stream
        /// </summary>
        public void Read(out float ret)
        {
            ret = ReadFloat();
        }
        /// <summary>
        /// Read a double from the stream
        /// </summary>
        public void Read(out double ret)
        {
            ret = ReadDouble();
        }
        /// <summary>
        /// Read a string from the stream.  Can represent null
        /// </summary>
        public void Read(out string ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ret = reader.ReadString();
#if DEBUG
            if (ret == null)
                Log("<ReadString StreamLabel=\"0x" + label.ToString("x") + "\"/>");
            else
                Log("<ReadString Value=" + ret + " StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }
        /// <summary>
        /// d) from the stream
        /// </summary>
        public void Read(out StreamLabel ret)
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ret = reader.ReadLabel();
#if DEBUG
            Log("<Read Type=\"StreamLabel\" Value=\"0x" + ret.ToString("x") + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
        }

        /// <summary>
        /// Read a IFastSerializable object from the stream and place it in ret
        /// </summary>
        public void Read<T>(out T ret) where T : IFastSerializable
        {
            ret = (T)ReadObject();
        }
        /// <summary>
        /// Read a IFastSerializable object from the stream and return it
        /// </summary>
        public IFastSerializable ReadObject()
        {
            Log("<ReadObjectReference StreamLabel=\"0x" + reader.Current.ToString("x") + "\">");

            StreamLabel objectLabel = reader.Current;
            Tags tag = ReadTag();
            IFastSerializable ret;
            if (tag == Tags.ObjectReference)
            {
                StreamLabel target = reader.ReadLabel();
                if (!ObjectsInGraph.TryGetValue(target, out ret))
                {
                    ret = ReadObject(target);
                }
            }
            else if (tag == Tags.NullReference)
            {
                ret = null;
            }
            else if (tag == Tags.ForwardReference)
            {
                Log("<ReadForwardRef>");
                ForwardReference forwardReference = ReadForwardReference();
                Log("<ReadForwardReferenceType>");
                SerializationType type = (SerializationType)ReadObject();
                Log("</ReadForwardReferenceType>");

                StreamLabel definition = ResolveForwardReference(forwardReference);
                if (definition != StreamLabel.Invalid)
                {
                    if (!ObjectsInGraph.TryGetValue(definition, out ret))
                    {
                        Log("<FoundDefinedForwardRef StreamLabelRef=\"0x" + definition.ToString("x") + "\">");
                        StreamLabel orig = reader.Current;
                        Goto(definition);
                        ret = ReadObjectDefintion();
                        Goto(orig);
                        Log("</FoundDefinedForwardRef>");
                    }
                }
                else
                {
                    if (unInitializedForwardReferences == null)
                    {
                        unInitializedForwardReferences = new Dictionary<ForwardReference, IFastSerializable>();
                    }

                    if (!unInitializedForwardReferences.TryGetValue(forwardReference, out ret))
                    {
                        ret = type.CreateInstance();
                        Log("<AddingUninitializedForwardRef indexRef=\"" + forwardReference + "\" objRef=\"0x" + ret.GetHashCode().ToString("x") + "\"/>");
                        unInitializedForwardReferences.Add(forwardReference, ret);
                    }
                    else
                    {
                        Log("<FoundExistingForwardRef indexRef=\"" + forwardReference + "\" objRef=\"0x" + ret.GetHashCode().ToString("x") + "\"/>");
                    }
                }
                Log("</ReadForwardRef>");
            }
            else if (tag == Tags.Blob)
            {
                // If it is a blob skip it and try again (presumably other things point at it.  
                int size = reader.ReadInt32();
                reader.Skip(size);
                return ReadObject();
            }
            else
            {
                ret = ReadObjectDefinition(tag, objectLabel);
            }
            Log("<Return objRef=\"0x" + (ret == null ? "0" : ret.GetHashCode().ToString("x")) + "\"" +
                (ret == null ? "" : " type=\"" + ret.GetType().Name + "\"") + "/>");
            Log("</ReadObjectReference>");
            return ret;
        }
        /// <summary>
        /// Read a bool from the stream and return it
        /// </summary>
        public bool ReadBool()
        {
            return ReadByte() != 0;
        }
        /// <summary>
        /// Read a byte from the stream and return it
        /// </summary>
        public byte ReadByte()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            byte ret = reader.ReadByte();
#if DEBUG
            Log("<ReadByte Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            return ret;
        }
        /// <summary>
        /// Read a short from the stream and return it
        /// </summary>
        public short ReadInt16()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            short ret = reader.ReadInt16();
#if DEBUG
            Log("<ReadInt16 Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            return ret;
        }
        /// <summary>
        /// Read an int from the stream and return it
        /// </summary>
        public int ReadInt()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            int ret = reader.ReadInt32();
#if DEBUG
            Log("<ReadInt32 Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            return ret;
        }
        /// <summary>
        /// Read a long from the stream and return it
        /// </summary>
        public long ReadInt64()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            long ret = reader.ReadInt64();
#if DEBUG
            Log("<ReadInt64 Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            return ret;
        }
        /// <summary>
        /// Read a float from the stream and return it
        /// </summary>
        public unsafe float ReadFloat()
        {
            float ret;
            int* intPtr = (int*)&ret;
            *intPtr = reader.ReadInt32();
            return ret;
        }
        /// <summary>
        /// Read a double from the stream and return it
        /// </summary>
        public unsafe double ReadDouble()
        {
            double ret;
            long* longPtr = (long*)&ret;
            *longPtr = reader.ReadInt64();
            return ret;
        }
        /// <summary>
        /// Read in a string value and return it
        /// </summary>
        public string ReadString()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            string ret = reader.ReadString();
#if DEBUG
            Log("<ReadString Value=\"" + ret.ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            return ret;
        }
        /// <summary>
        /// Read in a StreamLabel (a pointer to some other part of the stream) and return it
        /// </summary>
        public StreamLabel ReadLabel()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            StreamLabel ret = reader.ReadLabel();
#if DEBUG
            Log("<ReadLabel StreamLabelRef=\"0x" + ret.ToString("x") + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            return ret;
        }
        /// <summary>
        /// Read in a ForwardReference (a pointer to some other part of the stream which was not known at the tie it was written) and return it
        /// Use ResolveForwardReference to convert the ForwardReference to a StreamLabel
        /// </summary>
        public ForwardReference ReadForwardReference()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            ForwardReference ret = (ForwardReference)reader.ReadInt32();
#if DEBUG
            Log("<ReadForwardReference indexRef=\"" + ret + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            return ret;
        }

        // forward reference support
        /// <summary>
        /// Given a forward reference find the StreamLabel (location in the stream) that it points at).  
        /// Normally this call preserves the current read location, but if you do don't care you can 
        /// set preserveCurrent as an optimization to make it more efficient.  
        /// </summary>
        public StreamLabel ResolveForwardReference(ForwardReference reference, bool preserveCurrent = true)
        {
            StreamLabel ret = StreamLabel.Invalid;
            if (forwardReferenceDefinitions == null)
            {
                forwardReferenceDefinitions = new List<StreamLabel>();
            }

            if ((uint)reference < (uint)forwardReferenceDefinitions.Count)
            {
                ret = forwardReferenceDefinitions[(int)reference];
            }

            if (ret == StreamLabel.Invalid && !deferForwardReferences)
            {
                Log("<GetFowardReferenceTable>");
                StreamLabel orig = reader.Current;

                reader.GotoSuffixLabel();
                Log("<Trailer StreamLabel=\"0x" + reader.Current.ToString("x") + "\"/>");
                StreamLabel forwardRefsLabel = reader.ReadLabel();

                Goto(forwardRefsLabel);
                int fowardRefCount = reader.ReadInt32();
                Log("<ForwardReferenceDefinitons StreamLabel=\"0x" + forwardRefsLabel.ToString("x") +
                    "\" Count=\"" + fowardRefCount + "\">");
                for (int i = 0; i < fowardRefCount; i++)
                {
                    StreamLabel defintionLabel = reader.ReadLabel();
                    if (i >= forwardReferenceDefinitions.Count)
                    {
                        forwardReferenceDefinitions.Add(defintionLabel);
                    }
                    else
                    {
                        Debug.Assert(
                            forwardReferenceDefinitions[i] == StreamLabel.Invalid ||
                            forwardReferenceDefinitions[i] == defintionLabel);
                        forwardReferenceDefinitions[i] = defintionLabel;
                    }
                    Log("<ForwardReference index=\"" + i + "\"  StreamLabelRef=\"0x" + defintionLabel.ToString("x") + "\"/>");
                }
                Log("</ForwardReferenceDefinitons>");
                if (preserveCurrent)
                {
                    Goto(orig);
                }

                ret = forwardReferenceDefinitions[(int)reference];
                Log("</GetFowardReferenceTable>");
            }

            Log("<GetForwardReference indexRef=\"" + reference +
                "\" StreamLabelRef=\"0x" + ret.ToString("x") +
                "\" deferForwardReferences=\"" + deferForwardReferences + "\"/>");
            return ret;
        }

        /// <summary>
        /// Meant to be called from FromStream.  It returns the version number of the 
        /// type being deserialized.   It can be used so that new code can recognizes that it
        /// is reading an old file format and adjust what it reads.   
        /// </summary>
        public int VersionBeingRead { get { return typeBeingRead.Version; } }

        /// <summary>
        /// Meant to be called from FromStream.  It returns the version number of the MinimumReaderVersion
        /// of the type that was serialized.   
        /// </summary>
        public int MinimumReaderVersionBeingRead { get { return typeBeingRead.MinimumReaderVersion; } }

        /// <summary>
        /// The filename if read from a file or the stream name if read from a stream
        /// </summary>
        public String Name { get; private set; }

        /// <summary>
        /// If set this function is set, then it is called whenever a type name from the serialization
        /// data is encountered.  It is your you then need to look that up.  If it is not present 
        /// it uses Type.GetType(string) which only checks the current assembly and mscorlib. 
        /// </summary>
        public Func<string, Type> TypeResolver { get; set; }

        /// <summary>
        /// For every IFastSerializable object being deserialized, the Deserializer needs to create 'empty' objects 
        /// that 'FromStream' is invoked on.  The Deserializer gets these 'empty' objects by calling a 'factory'
        /// delegate for that type.   Thus all types being deserialized must have a factory.   
        /// 
        /// RegisterFactory registers such a factory for particular 'type'.  
        /// </summary>
        public void RegisterFactory(Type type, Func<IFastSerializable> factory)
        {
            factories[type.FullName] = factory;
        }
        public void RegisterFactory(string typeName, Func<IFastSerializable> factory)
        {
            factories[typeName] = factory;
        }

        /// <summary>
        /// For every IFastSerializable object being deserialized, the Deserializer needs to create 'empty' objects 
        /// that 'FromStream' is invoked on.  The Deserializer gets these 'empty' objects by calling a 'factory'
        /// delegate for that type.   Thus all types being deserialized must have a factory.   
        /// 
        /// RegisterDefaultFactory registers a factory that is passed a type parameter and returns a new IFastSerialable object. 
        /// </summary>
        public void RegisterDefaultFactory(Func<Type, IFastSerializable> defaultFactory)
        {
            this.defaultFactory = defaultFactory;
        }

        // For FromStream method bodies, reading tagged values (for post V1 field additions)
        // If the item is present, it is read into 'ret' otherwise 'ret' is left unchanged.   
        // If after V1 you always add fields at the end, and you always use WriteTagged() and TryReadTagged()
        // to write and read them, then you always get perfect backward and forward compatibility! 
        // For things like collections you can do
        //
        // in collectionLen = 0;
        // TryReadTagged(ref collectionLen)
        // this.array = new int[collectionLen]; // initialize the array
        // for(int i =0; i < collectionLenght; i++)
        //      Read(out this.array[i]);
        //
        // notice that the reading the array elements is does not use TryReadTagged, but IS 
        // conditional on the collection length (which is tagged).   

        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged bool, return int in ret and return true, otherwise leave the cursor unchanged and return false
        /// </summary>
        public bool TryReadTagged(ref bool ret)
        {
            Tags tag = ReadTag();
            if (tag == Tags.Byte)
            {
                byte data = 0;
                Read(out data);
                ret = (data != 0);
                return true;
            }
            reader.Goto(Current - 1);
            return false;
        }
        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged byte, return int in ret and return true, otherwise leave the cursor unchanged and return false
        /// </summary>
        public bool TryReadTagged(ref byte ret)
        {
            Tags tag = ReadTag();
            if (tag == Tags.Byte)
            {
                Read(out ret);
                return true;
            }
            reader.Goto(Current - 1);
            return false;
        }
        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged short, return int in ret and return true, otherwise leave the cursor unchanged and return false
        /// </summary>
        public bool TryReadTagged(ref short ret)
        {
            Tags tag = ReadTag();
            if (tag == Tags.Int16)
            {
                Read(out ret);
                return true;
            }
            reader.Goto(Current - 1);
            return false;
        }
        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged int, return int in ret and return true, otherwise leave the cursor unchanged and return false
        /// </summary>
        public bool TryReadTagged(ref int ret)
        {
            Tags tag = ReadTag();
            if (tag == Tags.Int32)
            {
                Read(out ret);
                return true;
            }
            reader.Goto(Current - 1);
            return false;
        }
        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged long, return int in ret and return true, otherwise leave the cursor unchanged and return false
        /// </summary>
        public bool TryReadTagged(ref long ret)
        {
            Tags tag = ReadTag();
            if (tag == Tags.Int64)
            {
                Read(out ret);
                return true;
            }
            reader.Goto(Current - 1);
            return false;
        }
        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged string, return int in ret and return true, otherwise leave the cursor unchanged and return false
        /// </summary>
        public bool TryReadTagged(ref string ret)
        {
            Tags tag = ReadTag();
            if (tag == Tags.String)
            {
                Read(out ret);
                return true;
            }
            reader.Goto(Current - 1);
            return false;
        }
        /// <summary>
        /// Try to read the header for a tagged blob of bytes.  If Current points at a tagged
        /// blob it succeeds and returns the size of the blob (the caller must read or skip 
        /// past it manually) If it is not a tagged blob it returns a size of 0 and resets
        /// the read pointer to what it was before this method was called.  
        /// </summary>
        public int TryReadTaggedBlobHeader()
        {
            Tags tag = ReadTag();
            if (tag == Tags.Blob)
            {
                return reader.ReadInt32();
            }

            reader.Goto(Current - 1);
            return 0;
        }
        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged FastSerializable, return int in ret and return true, otherwise leave the cursor unchanged and return false
        /// </summary>
        public bool TryReadTagged<T>(ref T ret) where T : IFastSerializable
        {
            // Tagged objects always start with a SkipRegion so we don't need to know its size.  
            Tags tag = ReadTag();
            if (tag == Tags.SkipRegion)
            {
                ReadForwardReference();     // Skip the forward reference which is part of SkipRegion 
                ret = (T)ReadObject();      // Read the real object 
                return true;
            }
            reader.Goto(Current - 1);
            return false;
        }
        /// <summary>
        /// Try to read tagged value from the stream.  If it is a tagged FastSerializable, return it, otherwise leave the cursor unchanged and return null
        /// </summary>
        public IFastSerializable TryReadTaggedObject()
        {
            // Tagged objects always start with a SkipRegion so we don't need to know its size.  
            Tags tag = ReadTag();
            if (tag == Tags.SkipRegion)
            {
                ReadForwardReference();     // Skip the forward reference which is part of SkipRegion 
                return ReadObject();        // Read the real object 
            }
            reader.Goto(Current - 1);
            return null;
        }

        /// <summary>
        /// Set the read position to the given StreamLabel
        /// </summary>
        public void Goto(StreamLabel label)
        {
            Log("<Goto StreamLabelRef=\"0x" + label.ToString("x") + "\"/>");
            reader.Goto(label);
        }
        /// <summary>
        /// Set the read position to the given ForwardReference
        /// </summary>
        public void Goto(ForwardReference reference)
        {
            Goto(ResolveForwardReference(reference, false));
        }
        /// <summary>
        /// Returns the current read position in the stream. 
        /// </summary>
        public StreamLabel Current { get { return reader.Current; } }
        /// <summary>
        /// Fetch the underlying IStreamReader that the deserializer reads data from 
        /// </summary>
        public IStreamReader Reader { get { return reader; } }
        /// <summary>
        /// Close the IStreamReader and free resources associated with the Deserializer
        /// </summary>
        public void Dispose()
        {
            if (reader != null)
            {
                reader.Dispose();
                reader = null;
            }
            ObjectsInGraph = null;
            forwardReferenceDefinitions = null;
            unInitializedForwardReferences = null;
            if (log != null)
            {
                Log("</Deserialize>");
                log.Dispose();
                log = null;
            }
        }

        #region private 
        private StreamWriter log;
        [Conditional("DEBUG_SERIALIZE")]
        // see also Serializer.Log 
        public void Log(string str)
        {
            if (log == null)
            {
                log = File.CreateText("log.deserialize.xml");
            }

            log.WriteLine(str);
            log.Flush();
        }

        internal IFastSerializable ReadObject(StreamLabel label)
        {
            StreamLabel orig = reader.Current;
            Goto(label);
            IFastSerializable ret = ReadObject();
            Goto(orig);
            return ret;
        }

        private IFastSerializable ReadObjectDefintion()
        {
            StreamLabel objectLabel = reader.Current;
            Tags tag = ReadTag();
            return ReadObjectDefinition(tag, objectLabel);
        }
        private IFastSerializable ReadObjectDefinition(Tags tag, StreamLabel objectLabel)
        {
            Log("<ReadObjectDefinition StreamLabel=\"0x" + reader.Current.ToString("x") + "\" Tag=\"" + tag + "\">");

            IFastSerializable ret;
            IFastSerializable existingObj = null; ;
            SerializationType type;
            if (tag == Tags.BeginPrivateObject)
            {
                type = (SerializationType)ReadObject();
                // Special case, a null object means 'typeof serializationType'
                if (type == null)
                {
                    type = new SerializationType(typeof(SerializationType).FullName, this);
                }
                ret = type.CreateInstance();
            }
            else
            {
                ForwardReference forwardReference = ForwardReference.Invalid;
                if (tag == Tags.ForwardDefinition)
                {
                    forwardReference = (ForwardReference)reader.ReadInt32();
                    Log("<ForwardDefintion index=\"" + forwardReference + "\"/>");
                    tag = ReadTag();
                }
                if (tag != Tags.BeginObject)
                {
                    throw new SerializationException("Bad serialization tag found when starting object");
                }

                Log("<ReadType>");
                type = (SerializationType)ReadObject();

                // Special case, a null object forwardReference means 'typeof serializationType'
                if (type == null)
                {
                    type = new SerializationType(typeof(SerializationType).FullName, this);
                }

                Log("</ReadType>");

                // Create the instance (or get it from the unInitializedForwardReferences if it was created
                // that way).  
                if (forwardReference != ForwardReference.Invalid)
                {
                    DefineForwardReference(forwardReference, objectLabel);
                    if (unInitializedForwardReferences != null && unInitializedForwardReferences.TryGetValue(forwardReference, out ret))
                    {
                        Log("<RemovingUninitializedForwardRef indexRef=\"" + forwardReference + "\" objRef=\"0x" + ret.GetHashCode().ToString("x") + "\"/>");
                        unInitializedForwardReferences.Remove(forwardReference);
                    }
                    else
                    {
                        ret = type.CreateInstance();
                    }
                }
                else
                {
                    ret = type.CreateInstance();
                }

                Log("<AddingObjectToGraph StreamLabelRef=\"0x" + objectLabel.ToString("x") +
                    "\" obj=\"0x" + ret.GetHashCode().ToString("x") +
                    "\" type=\"" + ret.GetType().Name +
                    "\"/>");

                if (!ObjectsInGraph.TryGetValue(objectLabel, out existingObj))
                {
                    ObjectsInGraph.Add(objectLabel, ret);
                }
            }

            // Actually initialize the object's fields.
            var saveTypeBeingRead = typeBeingRead;
            typeBeingRead = type;
            ret.FromStream(this);
            typeBeingRead = saveTypeBeingRead;
            FindEndTag(type, ret);

            // TODO in the case where the object already exist, we just created an object just to throw it
            // away just (so we can skip the fields). figure out a better way.
            if (existingObj != null)
            {
                Log("<UseExistingObject/>");
                ret = existingObj;
            }

            Log("<Return obj=\"0x" + (ret == null ? "0" : ret.GetHashCode().ToString("x")) + "\"" +
                (ret == null ? "" : " type=\"" + ret.GetType().Name + "\"") + "/>");
            Log("</ReadObjectDefinition>");
            return ret;
        }

        internal Func<IFastSerializable> GetFactory(string fullName)
        {
            Func<IFastSerializable> ret;
            if (factories.TryGetValue(fullName, out ret))
            {
                return ret;
            }

            Type type;
            if (TypeResolver != null)
            {
                type = TypeResolver(fullName);
            }
            else
            {
                type = Type.GetType(fullName);
            }

            if (type == null)
            {
                throw new TypeLoadException("Could not find type " + fullName);
            }

            return delegate
            {
                // If we have a default factory, use it.  
                if (defaultFactory != null)
                {
                    IFastSerializable instance = defaultFactory(type);
                    if (instance != null)
                    {
                        return instance;
                    }
                }
                // Factory of last resort.  
                try
                {
                    return (IFastSerializable)Activator.CreateInstance(type);
                }
                catch (MissingMethodException)
                {
                    throw new SerializationException("Failure deserializing " + type.FullName +
                        ".\r\nIt must either have a parameterless constructor or been registered with the serializer.");
                }
            };
        }

        private void FindEndTag(SerializationType type, IFastSerializable objectBeingDeserialized)
        {
            // Skip any extra fields in the object that I don't understand. 
            Log("<EndTagSearch>");
            int i = 0;
            for (; ; )
            {
                Debug.Assert(i == 0 || type.Version != 0);
                StreamLabel objectLabel = reader.Current;
                // If this fails, the likely culprit is the FromStream of the objectBeingDeserialized. 
                Tags tag = ReadTag();

                // TODO this is a hack.   The .NET Core Runtime < V2.1 do not emit an EndObject tag
                // properly for its V1 EventPipeFile object.   The next object is always data that happens
                // to be size that is likley to be in the range 0x50-0xFF.  Thus we can fix this
                // and implicilty insert the EndObject and fix things up.   This is acceptable because
                // it really does not change non-error behavior.   
                // V2.1 of NET Core will ship in 4/2018 so a year or so after that is is probably OK
                // to remove this hack (basically dropping support for V1 of EventPipeFile)
                if (type.FullName == "Microsoft.DotNet.Runtime.EventPipeFile" && type.Version <= 2 && (int)tag > 0x50)
                {
                    reader.Skip(-1);        // Undo the read of the byte 
                    tag = Tags.EndObject;   // And make believe we saw the EntObject instead.  
                }

                int nesting = 0;
                switch (tag)
                {
                    case Tags.Byte:
                        reader.ReadByte();
                        break;
                    case Tags.Int16:
                        reader.ReadInt16();
                        break;
                    case Tags.Int32:
                        reader.ReadInt32();
                        break;
                    case Tags.Int64:
                        reader.ReadInt64();
                        break;
                    case Tags.String:
                        reader.ReadString();
                        break;
                    case Tags.NullReference:
                        break;
                    case Tags.BeginObject:
                    case Tags.BeginPrivateObject:
                        nesting++;
                        break;
                    case Tags.ForwardDefinition:
                    case Tags.ForwardReference:
                        ReadForwardReference();
                        break;
                    case Tags.ObjectReference:
                        reader.ReadLabel();
                        break;
                    case Tags.SkipRegion:
                        // Allow the region to be skipped.  
                        ForwardReference endSkipRef = ReadForwardReference();
                        StreamLabel endSkip = ResolveForwardReference(endSkipRef);
                        reader.Goto(endSkip);
                        break;
                    case Tags.Blob:
                        var size = reader.ReadInt32();
                        reader.Skip(size);
                        break;
                    case Tags.EndObject:
                        --nesting;
                        if (nesting < 0)
                        {
                            goto done;
                        }

                        break;
                    default:
                        throw new SerializationException("Could not find object end tag for object of type " + objectBeingDeserialized.GetType().Name + " at stream offset 0x" + ((int)objectLabel).ToString("x"));
                }
                i++;
            }
            done:
            Log("</EndTagSearch>");
            // TODO would like some redundancy, so that failure happen close to the cause.  
        }

        private void DefineForwardReference(ForwardReference forwardReference, StreamLabel definitionLabel)
        {
            Log("<DefineForwardReference indexRef=\"" + forwardReference + "\" StreamLableRef=\"0x" + definitionLabel.ToString("x") + "\"/>");

            if (forwardReferenceDefinitions == null)
            {
                forwardReferenceDefinitions = new List<StreamLabel>();
            }

            int idx = (int)forwardReference;
            while (forwardReferenceDefinitions.Count <= idx)
            {
                forwardReferenceDefinitions.Add(StreamLabel.Invalid);
            }

            // If it is already defined, it better match! 
            Debug.Assert(forwardReferenceDefinitions[idx] == StreamLabel.Invalid ||
                forwardReferenceDefinitions[idx] == definitionLabel);

            // Define the forward forwardReference
            forwardReferenceDefinitions[idx] = definitionLabel;
        }

        private Tags ReadTag()
        {
#if DEBUG
            StreamLabel label = reader.Current;
#endif
            Tags tag = (Tags)reader.ReadByte();
#if DEBUG
            Log("<ReadTag Type=\"" + tag + "\" Value=\"" + ((int)tag).ToString() + "\" StreamLabel=\"0x" + label.ToString("x") + "\"/>");
#endif
            // The tag > 0x50 is a work around see comment in FindEndTag
            Debug.Assert(Tags.Error < tag && tag < Tags.Limit || (int)tag > 0x50);
            return tag;
        }

        private SerializationType typeBeingRead;
        internal IStreamReader reader;
        internal IFastSerializable entryObject;
        internal IDictionary<StreamLabel, IFastSerializable> ObjectsInGraph;
        internal IDictionary<ForwardReference, IFastSerializable> unInitializedForwardReferences;
        internal List<StreamLabel> forwardReferenceDefinitions;
        internal bool allowLazyDeserialization;
        /// <summary>
        /// When we encounter a forward reference, we can either go to the forward reference table immediately and resolve it 
        /// (deferForwardReferences == false), or simply remember that that position needs to be fixed up and continue with
        /// the deserialization.   This later approach allows 'no seek' deserialization.   This variable which scheme we do. 
        /// </summary>
        internal bool deferForwardReferences;
        private Dictionary<string, Func<IFastSerializable>> factories;
        private Func<Type, IFastSerializable> defaultFactory;
        #endregion
    };


    /// <summary>
    /// #DeferedRegionOverview. 
    /// 
    /// A DeferedRegion help make 'lazy' objects. You will have a DeferedRegion for each block of object you
    /// wish to independently decide whether to deserialize lazily (typically you have one per object however
    /// in the limit you can have one per field, it is up to you).
    /// 
    /// When you call DeferedRegion.Write you give it a delegate that will write all the deferred fields.
    /// The Write operation will place a forward reference in the stream that skips all the fields written,
    /// then the fields themselves, then define the forward reference. This allows readers to skip the
    /// deferred fields.
    /// 
    /// When you call DeferedRegion.Read  you also give it a delegate that reads all the deferred fields.
    /// However when 'Read' instead of reading the fields it
    /// 
    ///     * remembers the deserializer, stream position, and reading delegate.
    ///     * it uses the forward reference to skip the region.
    ///     
    /// When DeferedRegion.FinishRead is called, it first checks if the region was already restored. 
    /// If not it used the information to read in the deferred region and returns.  Thus this FinishRead
    /// should be called before any deferred field is used.  
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    struct DeferedRegion
    {
        /// <summary>
        /// see #DeferedRegionOverview.  
        /// TODO more 
        /// </summary>
        public void Write(Serializer serializer, Action toStream)
        {
            serializer.Log("<DeferedRegion>\r\n");
            // We actually don't use the this pointer!  We did this for symmetry with Read
            ForwardReference endRegion = serializer.GetForwardReference();
            serializer.Write(endRegion);        // Allow the reader to skip this. 
            toStream();                         // Write the deferred data. 
            serializer.DefineForwardReference(endRegion);
            serializer.Log("</DeferedRegion>\r\n");
        }
        /// <summary>
        /// See overview in DeferedRegion class comment.  
        /// This call indicates that the 'fromStream' delegate  can deserialize a region of the object, which
        /// was serialized with the DeferedRegion.Write method.   The read skips the data for the region (thus
        /// no objects associated with the region are created in memory) but the deferred object remembers 
        /// 'fromStream' and will call it when 'FinishRead()' is called. 
        /// </summary>
        public void Read(Deserializer deserializer, Action fromStream)
        {
            Debug.Assert(this.fromStream == null);      // For now, don't call this more than once. 
            deserializer.Log("<DeferRegionRead StreamLabel=\"0x" + deserializer.Current.ToString("x") + "\">");
            ForwardReference endReference = deserializer.ReadForwardReference();
            this.deserializer = deserializer;
            startPosition = deserializer.Current;
            this.fromStream = fromStream;
            deserializer.Goto(endReference);
            deserializer.Log("</DeferRegionRead>");
        }
        /// <summary>
        /// FinishRead indicates that you need to deserialize the lazy region you defined with the 'Read' method.
        /// If the region has already been deserialized, nothing is done.   Otherwise when you call this
        /// method the current position in the stream is put back to where it was when Read was called and the
        /// 'fromStream' delegate registered in 'Read' is called to perform the deserialization.    
        /// </summary>
        public void FinishRead(bool preserveStreamPosition = false)
        {
            if (fromStream != null)
            {
                FinishReadHelper(preserveStreamPosition);
            }
        }
        /// <summary>
        /// Returns true if the FinsihRead() has already been called. 
        /// </summary>
        public bool IsFinished { get { return fromStream == null; } }

        /// <summary>
        /// Get the deserializer associated with this DeferredRegion
        /// </summary>
        public Deserializer Deserializer { get { return deserializer; } }
        /// <summary>
        /// Get the stream position when Read was called
        /// </summary>
        public StreamLabel StartPosition { get { return startPosition; } }
        #region private
        /// <summary>
        /// This helper is just here to insure that FinishRead gets inlined 
        /// </summary>
        private void FinishReadHelper(bool preserveStreamPosition)
        {
            StreamLabel originalPosition = 0;       // keeps the compiler happy. 
            if (preserveStreamPosition)
            {
                originalPosition = deserializer.Current;
            }

            deserializer.Log("<DeferRegionFinish StreamLabelRef=\"0x" + startPosition.ToString("x") + "\">");
            deserializer.Goto(startPosition);
            fromStream();
            deserializer.Log("</DeferRegionFinish>");
            fromStream = null;      // Indicates we ran it. 

            if (preserveStreamPosition)
            {
                deserializer.Goto(originalPosition);
            }
        }

        internal Deserializer deserializer;
        internal StreamLabel startPosition;
        internal Action fromStream;
        #endregion
    }

    /// <summary>
    /// A type can opt into being serializable by implementing IFastSerializable and a default constructor
    /// (constructor that takes not arguments).
    /// 
    /// Conceptually all clients of IFastSerializable also implement IFastSerializableVersion
    /// however the serializer will assume a default implementation of IFastSerializableVersion (that
    /// Returns version 1 and assumes all versions are allowed to deserialize it.  
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    interface IFastSerializable
    {
        /// <summary>
        /// Given a Serializer, write yourself to the output stream. Conceptually this routine is NOT
        /// responsible for serializing its type information but only its field values. However it is
        /// conceptually responsible for the full transitive closure of its fields.
        /// 
        /// * For primitive fields, the choice is easy, simply call Serializer.Write
        /// * For object fields there is a choice
        ///     * If is is only references by the enclosing object (eg and therefore field's lifetime is
        ///         identical to referencing object), then the Serialize.WritePrivateObject can be
        ///         used.  This skips placing the object in the interning table (that insures it is written
        ///         exactly once).  
        ///     * Otherwise call Serialize.WriteObject
        /// * For value type fields (or collections of structs), you serialize the component fields.  
        /// * For collections, typically you serialize an integer inclusiveCountRet followed by each object. 
        /// </summary>
        void ToStream(Serializer serializer);
        /// <summary>
        /// 
        /// Given a reader, and a 'this' instance, made by calling the default constructor, create a fully
        /// initialized instance of the object from the reader stream.  The deserializer provides the extra
        /// state needed to do this for cyclic object graphs.  
        /// 
        /// Note that it is legal for the instance to cache the deserializer and thus be 'lazy' about when
        /// the actual deserialization happens (thus large persisted strucuture on the disk might stay on the
        /// disk).  
        /// 
        /// Typically the FromStream implementation is an exact mirror of the ToStream implementation, where
        /// there is a Read() for every Write(). 
        /// </summary>
        void FromStream(Deserializer deserializer);
    }

    // TODO fix the versioning so you don't have to create an instance of the type on serialization. 
    /// <summary>
    /// Objects implement IFastSerializableVersion to indicate what the current version is for writing
    /// and which readers can read the current version.   If this interface is not implemented a default is
    /// provided (assuming version 1 for writing and MinimumVersion = 0).  
    /// 
    /// By default Serializer.WriteObject will place marks when the object ends and always skip to the
    /// end even if the FromStream did not read all the object data.   This allows considerable versioning
    /// flexibility.  Simply by placing the new data at the end of the existing serialization, new versions
    /// of the type can be read by OLD deserializers (new fields will have the value determined by the
    /// default constructor (typically 0 or null).  This makes is relatively easy to keep MinimumVersion = 0
    /// (the ideal case).  
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    interface IFastSerializableVersion
    {
        /// <summary>
        /// This is the version number for the serialization CODE (that is the app decoding the format)
        /// It should be incremented whenever a change is made to IFastSerializable.ToStream and the format 
        /// is publicly disseminated.  It must not vary from instance to instance.  This is pretty straightforward.   
        /// It defaults to 0
        /// </summary>
        int Version { get; }

        /// <summary>
        /// At some point typically you give up allowing new versions of the read to read old wire formats
        /// This is the Minimum version of the serialized data that this reader can deserialize.   Trying
        /// to read wire formats strictly smaller (older) than this will fail.   Setting this to the current
        /// version indicates that you don't care about ever reading data generated with an older version
        /// of the code.  
        /// 
        /// If you set this to something other than your current version, you are obligated to insure that
        /// your FromStream() method can handle all formats >= than this number. 
        ///
        /// You can achieve this if you simply use the 'WriteTagged' and 'ReadTagged' APIs in your 'ToStream' 
        /// and 'FromStream' after your V1 AND you always add new fields to the end of your class.   
        /// This is the best practice.   Thus  
        /// 
        ///     void IFastSerializable.ToStream(Serializer serializer)
        ///     {
        ///         serializer.Write(Ver_1_Field1);
        ///         serializer.Write(Ver_1_Field2);
        ///         // ...
        ///         serializer.WriteTagged(Ver_2_Field1);   
        ///         serializer.WriteTagged(Ver_2_Field2);
        ///         // ...
        ///         serializer.WriteTagged(Ver_3_Field1);
        ///     }
        /// 
        ///     void IFastSerializable.FromStream(Deserializer deserializer)
        ///     {
        ///         deserializer.Read(out Ver_1_Field1);
        ///         deserializer.Read(out Ver_1_Field2);
        ///         // ...
        ///         deserializer.TryReadTagged(ref Ver_2_Field1);  // If data no present (old format) then Ver_2_Field1 not set.
        ///         deserializer.TryReadTagged(ref Ver_2_Field2);  // ditto...
        ///         // ...
        ///         deserializer.TryReadTagged(ref Ver_3_Field1);    
        ///     } 
        /// 
        /// Tagging outputs a byte tag in addition to the field itself.   If that is a problem you can also use the
        /// VersionBeingRead to find out what format is being read and write code that explicitly handles it.  
        /// Note however that this only gets you Backward compatibility (new readers can read the old format, but old readers 
        /// will still not be able to read the new format), which is why this is not the preferred method.  
        /// 
        ///     void IFastSerializable.FromStream(Deserializer deserializer)
        ///     {
        ///         // We assume that MinVersionCanRead == 4
        ///         // Deserialize things that are common to all versions (4 and earlier) 
        ///         
        ///         if (deserializer.VersionBeingRead >= 5)
        ///         {
        ///             deserializer.Read(AVersion5Field);
        ///             if (deserializer.VersionBeingRead >= 5)
        ///                 deserializer.ReadTagged(AVersion6Field);    
        ///         }
        ///     }
        /// </summary>
        int MinimumVersionCanRead { get; }
        /// <summary>
        /// This is the minimum version of a READER that can read this format.   If you don't support forward
        /// compatibility (old readers reading data generated by new readers) then this should be set to 
        /// the current version.  
        /// 
        /// If you set this to something besides the current version you are obligated to insure that your
        /// ToStream() method ONLY adds fields at the end, AND that all of those added fields use the WriteTagged()
        /// operations (which tags the data in a way that old readers can skip even if they don't know what it is)
        /// In addition your FromStream() method must read these with the ReadTagged() deserializer APIs.  
        /// 
        /// See the comment in front of MinimumVersionCanRead for an example of using the WriteTagged() and ReadTagged() 
        /// methods. 
        /// </summary>
        int MinimumReaderVersion { get; }
    }

    /// <summary>
    /// Thrown when the deserializer detects an error. 
    /// </summary>
#if FASTSERIALIZATION_PUBLIC
    public
#endif
    class SerializationException : Exception
    {
        /// <summary>
        /// Thown when a error occurs in serialization.  
        /// </summary>
        public SerializationException(string message)
            : base(message)
        {
        }
    }

    #region internal classes
    internal sealed class SerializationType : IFastSerializable
    {
        /// <summary>
        /// This is the version represents the version of both the reading
        /// code and the version for the format for this type in serialized form.  
        /// See IFastSerializableVersion for more.  
        /// </summary>
        public int Version { get { return version; } }
        /// <summary>
        /// The version the the smallest (oldest) reader code that can read 
        /// this file format.  Readers strictly less than this are rejected.  
        /// This allows support for forward compatbility.   
        /// See IFastSerializableVersion for more.  
        /// </summary>
        public int MinimumReaderVersion { get { return minimumReaderVersion; } }
        public string FullName { get { return fullName; } }
        public IFastSerializable CreateInstance()
        {
            return factory();
        }
        public override string ToString()
        {
            return FullName;
        }

        #region private
        internal SerializationType() { }
        internal SerializationType(Type type)
        {
            fullName = type.FullName;
        }
        internal SerializationType(string fullName, Deserializer deserializer)
        {
            this.fullName = fullName;
            factory = deserializer.GetFactory(fullName);
        }
        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(version);
            serializer.Write(minimumReaderVersion);
            serializer.Write(fullName);
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out version);
            deserializer.Read(out minimumReaderVersion);
            deserializer.Read(out fullName);
            factory = deserializer.GetFactory(fullName);
            // This is only here for efficiency (you don't have to cast every instance)
            // since most objects won't be versioned.  However it means that you have to
            // opt into versioning or you will break on old formats.  
            if (minimumReaderVersion > 0)
            {
                IFastSerializableVersion instance = factory() as IFastSerializableVersion;

                // File version must meet minimum version requirements. 
                if (instance != null && !(version >= instance.MinimumVersionCanRead))
                {
                    throw new SerializationException(string.Format("File format is version {0} App accepts formats >= {1}.",
                            version, instance.MinimumVersionCanRead));
                }

                int readerVersion = 0;
                if (instance != null)
                {
                    readerVersion = instance.Version;
                }

                if (!(readerVersion >= minimumReaderVersion))
                {
                    throw new SerializationException(string.Format("App is version {0}.  File format accepts apps >= {1}.",
                             readerVersion, minimumReaderVersion));
                }
            }
        }

        internal int version;
        internal int minimumReaderVersion;
        internal string fullName;
        internal Func<IFastSerializable> factory;
        #endregion
    }

    internal enum Tags : byte
    {
        Error,              // To improve debugabilty, 0 is an illegal tag.  
        NullReference,      // Tag for a null object forwardReference. 
        ObjectReference,    // followed by StreamLabel 
        ForwardReference,   // followed by an index (Int32) into the Forward forwardReference array and a Type object
        BeginObject,        // followed by Type object, ToStream data, tagged EndObject
        BeginPrivateObject, // Like beginObject, but not placed in interning table on deserialiation 
        EndObject,          // placed after an object to mark its end (for V2 fields, and debugability). 
        ForwardDefinition,  // followed by a forward forwardReference index and an Object definition (BeginObject)
        // This is used when a a forward forwardReference is actually defined.  

        // In important invarient is that you must always be able to 'skip' to the next object in the
        // serialization stream.  For the first version, this happens naturally, but if you add
        // additional fields in V2, you want V1 readers to be able to skip these extra fields.  We do
        // this by requiring all post V1 fields that are added to be 'tagged' and requiring that objects
        // always end with the 'EndObject' tag.  After a 'FromStream' call, the deserializer will keep
        // reading objects until it finds an unmatched 'EndObject' tag.  Thus even though the V1
        // FromStream call has no knowledge of the extra fields, they are properly skipped.   For this
        // to work all most V1 fields must be tagged (so we know how to skip them even if we don't
        // understand what they are for).  That is what these tags are for.  
        // 
        // ToStream routines are free to encode all fields with tags, which allows a bit more
        // debuggability, because the object's data can be decoded even if the Deserializer is not
        // available.  
        Byte,
        Int16,
        Int32,
        Int64,
        SkipRegion,
        String,             // Size of string (in bytes) followed by UTF8 bytes.  
        Blob,
        Limit,              // Just past the last valid tag, used for asserts.  
    }
    #endregion

#if false
    public class SerializationTests
    {
        public class MyClass1 : IFastSerializable, IFastSerializableVersion
        {
            DeferedRegion lazy;
            private int value;
            private string str;
            private MyClass1 left;
            private MyClass1 right;
            private MyClass1 other;

            public int Value { get { lazy.FinishRead(); return value; } }
            public string Str { get { lazy.FinishRead(); return str; } }
            public MyClass1 Left { get { lazy.FinishRead(); return left; } }
            public MyClass1 Right { get { lazy.FinishRead(); return right; } }
            internal MyClass1 Other
            {
                get { lazy.FinishRead(); return other; }
                set { lazy.FinishRead(); other = Other; }
            }

            public MyClass1() { }       // Needed for the IFastSerializable contract.  
            public MyClass1(int value, string str, MyClass1 left, MyClass1 right, MyClass1 other)
            {
                this.value = value;
                this.str = str;
                this.left = left;
                this.right = right;
                this.other = other;
            }
            public override string ToString()
            {
                lazy.FinishRead();
                return value.ToString() + " : " + str;
            }

            int IFastSerializableVersion.Version
            {
                get { return 1; }
            }
            int IFastSerializableVersion.MinimumVersion
            {
                get { return 0; }
            }
            void IFastSerializable.ToStream(Serializer serializer)
            {
                serializer.Write(str);
                serializer.Write(value);
                lazy.Write(serializer, delegate
                {
                    serializer.Write(left);
                    serializer.Write(right);
                    serializer.Write(other);
                });

                // Add a few more fields, simulating V2
                serializer.WriteTagged(7);
                serializer.WriteTagged("Testing");
                serializer.WriteDefered(this);
                serializer.WriteDefered(left);
            }
            void IFastSerializable.FromStream(Deserializer deserializer)
            {
                deserializer.Read(out str);
                deserializer.Read(out value);
                lazy.Read(deserializer, delegate
                {
                    deserializer.Read(out left);
                    deserializer.Read(out right);
                    deserializer.Read(out other);
                });
            }
        }

        public static void Tests(string fileName)
        {
            Console.WriteLine("Writing serialized data to " + fileName);
            MyClass1 obj = MakeTree();
            Serializer serializer = new Serializer(fileName, obj);
            serializer.Close();

            Deserializer deserializer = new Deserializer(fileName);
            //deserializer.AllowLazyDeserialization = false;
            MyClass1 objRoundTrip;
   Assert(obj1.Str == obj2.Str);
            Comparer(obj1.Left, obj2.Left, depth - 1);
            Comparer(obj1.Right, obj2.Right, depth - 1);
            Comparer(obj1.Other, obj2.Other, depth - 1);
        }

        private static MyClass1 MakeTree()
        {
            MyClass1 bottomleft = new MyClass1(1, "Bottom left", null, null, null);
            MyClass1 bottomMiddle = new MyClass1(2, "Bottom Middle", null, null, bottomleft);
            MyClass1 bottomright = new MyClass1(3, "Bottom Right", null, null, bottomleft);

            MyClass1 Mid1 = new MyClass1(4, "Mid1", bottomleft, bottomMiddle, null);
            MyClass1 Mid2 = new MyClass1(5, "Mid2", Mid1, bottomright, null);

            MyClass1 ret = new MyClass1(6, "Ret", Mid1, bottomright, null);
            bottomleft.Other = ret;
            return ret;
        }
    }
#endif
}
