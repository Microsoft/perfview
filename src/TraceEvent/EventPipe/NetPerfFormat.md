# EventPipe NetPerf Format

[Update 6/6/2019] *As of .Net Core 3.0 the NetPerf format was superceded by the [NetTrace format](./EventPipeFormat.md), but TraceEvent remains a back-compatible parser so that old files can still be read. The doc is archived below as originally written.*


EventPipe is the name of the logging mechanism given to system used by the .NET Core 
runtime to log events in a OS independent way.   It is meant to serve roughly the same
niche as ETW does on Windows, but works equally well on Linux. 

By convention files in this format are call *.netperf files and this can be thought
of as the NetPerf File format.   However the format is more flexible than that.  

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
* StreamLabels: The format assumes you know the start of the stream (0) and 
    you keep track of your position.  The format currently assumes this is 
    a 32 bit number (thus limiting references using StreamLabels to 4GB) 
    This may change but it is a format change if you do).
* Compression: The format does not try to be particularly smart about compression
    The idea is that compression is VERY likely to be best done by compressing 
    the stream as a whole so it is not that important that we do 'smart' things
    like make variable length integers etc.   Instead the format is tuned for
    making it easy for the memory to be used 'in place' and assumes that compression
    will be done on the stream outside of the serialization/deserialization.  
 * Alignment: by default the stream is only assumed to be byte aligned.  However
    as you will see particular objects have a lot of flexibility in their encoding
    and they may choose to align their data.  The is valuable because it allows
    efficient 'in place' use of the data stream, however it is more the exception
    than the rule.  
    
## First Bytes: The Stream Header:

The beginning of the format is always the stream header.   This header's only purpose
is to quickly identify the format of this stream (file) as a whole, and to indicate
exactly which version of the basic Stream library should be used.    It is exactly
one (length prefixed UTF string with the value "!FastSerialization.1"  This declares
the the rest of file uses the FastSerialization version 1 conventions.  

Thus the first 24 bytes of the file will be
  4 bytes little endian number 20 (number of bytes in "!FastSerialization.1"
 20 bytes of the UTF8 encoding of "!FastSerialization.1"

After the format is a list of objects.  

## Objects:

The format has the concept of an object.   Indeed the stream can be thought of as
simply the serialization of a list of objects.  

Tags:  The format uses a number of byte-sized tags that are used in the serialization
and use of objects.    In particular there are BeginObject and EndObject which 
are used to define a new object, as well as a few other (discussed below) which
allow you to refer to objects.  
There are only a handful of them, see the Tags Enum for a complete list.  

Object Types: every object has a type.   A type at a minimum represents
   1. The name of the type (which allows the serializer and deserializer to agree what
      is being transmitted
   2. The version number for the data being sent.  
   3. A minimum version number.   new format MAY be compatible with old readers
      this version indicates the oldest reader that can read this format.

An object's structure is

* BeginObject Tag
* SERIALIZED TYPE 
* SERIALIZED DATA
* EndObject Tag

As mentioned a type is just another object, but the if that is true it needs a type
which leads to infinite recursion.   Thus the type of a type is always simply
a special tag call the NullReference that represent null.

## The First Object: The EventTrace Object

After the Trace Header comes the EventTrace object, which represents all the data
about the Trace as a whole.   

* BeginObject Tag  (begins the EventTrace Object)
* BeginObject Tag  (begins the Type Object for EventTrace)
* NullReference Tag (represents the type of type, which is by convention null)
* 4 byte integer Version field for type
* 4 byte integer MinimumReaderVersion field for type
* SERIALIZED STRING for FullName Field for type (4 byte length + UTF8 bytes)
* EndObject Tag (ends Type Object)
* DATA FIELDS FOR EVENTTRACE OBJECT  
* End Object Tag (for EventTrace object)  

The data field for object depend are deserialized in the 'FromStream' for
the class that deserialize the object.   EventPipeEventSource is the class
that deserializes the EventTrace object, so you can see its fields there. 
These fields are the things like the time the trace was collected, the
units of the event timestamps, and other things that apply to all events.  

See [Event Serialization](EventSerialization.md) for more.  

## Next Objects : The EventBlock Object

After the EventTrace object there are zero or more EventBlock objects.  
they look very much like the EventTrace object's layout ultimate fields
are different

* BeginObject Tag  (begins the EventBlock Object)
* BeginObject Tag  (begins the Type Object for EventBlock)
* NullReference Tag (represents the type of type, which is by convention null)
* 4 byte integer Version field for type
* 4 byte integer MinimumReaderVersion field for type
* SERIALIZED STRING for FullName Field for type (4 byte length + UTF8 bytes little endian)
* EndObject Tag (ends Type Object)
* DATA FIELDS FOR EVENTBLOCK OBJECT (size of blob + event bytes blob)
* End Object Tag (for EventBlock object)  

The data in an EventBlock is simply an integer representing the size (in
bytes not including the size int itself) of the data blob and the event
data blob itself.   

The event blob itself is simply a list of 'event' blobs.  each blob has
a header (defined by EventPipeEventHeader), following by some number of
bytes of payload data, followed by the byteSize and bytes for the stack
associated with the event.   See EventPipeEventHeader for details.

Some events are actually not true data events but represent meta-data 
about an event.  This data includes the name of the event, the name
of the provider of the event and the names and types of all the fields
of the event.   This meta-data is given an small integer numeric ID 
(starts at 1 and grows incrementally), 

One of the fields for an event is this Meta-data ID.   An event with 
a Meta-data ID of 0 is expected to be a Meta-data event itself.  
See the constructor of EventPipeEventMetaDataHeader for details of the 
format of this event.

## Ending the stream: The NullReference Tag

After the last EventBlock is emitted, the stream is ended by
emitting a NullReference Tag which indicates that there are no 
more objects in the stream to read.  

## Versioning the Format While Maintaining Compatibility

### Backward compatibility

It is a relatively straightforward exercise to update the file format
to add more information while maintaining backward compatibility (that is
new readers can read old writers).   What is necessary is to 

1. For the EventTrace Type, Increment the Version number 
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
file even if you only care about a small fraction of it.    If however
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
