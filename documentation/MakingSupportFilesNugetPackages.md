# Making the *.SupportFiles.nupkg NuGet Packages.

The build of PerfView depends on two packages of support files.  

 * Microsoft.Diagnostics.Tracing.TraceEvent.SupportFiles.1.0.0
 * PerfView.SupportFiles.1.0.0


These two Nuget packages contain binaries (DLLs and tools) that are assumed as 
part of the underlying platform, so they are not created as part of the build.  These
include 

 * The native code msdia* library that allows you to decode PDB files
 * The native KernelTraceControl* library that allows for merging of ETL files
 * Managed COM interop assemblies (generated with the TLBEXP tool on native type libraries)


These Nuget packages are available on https://www.nuget.org/ so the build should 'just work'
but it is certainly possible that at some point we will want to update these binaries. 
and this document indicates how to do this.  

There are files in the 'NugetSupportFiles' directory for regenerating these two Nuget packages.
We go through the procedure for PerfView.SupportFiles.1.0.0 but the same technique works for
Microsoft.Diagnostics.Tracing.TraceEvent.SupportFiles.1.0.0.


## Step 1 populate binaries you are not updating

It is likely that you only need to update a subset of all the DLLs in the package.  Thus you
need to start with an existing set.  You can do this by running editing and running the 
PerfView.SupportFiles.Populate.bat script.  

1.  First *look in %HOMEPATH%\.nuget\packages directory to see what the latest version for 
    the support package you are updating and modify 
    the PerfView.SupportFiles.Populate.bat* to copy files from the directory associated
    with this latest version.   You can also find the latest
    version of the SupportFiles package by looking the Directory.Build.props file at the 
    base of the repo.   *If you don't update this
    file to take from the current version, you will end up making a package with old 
    binaries in it, which cause fixed bugs to reappear*.  
2.  Then you can run the batch file. This copies the files you are currently using
    to form a baseline for the new package in a subdirectory of the src\NugetSupportFiles
	directory.  

This batch script also has a list of the files it is going to copy so that even if you don't
have the existing nuget package, you can populate the new package 'by hand' from 'raw' files.

## Step 2 Update the files in the baseline Image (PerfView.SupportFiles)

This is dependent on exactly what you want to do. However something you should ALWAYS do
is *update the version number in the PerfView.SupportFiles.nuspec file*.  You should also
update the releaseNotes element in the nuspec file. Thus at this
point you have updates the *.Populate*.bat file and the *.nuspec file. 

## Step 3 Generate the nuget package

There is a script called PerfView.SupportFiles.MakeNuget.bat which does this.  It is a one line
script, and generates a new *.nupkg in the current directory.  

## Step 4 Test your new package

The PerfView reposititory has a Nuget.config file that tells nuget to look in the src\NugetSupportFiles
directory for nuget packages.   The 'Directory.Build.props' file is the file that tells
what particular version of each of the *.SupportFiles* packages it should actually use.  
THus at this point (even before uploading to Nuget.org) you can update the Directory.Build.props to
point at your latest version, and build PerfView and test that the new behavior works.  

Note that you can't actually check in this change to Directory.Build.props, however because 
anyone else trying to build PerfView (including the PerfView appveyor system) does not have access
to your new nuget packages. This change is only for local testing (at the moment).

## Step 5 Sign your New Nuget packages and upload them to nuget.org.

Nuget now requires that all Microsoft owned packages use nuget's package signing in order
to be uploaded.  You also need special permission to upload them.   See 
[Internal Docs](https://devdiv.visualstudio.com/DevDiv/_git/perfview?_a=preview&path=%2Fdocumentation%2Finternal%2FinternalDocs.md&version=GBmain) for more on this.   

It does take a few minutes after uploading for the packages to become visible. Be patient.  

## Step 6 Check in the use of the updated Support packages.

Once the new packages are available you can actually check in the changes to Directory.Build.props to use
the new packages officially. It is a good practice to update the *.Populate.bat file and *.nuspec 
files to the NEXT version (that way you can only make a mistake if you forget to do this TWICE).  
