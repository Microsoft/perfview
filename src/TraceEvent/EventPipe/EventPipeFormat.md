# EventPipe (File) Format

EventPipe is the name of the logging mechanism given to system used by the .NET Core 
runtime to log events in a OS independent way.   It is meant to serve roughly the same
niche as ETW does on Windows, but works equally well on Linux. 

By convention files in this format are call *.nettrace files and this can be thought
of as the NetTrace File format. There was a previous version of the format named *.netperf which was used in .Net Core 2.1 and some Previews of .Net Core 3. NetPerf documentation is archived in [here](./NetPerfFormat.md). For those interested in the changes between versions, there is a summary at the end of this document.

The format was designed to take advantage of the facilities of the FastSerialization 
library used by TraceEvent, however the format can be understood on its own, and here
we describe everything you need to know to use the format.

Fundamentally, the data can be thought of as a serialization of objects.  we want the 
format to be Simple, Extensible (it can tolerate multiple versions) and
make it as easy as possible to be both backward (new readers can read old data version) 
and forward (old readers can read new data versions).  We also want to be efficient 
and STREAMABLE (no need for seek, you can do most operations with just 'read').

Assumptions of the Format:  

We assume the following:

* Primitive Types: The format assumes you can emit the primitive data types
    (byte, short, int, long).  It is in little endian (least significant byte first)
* Strings: Strings can be emitted by emitting a int BYTE count followed by the
    UTF8 encoding 
 * Alignment: by default the stream is only assumed to be byte aligned.  However
    as you will see particular objects have a lot of flexibility in their encoding
    and they may choose to align their data.  The is valuable because it allows
    efficient 'in place' use of the data stream, however it is more the exception
    than the rule.  
    

## First Bytes: NetTrace Magic

The beginning of the format is always the stream header.   This header's only purpose
is to quickly identify the format of this stream (file) as a whole, and to indicate
exactly which version of the basic Stream library should be used. It is the UTF8 encoding of the characters 'Nettrace', thus the first 8 bytes will be:

45 65 74 74 72 61 63 65

## Stream Header

Following that is the generic header used by the FastSerialization library,
a length prefixed UTF string with the value "!FastSerialization.1"  This declares
the the rest of file uses the FastSerialization version 1 conventions.  

Thus the next 24 bytes of the file will be:

 -  4 bytes little endian number 20 (number of bytes in "!FastSerialization.1"
 - 20 bytes of the UTF8 encoding of "!FastSerialization.1"

After the format is a list of objects.  

## Objects:

The format has the concept of an object.   Indeed the stream can be thought of as
simply the serialization of a list of objects.  Each object consists of a payload of bytes and a type.

**Object payload data:** Each object has a payload of serialized data, and the format of this data is defined by its type.

**Object Types:** A type at a minimum represents:

   1. The name of the type (which allows the serializer and deserializer to agree what
      is being transmitted
   2. The version number for the data being sent.  
   3. A minimum version number. A new format may be compatible with old readers.
      This version indicates the oldest reader that can correctly interpret the payload of the object.

**Tags:**  The format uses a number of byte-sized tags that are used to demarcate the boundaries of objects and as sentinel values. In particular there are BeginPrivateObject and EndObject which are used to define a new object, and NullReference used to define Null. These tags are encoded as the bytes:

    NullReference      = 1 
    BeginPrivateObject = 5
    EndObject          = 6

**Object:** An object is serialized as the following things concatenated:

* BeginPrivateObject Tag
* SERIALIZED TYPE
* SERIALIZED PAYLOAD
* EndObject Tag

As mentioned a type is just another object, but the if that is true it needs a type
which leads to infinite recursion.   Thus the SERIALIZATION TYPE data for a type is always the NullReference tag.

**Type object:** The payload of a type consists of the following fields concatenated:

- version (int)
- minimum reader version (int)
- type name (string)


## The First Object: The Trace Object

- TypeName: Trace
- Version: 4
- MinumumReaderVersion: 4 

After the Stream Header, the first object is the Trace object, which represents all the data about the Trace as a whole.   

* 5 - BeginPrivateObject Tag  (begins the Trace Object)
* 5 - BeginPrivateObject Tag  (begins the Type Object for Trace)
* 1 - NullReference Tag (represents the type of type, which is by convention null)
* 4 - 4 byte integer Version field for type
* 4 - 4 byte integer MinimumReaderVersion field for type
* 'Trace' - SERIALIZED STRING for TypeName field for type (4 byte length + UTF8 bytes)
* 6 - EndObject Tag (ends Type Object)
* DATA FIELDS FOR TRACE OBJECT  
* 6 - End Object Tag (for Trace object)  

The data fields for an object are deserialized in the 'FromStream' for
the class that deserializes the object.   EventPipeEventSource is the class
that deserializes the Trace object, so you can see its fields there. 
These fields are the things like the time the trace was collected, the
units of the event timestamps, and other things that apply to all events.  

The Trace object has the following fields:

- SyncTimeUTC:
 - Year - short
 - Month - short
 - DayOfWeek - short
 - Day - short
 - Hour - short
 - Minute - short
 - Second - short
 - Millisecond - short
- SyncTimeQPC - long
- QPCFrequency - long
- PointerSize - int
- ProcessId - int
- NumberOfProcessors - int
- ExpectedCPUSamplingRate - int

 

## Remaining Stream objects:

Following the trace object, there is a sequence of zero or more of these object types in any order:

- **EventBlock** - Contains 1 or more serialized events
- **MetadataBlock** - Contains 1 or more metadata records. Each metadata record describes one type of event such as its name, id, provider, and the layout of the event payload data.
- **StackBlock** - Contains 1 or more stacks. Each event can optionally refer to a stack.
- **SPBlock** - Sequence Point blocks delineate regions within the trace file that are useful cache management, dropped event detection, and sorting boundaries.


## The EventBlock Object

- TypeName: EventBlock
- Version: 2
- MinumumReaderVersion: 2 

Each EventBlock contains a set of events and every event in the trace is contained within some EventBlock. The event block payload consists of the following concatenated fields:

- BlockSize - int - The size in bytes not including the size int itself of the remainder of the payload after the alignment padding
- 0 padding to reach 4 byte file alignment
- Header
  - HeaderSize - short - Size of the header including this field
  - Flags - short
  - Min Timestamp - long - The minimum timestamp of any event in this block
  - Max Timestamp - long - The maximum timestamp of any event in this block
  - optional reserved space based to reach HeaderSize bytes. Later versions of the format may embed extra data here so no assumption should be made about the contents.
- 1 or more Event blobs described further below. The end of the last event should coincide exactly with the BlockSize for this block

## The MetadataBlock Object

- TypeName: MetadataBlock
- Version: 2
- MinumumReaderVersion: 2 

Each MetadataBlock holds a set of metadata records. Each metadata record has an ID and it describes one type of event. Each event has a metadataId field which will indicate the ID of the metadata record which describes that event. Metadata includes an event name, provider name, and the layout of fields that are encoded in the event's payload section.

The MetadataBlock uses the same encoding as EventBlock and each metadata record is encoded as an event blob. The only distinction is that the MetadataId field of the event blobs is always zero, and the payload of the event blobs is described using a fixed format, not more metadata (or infinite recursion would ensue).

The fixed metadata payload format is described in the Event blob section below.

## Event blobs

Event blobs are not Objects (ie they aren't defined with tags and types), but rather serialized fragments of data that are included inside of an EventBlock or MetadataBlock. Event blobs serialized inside an EventBlock represent actual events whereas event blobs serialized inside a metadata block represent metadata records which describe events.

An event blob consists of a header and a payload. It can be encoded one of two ways, depending on the Flags field of the enclosing Block. If the flags field has the 1 bit set then events are in 'Header Compression' format, otherwise they are in uncompressed format. 

### Uncompressed events

In the uncompressed format an event blob is:

- Header
  - EventSize - int - The size of event blob not counting this field
  - MetadataId - int - In the context of an EventBlock the low 31 bits are a foreign key to the event's metadata. In the context of a metadata block the low 31 bits are always zero'ed. The high bit is the IsSorted flag.
  - Sequence Number - int - An incrementing counter that is used to detect dropped events
  - ThreadId - long - This the thread the event is logically describing. It is usually the same thread that physically recorded the event, but in some cases thread A will record some state that represents some other thread B.
  - CaptureThreadId - long - This is the thread which physically captured the event. Sequence Numbers are tracked relative to the CaptureThreadId
  - ProcessorNumber - int - Identifies which processor CaptureThreadId was running on
  - StackId - int - A foreign key to a stack that should be associated with this event. See StackBlock for more details.
  - TimeStamp - long - The QPC time the event occurred
  - ActivityID - GUID
  - RelatedActivityID - GUID
  - PayloadSize - int
- Payload - PayloadSize bytes of data. For event blobs in an EventBlock the layout of this data is described by the corresponding Metadata, for event blobs in the MetadataBlock this is data uses the metadata encoding defined below.
- Zero padding - Aligns to 4 byte file offset

### Header compression

In the header compression format an event blob uses a few additional encoding techniques:

1. variable length encodings of int and long. This is a standard technique where the least significant 7 bits are encoded in a byte. If all remaining bits in the starting number are zero then the 8th bit is set to zero to mark the end of the encoding. Otherwise the 8th bit is set to 1 to indicate another byte is forthcoming containing the next 7 least significant bits. This pattern repeats until there are no more non-zero bits to encode, at most 5 bytes for a 32 bit int and 10 bytes for a 64 bit long.

2. Optional fields - The first byte of the event blob is a set of flag bits that are used to indicate whether groups of fields are present or not. For each group of fields that is not present there is calculation that can be done based on the previously decoded event header in the same block to determine the field values.

3. Delta encoding - In cases where the field is present, the value that is encoded may be a relative value based on the same field in the previous event header. When starting a new event block assume that the previous event contained every field with a zeroed value.


Compressed events are encoded with a header:

- Flags byte
- MetadataId optional varint32 
  - if Flags & 1 the value is read from the stream
  - otherwise duplicate the previous MetadataId
- SequenceNumber optional varint32 
 - if Flags & 2 the value is read from the stream + previous SequenceNumber
 - otherwise previous sequence number
 - in either case, if MetadataId != 0 increment SequenceNumber by 1
- CaptureThreadId optional varint64
  - if Flags & 2 the value is read from the stream
  - otherwise previous CaptureThreadId
- Processor Number optional varint32
  - if Flags & 2 the value is read from the stream
  - otherwise previous Processor Number
- ThreadId optional varint64
  - if Flags & 4 the value is read from the stream
  - otherwise previous ThreadId
- StackId optional varint32
  - if Flags & 8 the value is read from the stream
  - otherwise previous StackId
- TimeStamp varint64 - read value from stream + previous value
- ActivityId optional GUID
  - if Flags & 16, read from stream
  - otherwise previous value
- RelatedActivityId optional GUID
  - if Flags & 32, read from stream
  - otherwise previous value
- IsSorted has value (flags & 64) == 64
- PayloadSize optional varint32
  - if Flags & 128, read from stream
  - otherwise previous value

Followed by PayloadSize bytes of payload and no alignment.

### Metadata event encoding

For event blobs within a MetadataBlock, the payload describes a type of event. The payload contains a V1 and V2 field description, versions 1-4 of the file format did not support array types (arbritrary number of arguments of the same type) and the V2 field description was added to enable this.


The PayloadBytes of such a MetaData definition are:


    int MetaDataId;      // The Meta-Data ID that is being defined.
    string ProviderName; // The 2 byte Unicode, null terminated string representing the Name of the Provider (e.g. EventSource)
    int EventId;         // A small number that uniquely represents this Event within this provider.  
    string EventName;    // The 2 byte Unicode, null terminated string representing the Name of the Event
    long Keywords;       // 64 bit set of groups (keywords) that this event belongs to.
    int Version          // The version number for this event.
    int Level;           // The verbosity (5 is verbose, 1 is only critical) for the event.

Following this header there is a V1 Payload description.   This consists of 

    int FieldCount;      // The number of fields in the payload

Followed by FieldCount number of field definitions 

    int TypeCode;	 // This is the System.Typecode enumeration
    <PAYLOAD_DESCRIPTION>
    string FieldName;    // The 2 byte Unicode, null terminated string representing the Name of the Field

In File format V5 and later only, following the FieldCount number of fields there are an optional set of metadata tags, where a tag consists of 

    int TagPayloadBytes; // The number of bytes the tag payload uses, uninclusive of this field
    byte TagKind;        // The type of tag it is. Currently used values are OpCode=1 V2Params=2

Followed by a tag payload. If TagKind==OpCode the payload consists of

    byte OpCode;         // The event's opcode

If TagKind=V2Params the payload consists of

    int V2FieldCount;    // The total number of fields in this payload

Followed by V2FieldCount number of field definitions

    int V2TypeCode;      // In V2 this can be EventPipeTypeCodeArray==19
    int ArrayTypeCode;   // This optional field only appears when V2TypeCode==EventPipeTypeCodeArray
    <PAYLOAD_DESCRIPTION>
    string FieldName

If the metadata event specifies a V2Params tag, the event must have an empty V1 parameter FieldCount and no field definitions.

For primitive types and strings <PAYLOAD_DESCRIPTION> is not present, however if TypeCode == Object (1) then <PAYLOAD_DESCRIPTION> another payload
description (that is a field count, followed by a list of field definitions).   These can be nested to arbitrary depth.  

No attempt is made to be sophisticated about serializing the payload fields for event blobs in an EventBlock.   Each primitive type is serialized in little endian format.  Strings 
are serialized as 2 byte unicode, null terminated strings.   Everything is serialized as its natural size and no alignment is done between fields
(everything is packed without spacing).


## StackBlock Object

- TypeName: StackBlock
- Version: 2
- MinumumReaderVersion: 2

The stack block contains a set of stacks that can be reference from events using an ID. This allows many events to refer the same stack without needing to repeat its encoding for every event.

The payload of a stack block is:

- BlockSize int - Size of the block in bytes starting after the alignment padding
- Alignment padding to 4 byte file offset
- Header
  - First Id - int
  - Count - int
- Concatenated sequence of Count stacks, each of which is
  - StackSize in bytes - int
  - StackSize number of bytes

On 32 bit traces the stack bytes can be interpreted as an array of 4 byte integer IPs. On 64 bit traces it is an array of 8 byte integer IPs. Each stack can be given an ID by starting with First Id and incrementing by 1 for each additional stack in the block in stream order. 

## SequencePointBlock Object

- TypeName: SPBlock
- Version: 2
- MinumumReaderVersion: 2 

A sequence point object is used as a stream checkpoint and serves several purposes.

1. It demarcates time with a TimeStamp. All the events in the stream which occurred prior to this TimeStamp are located before this sequence point in file order. Likewise all events which happened after this timestamp are located afterwards. The TimeStamps on successive Sequence points always increase so if you are interested in an event that happened at a particular point in time you can search the stream to find SequencePoints that occurred beforehand/afterwards and be assured the event will be in that region of the file.

2. It contains a list of every thread that could potentially emit an event at that point in time and a lower bound on the last event sequence number that thread attempted to log. This can be used to detect dropped events. See the section on Sequence numbering for more info.

3. The sequence point serves as a barrier for stack references. Events are only allowed to refer to a stack id if there is no sequence point in between the event and the stack in stream encoding order. This simplifies cache management by ensuring only a bounded region of stacks need to be kept in memory to resolve event->stack references.

A SequencePointBlock payload is encoded:

- BlockSize int - Size of the block in bytes starting after the alignment padding
- Alignment padding to 4 byte file offset
- TimeStamp long
- ThreadCount - int
- A sequence of ThreadCount threads, each of which is encoded:
  - ThreadId - long
  - SequenceNumber - int

## Ending the stream: The NullReference Tag

After the last object is emitted, the stream is ended by
emitting a NullReference Tag which indicates that there are no 
more objects in the stream to read.  

## Event Sequence Numbers

Every thread that can log events assigns each event a sequence number. Each thread has an independent counter per-session which starts at 1 for the first event, increments by 1 for each successive event, and if necessary rolls over back to zero after event 2^32-1. Sequence numbers are assigned at the time events enter the EventPipe API, regardless of whether the events are committed into the file stream or dropped for whatever reason. This allows the eventual reader of the file format to locate gaps in the sequence number sequence to infer that events have been lost. Spotting these gaps can occur two ways:

1. For a given CaptureThreadId the sequence number should always increase by 1. If events on the same CaptureThreadId skip sequence numbers then events have been dropped. There is one special exception - if the sequence number suddenly resets to 1 then it is possible that the previous thread with this ID died, the OS created a new thread with the same ID, and the new thread began as normal at 1. This ambiguity is unfortunate and might be resolved in a future revision of the file format.
2. Every sequence point contains a lower bound for the sequence number on each thread. If the sequence point records a higher sequence number for a given thread then the last observed event on that thread, events have been dropped. This allows detecting cases where a thread is dropping all of its events, likely because all buffers are remaining persistently full.

## Event TimeStamp sorting

In order to lower runtime overhead of emitted events and to improve locality for events emitted by the same thread, the runtime does not fully sort events based on time when emitting them. Instead if the reader is responsible for sorting if it needs to produce strongly time-ordered event sequences. The file format does offer some more limited guarantees to assist the reader in doing this while streaming and with limited computational resources.

1. All events in between a pair of sequence points are guaranteed to have timestamps in between the sequence point times. This means sorting each set of events between sequence points and then concatenating the lists together in file order will generate a complete sort of all events. The writer will attempt to bound the number of events between sequence points so sorting the file is ultimately O(N) for an N byte file as sorting each chunk is O(1) and there are N chunks.

2. In order to produce on-demand sorts with low latency in streaming situations, some events are marked with the IsSorted flag set to true. This flag serves as a weaker version of the sequence point and indicates that every event after this one in file order has a timestamp which is >= to the current event. For streaming this means the reader can safely sort and emit all events it has cached in the current sequence point region that have a timestamp older than this event. It is guaranteed no future event will have a timestamp in that range which would invalidate the sort. The runtime writes batches of events that have queued up at small time intervals (currently 100ms, maybe configurable in the future) and it is guaranteed at least one event in every batch will have this flag set. As long as the file writer is able to keep up with the rate of event generation this means every event in the previous batch will have a timestamp less than the marked event in the next batch and all of them can be sorted and emitted with a one time unit latency. 

## Versioning the Format While Maintaining Compatibility

### Backward compatibility

It is a relatively straightforward exercise to update the file format
to add more information while maintaining backward compatibility (that is
new readers can read old writers).   What is necessary is to 

1. For the Trace Type, Increment the Version number 
and set the MinimumReaderVersion number to this same value.   
2. Update the reader for the changed type to look at the Version
number of the type and if it is less than the new version do
what you did before, and if it is the new version read the new format
for that object.    

By doing (1) we make it so that every OLD reader does not simply 
crash misinterpreting data, but will learly notice that it does 
not support this new version (because the readers Version is less
than the MinimumReaderVersion value), and can issue a clean error
that is useful to the user.  

Doing (2) is also straightforward, but it does mean keeping the old
reading code.  This is the price of compatibility.  

### Forward compatibility

Making changes so that we preserve FORWARD compatibility (old readers
can read new writers) is more constaining, because old readers have
to at least know how to 'skip' things they don't understand.  

There are however several ways to do this.  The simplest way is to

* Add Tagged values to an object.

Every object has a begin tag, a type, data objects, and an end tag.
One feature of the FastSerialiable library is that it has a tag 
for all the different data types (bool, byte, short, int, long, string, blob).
It also has logic that after parsing the data area it 'looks' for 
the end tag (so we know the data is partially sane at least).  However
during this search if it finds other tags, it knows how to skip them.
Thus if after the 'Known Version 0' data objects, you place tagged
data, ANY reader will know how to skip it (it skips all tagged things
until it finds an endObject tag).  

This allows you to add new fields to an object in a way that OLD
readers can still parse (at least enough to skip them).   

Another way to add new data to the file is to 

* Add new object (and object types) to the list of objects.

The format is basically a list of objects, but there is no requirement
that there are only very loose requirements on the order or number of these
Thus you can create a new object type and insert that object in the
stream (that object must have only tagged fields however but a tagged
blob can represent almost anything).  This allows whole new objects to be 
added to the file format without breaking existing readers. 

* Insert new data into the EventBlock or MetadataBlock headers. These headers start with a header size so old readers will know to skip bytes even if they don't understand how to read the additional fields. This is a little cheaper size-wise and easier to reason about than embedding new objects adjacent to some other object that is logically being extended.

#### Version Numbers and forward compatibility.

There is no STRONG reason to update the version number when you make
changes to the format that are both forward (and backward compatible).
However it can be useful to update the file version because it allows
readers to quickly determine the set of things it can 'count on' and 
therefore what user interface can be supported.   Thus it can be useful
to update the version number when a non-trival amount of new functionality
is added.  

You can update the Version number but KEEP the MinimumReaderVersion 
unchanged to do this.  THus readers quickly know what they can count on
but old readers can still read the new format.   

## Suport for Random Access Streams

So far the features used in the file format are the simplest.  In particular
on object never directly 'points' at another and the stream can be 
processed usefully without needing information later in the file.  

But we pay a price for this: namely you have to read all the data in the 
file (or at least skip in bounded chunks) even if you only care about a small fraction of it.    If however
you have random access (seeking) for your stream (that is it is a file), 
you can overcome this.

The serialization library allows this by supporting a table of pointers
to objects and placing this table at the end of the stream (when you 
know the stream locations of all objects).  This would allow you to
seek to any particular object and only read what you need.   

The FastSerialization library supports this, but the need for this kind
of 'random access' is not clear at this time (mostly the data needs 
to be processed again and thus you need to read it all anyway).  For
now it is enough to know that this capability exists if we need it.  

## Changes relative to older file format versions

### Version 3 (NetPerf) -> 4 (NetTrace)

**Not Forward Compatible**

In general the file format became a little less simplistic in this revision to improve size on disk, speed up any IO bound read/write scenario, detect dropped events, and efficiently support a broader variety of access patterns. In exchange implementing a parser became a bit more complex and any read scenario that was previously memory or CPU bound likely degraded.
Although not intending to abandon the original goals of simplicity, this definitely takes a few steps away from it in the name of performance. One of the original premises was that the format should make no effort to at conserving size because generic compression algorithms could always recover it. This still feels true in the case where you have a file on disk and there is no tight constraint on CPU or latency to compress it. This doesn't appear to hold up as well when you assume that you want to do streaming with low latency guarantees and constrained writer CPU resources. General purpose compression algorithms need both CPU cycles and sufficient blocks of data to recognize and exploit compressible patterns. On the other hand we have a priori knowledge about potentially large parts of the format that are highly compressible (often better than 10:1). At the cost of some complexity we can write targeted algorithms that recover considerable low hanging fruit at very low CPU/latency cost.

Changes:

1. The 'Nettrace' magic was added to the front of the file to better distinguish Nettrace from any other serialized output of the FastSerializer library.
2. Metadata was moved out of EventBlocks and into separate MetadataBlocks. This makes it dramatically easier to locate in scenarios where the reader is searching only for specific events and the metadata necessary to interpret those events rather than wanting to parse every event in the stream.
3. Stacks are interned and stored in StackBlocks rather than inlined into each event. This gives considerable file size savings in scenarios where stacks are commonly repeated. This added StackBlocks and the StackId event field.
4. Header compression often dramatically reduces the file size overhead. Previously we paid a fixed 56 bytes per event. Anecdotally in a scenario I looked at recently compressed headers average 5 bytes despite encoding more data than before.
5. Timestamp ranges in the headers of EventBlocks help locate EventBlocks of interest without having to parse every event inside them.
6. Sequence numbering events aids in dropped event detection. This added the SequenceNumber and CaptureThreadId fields to event blobs.
7. ThreadId is now 64 bit instead of 32 bit to support large thread IDs used by some OSes.
8. Events are no longer guaranteed to be sorted. This alleviates potential cost and scalability concern from the runtime emitting the events in favor of paying that cost in the reader where processing overhead is more easily accepted. The IsSorted field was added to events to help the reader quickly sort them.
9. SequencePoint blocks are new and were added to support several of the above features: Cache boundaries for the stack interning, dropped event detection for the seqeunce numbering, and cache management/sort boundaries for the unsorted events.
10. The version number of the Trace object moved from 3 -> 4 and the version of EventBlock moved 1 -> 2.
11. BeginObject tags are replaced by PrivateBeginObject to avoid memoization based memory leaks in the FastSerialization library.

### Version 4 -> 5

This version adds a set of optional tags after the metadata header and payload. These tags are used to describe the opcode of an event or include metadata for types that are unsupported in V4 and earlier.

