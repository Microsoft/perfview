REM copies from an existing build to a nuget package creation area (so that *.MakeNuget.bat works)
REM
REM *** This is mostly a template for doing the copy.      ****  
REM *** Most likey you want this to be the current version ****
REM *** PLEASE MODIFY THE VERSION NUMBER TO BE CURRENT!    ****
REM 
xcopy /s %HOMEDRIVE%%HOMEPATH%\.nuget\packages\PerfView.SupportFiles\1.0.5\*.dll PerfView.SupportFiles
xcopy /s %HOMEDRIVE%%HOMEPATH%\.nuget\packages\PerfView.SupportFiles\1.0.5\*.exe PerfView.SupportFiles

@REM These are the binary files we need from somewhere to for the support package
@REM lib\native\x86\DiagnosticsHub.Packaging.dll
@REM lib\native\x86\DiagnosticsHub.Packaging.Interop.dll
@REM lib\native\x86\sd.exe
@REM lib\net40\DiagnosticsHub.Packaging.Interop.dll
@REM lib\net40\Microsoft.Diagnostics.Tracing.EventSource.dll
@REM lib\net40\Microsoft.DiagnosticsHub.Packaging.InteropEx.dll
@REM tools\MC.Exe
