# PerfView
PerfView is a performance-analysis tool that helps isolate CPU- and memory-related performance issues.

If you are unfamiliar with PerfView, there are [PerfView video tutorials](http://channel9.msdn.com/Series/PerfView-Tutorial). As well as [Vance Morrison's blog](http://blogs.msdn.com/b/vancem/archive/tags/perfview) which also gives overview and getting started information. 

The PerfView executable is ultimately published at the [PerfView download Site](http://www.microsoft.com/en-us/download/details.aspx?id=28567). It is a standalone executable file (packaged in a ZIP archive). You can be running it in less than a minute!  

The PerfView users guide is part of the application itself, however you can get the .HTM file for it in the users guide in the soruce code itself at [PerfView/SupportDlls/UsersGuide.htm](src/PerfView/SupportDlls/UsersGuide.htm).

PerfView is designed to build in Visual Studio 2013 or later.  The solution file is src/PerfView/Perfview.sln.  Opening this file in Visual file and selecting the Build -> Build Solution, will build it.   It follows standard Visual Studio conventions, and the resulting PerfView.exe file ends up in the src/PerfView/bin/<BuildType>/PerfView.exe   You need only deploy this one EXE to use it.  

The code is broken in several main sections:
  * TraceEvent - Library that understands how to decode Event Tracing for Windows (ETW) which is used to actually collect the data for many investgations
  * PerfView - GUI part of the application
  * MainWindow - GUI code for the window that is initially launched (lets you select files or collect new data) 
  * StackViewer - GUI code for any view with the 'stacks' suffix
  * EventViwer - GUI code for the 'events' view window
  * Dialogs - GUI code for a variety of small dialog boxes (although the CollectingDialog is reasonably complex)
  * Memory - Contains code for memory investigations, in particular it defines 'Graph' and 'MemoryGraph' which are used to desiplay node-arc graphs (e.g. GC heaps)
