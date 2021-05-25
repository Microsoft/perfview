/****************************************************************************/
/*                                  Command.cs                                  */
/****************************************************************************/


/*  Copyright (c) Microsoft Corporation.  All rights reserved. */
/* AUTHOR: Vance Morrison   
 * Date  : 10/20/2007  */
/****************************************************************************/

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Reflection;
using System.Diagnostics;               // for StackTrace; Process


/// <summary>
/// CommandOptions is a helper class for the Command class.  It stores options
/// that affect the behavior of the execution of Commands and is passes as a 
/// parapeter to the constuctor of a Command.  
/// 
/// It is useful for these options be be on a separate class (rather than 
/// on Command itself), because it is reasonably common to want to have a set
/// of options passed to several commands, which is not easily possible otherwise. 
/// </summary>
public sealed class CommandOptions
{
    /// <summary>
    /// Can be assigned to the Timeout Property to indicate infinite timeout. 
    /// </summary>
    public const int Infinite = System.Threading.Timeout.Infinite;

    /// <summary>
    /// CommanOptions holds a set of options that can be passed to the constructor
    /// to the Command Class as well as Command.Run*
    /// </summary>
    public CommandOptions()
    {
        timeoutMSec = 600000;
    }

    /// <summary>
    /// Return a copy an existing set of Command options
    /// </summary>
    /// <returns>The copy of the Command options</returns>
    public CommandOptions Clone()
    {
        return (CommandOptions)MemberwiseClone();
    }

    /// <summary>
    /// Normally commands will throw if the subprocess returns a non-zero 
    /// exit code.  NoThrow suppresses this. 
    /// </summary>
    public bool NoThrow { get { return noThrow; } set { noThrow = value; } }

    /// <summary>
    /// Updates the NoThrow propery and returns the updated commandOptions.
    /// <returns>Updated Command options</returns>
    public CommandOptions AddNoThrow()
    {
        this.noThrow = true;
        return this;
    }

    /// <summary>
    /// Normally commands are launched with CreateProcess.  However it is
    /// also possible use the Shell Start API.  This causes Command to look
    /// up the executable differnetly as well as no wait for the Command to 
    /// compete before returning.
    /// </summary>
    public bool Start { get { return start; } set { start = value; } }

    /// <summary>
    /// Updates the Start propery and returns the updated commandOptions.
    /// </summary>
    public CommandOptions AddStart()
    {
        this.start = true;
        return this;
    }

    /// <summary>
    /// By default commands have a 10 minute timeout (600,000 msec), If this
    /// is inappropriate, the Timeout property can change this.  Like all
    /// timouts in .NET, it is in units of milliseconds, and you can use
    /// CommandOptions.Infinite to indicate no timeout. 
    /// </summary>
    public int Timeout { get { return timeoutMSec; } set { timeoutMSec = value; } }

    /// <summary>
    /// Updates the Timeout propery and returns the updated commandOptions.
    /// </summary>
    public CommandOptions AddTimeout(int milliseconds)
    {
        this.timeoutMSec = milliseconds;
        return this;
    }

    /// <summary>
    /// Indicates the string will be sent to Console.In for the subprocess.  
    /// </summary>
    public string Input { get { return input; } set { input = value; } }
    /// <summary>
    /// Updates the Input propery and returns the updated commandOptions.
    /// </summary>
    public CommandOptions AddInput(string input)
    {
        this.input = input;
        return this;
    }

    /// <summary>
    /// Indicates the current directory the subProcess will have. 
    /// </summary>
    public string CurrentDirectory { get { return currentDirectory; } set { currentDirectory = value; } }
    /// <summary>
    /// Updates the CurrentDirectory propery and returns the updated commandOptions.
    /// </summary>
    public CommandOptions AddCurrentDirectory(string directoryPath)
    {
        this.currentDirectory = directoryPath;
        return this;
    }

    // TODO add a capability to return a enumerator of output lines. (and/or maybe a delegate callback)

    /// <summary>
    /// Indicates the standard output and error of the Command should be redirected
    /// to a archiveFile rather than being stored in memory in the 'Output' property of the
    /// Command.
    /// </summary>
    public string OutputFile
    {
        get { return outputFile; }
        set
        {
            if (outputStream != null || outputHandler != null)
                throw new Exception("Only one of OutputFile, OutputStream and OutputHandler can be set");
            outputFile = value;
        }
    }

    /// <summary>
    /// Updates the OutputFile propery and returns the updated commandOptions.
    /// </summary>
    public CommandOptions AddOutputFile(string outputFile)
    {
        OutputFile = outputFile;
        return this;
    }

    /// <summary>
    /// Indicates the standard error of the Command should be redirected
    /// to a a TextWriter rather than being stored in memory in the 'Error' property 
    /// of the Command.
    /// </summary>
    public TextWriter ErrorStream
    {
        get { return errorStream; }
        set
        {
            errorStream = value;
        }
    }

    /// <summary>
    /// Indicates the standard output of the Command should be redirected
    /// to a a TextWriter rather than being stored in memory in the 'Output' property 
    /// of the Command.
    /// </summary>
    public TextWriter OutputStream
    {
        get { return outputStream; }
        set
        {
            if (outputFile != null || outputHandler != null)
                throw new Exception("Only one of OutputFile, OutputStream and OutputHandler can be set");
            outputStream = value;
        }
    }

    public DataReceivedEventHandler OutputHandler
    {
        get { return outputHandler; }
        set
        {
            if (outputStream != null || outputFile != null)
                throw new Exception("Only one of OutputFile, OutputStream and OutputHandler can be set");
            outputHandler = value;
        }
    }

    /// <summary>
    /// Updates the ErrorStream propery and returns the updated commandOptions.
    /// </summary>
    public CommandOptions AddErrorStream(TextWriter stream)
    {
        this.ErrorStream = stream;
        return this;
    }

    /// <summary>
    /// Updates the OutputStream propery and returns the updated commandOptions.
    /// </summary>
    public CommandOptions AddOutputStream(TextWriter stream)
    {
        this.OutputStream = stream;
        return this;
    }

    /// <summary>
    /// Gets the Environment variables that will be set in the subprocess that
    /// differ from current process's environment variables.  Any time a string
    /// of the form %VAR% is found in a value of a environment variable it is
    /// replaced with the value of the environment variable at the time the
    /// Command is launched.  This is useful for example to update the PATH
    /// environment variable eg. "%PATH%;someNewPath"
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables
    {
        get
        {
            if (environmentVariables == null)
                environmentVariables = new Dictionary<string, string>();
            return environmentVariables;
        }
    }

    /// <summary>
    /// Adds the environment variable with the give value to the set of 
    /// environmetn variables to be passed to the sub-process and returns the 
    /// updated commandOptions.   Any time a string
    /// of the form %VAR% is found in a value of a environment variable it is
    /// replaced with the value of the environment variable at the time the
    /// Command is launched.  This is useful for example to update the PATH
    /// environment variable eg. "%PATH%;someNewPath"
    /// </summary>
    public CommandOptions AddEnvironmentVariable(string variable, string value)
    {
        EnvironmentVariables[variable] = value;
        return this;
    }

    // We ar friends with the Command class 

    // TODO implement
    // internal bool showCommand;          // Show the Command before running it. 

    internal bool noThrow;
    internal bool start;
    internal int timeoutMSec;
    internal string input;
    internal string outputFile;
    internal TextWriter outputStream;
    internal TextWriter errorStream;
    internal DataReceivedEventHandler outputHandler;
    internal string currentDirectory;
    internal Dictionary<string, string> environmentVariables;
};

/// <summary>
/// Command represents a running of a Command lineNumber process.  It is basically
/// a wrapper over System.Diagnostics.Process, which hides the complexitity
/// of System.Diagnostics.Process, and knows how to capture output and otherwise
/// makes calling commands very easy.
/// </summary>
public sealed class Command
{
    /// <summary>
    /// The time the process started.  
    /// </summary>
    public DateTime StartTime { get { return process.StartTime; } }

    /// <summary>
    /// returns true if the process has exited. 
    /// </summary>
    public bool HasExited { get { return process.HasExited; } }

    /// <summary>
    /// The time the processed Exited.  (HasExited should be true before calling)
    /// </summary>
    public DateTime ExitTime { get { return process.ExitTime; } }

    /// <summary>
    /// The duration of the Command (HasExited should be true before calling)
    /// </summary>
    public TimeSpan Duration { get { return ExitTime - StartTime; } }

    /// <summary>
    /// The operating system ID for the subprocess.  
    /// </summary>
    public int Id { get { return process.Id; } }

    /// <summary>
    /// The process exit code for the subprocess.  (HasExited should be true before calling)
    /// Often this does not need to be checked because Command.Run will throw an exception 
    /// if it is not zero.   However it is useful if the CommandOptions.NoThrow property 
    /// was set.  
    /// </summary>
    public int ExitCode { get { return process.ExitCode; } }

    /// <summary>
    /// The standard output and standard error output from the Command.  This
    /// is accumulated in real time so it can vary if the process is still running.
    /// 
    /// This property is NOT available if the CommandOptions.OutputFile or CommandOptions.OutputStream
    /// is specified since the output is being redirected there.   If a large amoutn of output is 
    /// expected (> 1Meg), the Run.AddOutputStream(Stream) is recommended for retrieving it since
    /// the large string is never materialized at one time. 
    /// </summary>
    public string Output
    {
        get
        {
            if (outputStream != null)
                throw new Exception("Output not available if redirected to file or stream");
            return output.ToString();
        }
    }

    /// <summary>
    /// Returns that CommandOptions structure that holds all the options that affect
    /// the running of the Command (like Timeout, Input ...)
    /// </summary>
    public CommandOptions Options { get { return options; } }

    /// <summary>
    /// Run 'commandLine', sending the output to the console, and wait for the Command to complete.
    /// This simulates what batch filedo when executing their commands.  It is a bit more verbose
    /// by default, however 
    /// </summary>
    /// <param variable="commandLine">The Command lineNumber to run as a subprocess</param>
    /// <param variable="options">Additional qualifiers that control how the process is run</param>
    /// <returns>A Command structure that can be queried to determine ExitCode, Output, etc.</returns>
    public static Command RunToConsole(string commandLine, CommandOptions options)
    {
        return Run(commandLine, options.Clone().AddOutputStream(Console.Out).AddErrorStream(Console.Error));
    }

    public static Command RunToConsole(string commandLine)
    {
        return RunToConsole(commandLine, new CommandOptions());
    }

    /// <summary>
    /// Run 'commandLine' as a subprocess and waits for the Command to complete.
    /// Output is captured and placed in the 'Output' property of the returned Command 
    /// structure. 
    /// </summary>
    /// <param variable="commandLine">The Command lineNumber to run as a subprocess</param>
    /// <param variable="options">Additional qualifiers that control how the process is run</param>
    /// <returns>A Command structure that can be queried to determine ExitCode, Output, etc.</returns>
    public static Command Run(string commandLine, CommandOptions options)
    {
        Command run = new Command(commandLine, options);
        run.Wait();
        return run;
    }
    public static Command Run(string commandLine)
    {
        return Run(commandLine, new CommandOptions());
    }

    /// <summary>
    /// Launch a new Command and returns the Command object that can be used to monitor
    /// the restult.  It does not wait for the Command to complete, however you 
    /// can call 'Wait' to do that, or use the 'Run' or 'RunToConsole' methods. */
    /// </summary>
    /// <param variable="commandLine">The Command lineNumber to run as a subprocess</param>
    /// <param variable="options">Additional qualifiers that control how the process is run</param>
    /// <returns>A Command structure that can be queried to determine ExitCode, Output, etc.</returns>
    public Command(string commandLine, CommandOptions options)
    {
        this.options = options;
        this.commandLine = commandLine;

        // See if the Command is quoted and match it in that case
        Match m = Regex.Match(commandLine, "^\\s*\"(.*?)\"\\s*(.*)");
        if (!m.Success)
            m = Regex.Match(commandLine, @"\s*(\S*)\s*(.*)");    // thing before first space is Command

        ProcessStartInfo startInfo = new ProcessStartInfo(m.Groups[1].Value, m.Groups[2].Value);
        process = new Process();
        process.StartInfo = startInfo;
        if (options.start)
        {
            startInfo.UseShellExecute = true;
        }
        else
        {
            if (options.input != null)
                startInfo.RedirectStandardInput = true;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.ErrorDialog = false;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.CreateNoWindow = true;

            output = new StringBuilder();
            if (options.OutputHandler != null)
            {
                process.OutputDataReceived += options.outputHandler;
                process.ErrorDataReceived += options.outputHandler;
            }
            else
            {
                process.OutputDataReceived += new DataReceivedEventHandler(OnProcessOutput);
                process.ErrorDataReceived += new DataReceivedEventHandler(OnProcessOutput);
            }
        }

        if (options.environmentVariables != null)
        {
            // copy over the environment variables to the process startInfo options. 
            foreach (string key in options.environmentVariables.Keys)
            {

                // look for %VAR% strings in the value and subtitute the appropriate environment variable. 
                string value = options.environmentVariables[key];
                if (value != null)
                {
                    int startAt = 0;
                    for (; ; )
                    {
                        m = new Regex(@"%(\w+)%").Match(value, startAt);
                        if (!m.Success) break;
                        string varName = m.Groups[1].Value;
                        string varValue;
                        if (startInfo.EnvironmentVariables.ContainsKey(varName))
                            varValue = startInfo.EnvironmentVariables[varName];
                        else
                        {
                            varValue = Environment.GetEnvironmentVariable(varName);
                            if (varValue == null)
                                varValue = "";
                        }
                        // replace this instance of the variable with its definition.  
                        int varStart = m.Groups[1].Index - 1;     // -1 because % chars are not in the group
                        int varEnd = varStart + m.Groups[1].Length + 2; // +2 because % chars are not in the group
                        value = value.Substring(0, varStart) + varValue + value.Substring(varEnd, value.Length - varEnd);
                        startAt = varStart + varValue.Length;
                    }
                }
                startInfo.EnvironmentVariables[key] = value;
            }
        }
        startInfo.WorkingDirectory = options.currentDirectory;

        outputStream = options.outputStream;
        if (options.outputFile != null)
        {
            outputStream = File.CreateText(options.outputFile);
        }

#if false
            if (options.showCommand && outputStream != null)
            {
                // TODO why only for output streams?
                outputStream.WriteLine("RUN CMD: " + commandLine);
            }
#endif

        try
        {
            process.Start();
        }
        catch (Exception e)
        {
            string msg = "Failure starting Process\r\n" +
                "    Exception: " + e.Message + "\r\n" +
                "    Cmd: " + commandLine + "\r\n";

            if (Regex.IsMatch(startInfo.FileName, @"^(copy|dir|del|color|set|cd|cdir|md|mkdir|prompt|pushd|popd|start|assoc|ftype)", RegexOptions.IgnoreCase))
                msg += "    Cmd " + startInfo.FileName + " implemented by Cmd.exe, fix by prefixing with 'cmd /c'.";
            throw new ApplicationException(msg, e);
        }

        if (!startInfo.UseShellExecute)
        {
            // startInfo asyncronously collecting output
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        // Send any input to the Command 
        if (options.input != null)
        {
            process.StandardInput.Write(options.input);
            process.StandardInput.Close();
        }
    }
    /// <summary>
    /// Create a subprocess to run 'commandLine' with no special options. 
    /// <param variable="commandLine">The Command lineNumber to run as a subprocess</param>
    /// </summary>
    public Command(string commandLine)
        : this(commandLine, new CommandOptions())
    {
    }

    /// <summary>
    /// Wait for a started process to complete (HasExited will be true on return)
    /// </summary>
    /// <returns>Wait returns that 'this' pointer.</returns>
    public Command Wait()
    {
        // shell execute processes you don't wait for
        if (process.StartInfo.UseShellExecute)
            return this;

        //process.WaitForExit(options.timeoutMSec);
        //WaitForExit(timeout) and async output handling don't mix.
        //Unless timeout is Int32.MaxValue, the process does not wait for the streams to drain
        //Doing Thread.Sleep() was of limited use, output was lost either way.
        process.WaitForExit();

        if (!process.HasExited)
        {
            Kill();
            throw new ApplicationException("Timeout of " + (options.timeoutMSec / 1000) + " sec exceeded\r\n    Cmd: " + commandLine);
        }

        // If we created the output stream, we should close it.  
        if (outputStream != null && options.outputFile != null)
            outputStream.Close();
        outputStream = null;

        if (process.ExitCode != 0 && !options.noThrow)
            ThrowCommandFailure(null);
        return this;
    }

    /// <summary>
    /// Throw a error if the Command exited with a non-zero exit code
    /// printing useful diagnostic information along with the thrown message.
    /// This is useful when NoThrow is specified, and after post-processing
    /// you determine that the Command really did fail, and an normal 
    /// Command.Run failure was the appropriate action.  
    /// </summary>
    /// <param name="message">An additional message to print in the throw (can be null)</param>
    public void ThrowCommandFailure(string message)
    {
        if (process.ExitCode != 0)
        {
            string outSpec = "";
            if (this.outputStream == null)
            {
                string outStr = output.ToString();

                // Only print out this many lines.  Omit the 'middle' if necessary. 
                const int maxLines = 200;
                int lineCount = 0;
                int startOmitIndex = 0; 
                while(startOmitIndex < outStr.Length)
                {
                    if (outStr[startOmitIndex] == '\n')
                    {
                        lineCount++;
                        if (lineCount >= maxLines / 2)
                        {
                            startOmitIndex++;
                            break;
                        }
                    }
                    startOmitIndex++;
                }
                lineCount = 0;
                int endOmitIndex = outStr.Length; 
                while(startOmitIndex < endOmitIndex)
                {
                    --endOmitIndex;
                    if (outStr[startOmitIndex] == '\n')
                    {
                        lineCount++;
                        if (lineCount >= maxLines / 2)
                        {
                            endOmitIndex++;
                            break;
                        }
                    }
                }
                if (startOmitIndex < endOmitIndex)
                    outStr = outStr.Substring(0, startOmitIndex) + "<<< Omitted output ... >>>\r\n" + outStr.Substring(endOmitIndex);

                // Indent the output
                outStr = outStr.Replace("\n", "\n    ");
                outSpec = "\r\n  Output: {\r\n    " + outStr + "\r\n  }";
            }
            if (message == null)
                message = "";
            else if (message.Length > 0)
                message += "\r\n";
            throw new ApplicationException(message + "Process returned exit code 0x" + process.ExitCode.ToString("x") + "\r\n" +
                                "  Cmd: " + commandLine + outSpec);
        }
    }

    /// <summary>
    /// Get the underlying process object.  Generally not used. 
    /// </summary>
    public System.Diagnostics.Process Process { get { return process; } }

    /// <summary>
    /// Kill the process (and any child processses (recursively) associated with the 
    /// running Command).   Note that it may not be able to kill everything it should
    /// if the child-parent' chain is broken by a child that creates a subprocess and
    /// then dies itself.   This is reasonably uncommon, however. 
    /// </summary>
    public void Kill(bool quiet)
    {

        // We use taskkill because it is built into windows, and knows
        // how to kill all subchildren of a process, which important. 
        // TODO (should we use WMI instead?)
        if (!quiet)
            Console.WriteLine("Killing process tree " + Id + " Cmd: " + commandLine);
        try
        {
            Command.Run("taskkill /f /t /pid " + process.Id);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        int ticks = 0;
        do
        {
            System.Threading.Thread.Sleep(10);
            ticks++;
            if (ticks > 100)
            {
                Console.WriteLine("ERROR: process is not dead 1 sec after killing " + process.Id);
                Console.WriteLine("Cmd: " + commandLine);
            }
        } while (!process.HasExited);

        // If we created the output stream, we should close it.  
        if (outputStream != null && options.outputFile != null)
            outputStream.Close();
        outputStream = null;
    }

    /// <summary>
    /// Kill the process (and any child processses (recursively) associated with the 
    /// running Command).   Note that it may not be able to kill everything it should
    /// if the child-parent' chain is broken by a child that creates a subprocess and
    /// then dies itself.   This is reasonably uncommon, however. 
    /// </summary>
    public void Kill()
    {
        Kill(false);
    }

    /* called data comes to either StdErr or Stdout */
    private void OnProcessOutput(object sender, DataReceivedEventArgs e)
    {
        if (outputStream != null)
            outputStream.WriteLine(e.Data);
        else
            output.AppendLine(e.Data);
    }

    /* private state */
    private string commandLine;
    private Process process;
    private StringBuilder output;
    private CommandOptions options;
    private TextWriter outputStream;
}
