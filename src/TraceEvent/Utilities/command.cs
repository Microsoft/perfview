/****************************************************************************/
/*                                  Command.cs                                  */
/****************************************************************************/


/* AUTHOR: Vance Morrison
 * Date  : 11/3/2005  */
/****************************************************************************/

using Microsoft.Diagnostics.Tracing.Compatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;               // for StackTrace; Process
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities
{

    /// <summary>
    /// CommandOptions is a helper class for the Command class.  It stores options
    /// that affect the behavior of the execution of ETWCommands and is passes as a 
    /// parameter to the constructor of a Command.  
    /// 
    /// It is useful for these options be be on a separate class (rather than 
    /// on Command itself), because it is reasonably common to want to have a set
    /// of options passed to several commands, which is not easily possible otherwise. 
    /// </summary>
#if COMMAND_PUBLIC
    public
#endif
    sealed class CommandOptions
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
        /// Return a copy an existing set of command options
        /// </summary>
        /// <returns>The copy of the command options</returns>
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
        /// <returns>Updated command options</returns>
        /// </summary>
        public CommandOptions AddNoThrow()
        {
            noThrow = true;
            return this;
        }

        /// <summary>
        /// ShortHand for UseShellExecute and NoWait
        /// </summary>
        public bool Start { get { return useShellExecute; } set { useShellExecute = value; noWait = value; } }

        /// <summary>
        /// Updates the Start propery and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddStart()
        {
            Start = true;
            return this;
        }

        /// <summary>
        /// Normally commands are launched with CreateProcess.  However it is
        /// also possible use the Shell Start API.  This causes Command to look
        /// up the executable differently 
        /// </summary>
        public bool UseShellExecute { get { return useShellExecute; } set { useShellExecute = value; } }

        /// <summary>
        /// Updates the Start propery and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddUseShellExecute()
        {
            useShellExecute = true;
            return this;
        }

        /// <summary>
        /// Indicates that you want to hide any new window created.  
        /// </summary>
        public bool NoWindow { get { return noWindow; } set { noWindow = value; } }

        /// <summary>
        /// Updates the NoWindow propery and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddNoWindow()
        {
            noWindow = true;
            return this;
        }

        /// <summary>
        /// Indicates that you want don't want to wait for the command to complete.
        /// </summary>
        public bool NoWait { get { return noWait; } set { noWait = value; } }

        /// <summary>
        /// Updates the NoWait propery and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddNoWait()
        {
            noWait = true;
            return this;
        }

        /// <summary>
        /// Indicates that the command must run at elevated Windows privledges (causes a new command window)
        /// </summary>
        public bool Elevate { get { return elevate; } set { elevate = value; } }

        /// <summary>
        /// Updates the Elevate propery and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddElevate()
        {
            elevate = true;
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
        /// CommandOptions.Infinite can be used for infinite
        /// </summary>
        public CommandOptions AddTimeout(int milliseconds)
        {
            timeoutMSec = milliseconds;
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
            currentDirectory = directoryPath;
            return this;
        }

        // TODO add a capability to return a enumerator of output lines. (and/or maybe a delegate callback)

        /// <summary>
        /// Indicates the standard output and error of the command should be redirected
        /// to a archiveFile rather than being stored in Memory in the 'Output' property of the
        /// command.
        /// </summary>
        public string OutputFile
        {
            get { return outputFile; }
            set
            {
                if (outputStream != null)
                {
                    throw new Exception("OutputFile and OutputStream can not both be set");
                }

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
        /// Indicates the standard output and error of the command should be redirected
        /// to a a TextWriter rather than being stored in Memory in the 'Output' property 
        /// of the command.
        /// </summary>
        public TextWriter OutputStream
        {
            get { return outputStream; }
            set
            {
                if (outputFile != null)
                {
                    throw new Exception("OutputFile and OutputStream can not both be set");
                }

                outputStream = value;
            }
        }

        /// <summary>
        /// Updates the OutputStream property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddOutputStream(TextWriter outputStream)
        {
            OutputStream = outputStream;
            return this;
        }

        /// <summary>
        /// Gets the Environment variables that will be set in the subprocess that
        /// differ from current process's environment variables.  Any time a string
        /// of the form %VAR% is found in a value of a environment variable it is
        /// replaced with the value of the environment variable at the time the
        /// command is launched.  This is useful for example to update the PATH
        /// environment variable eg. "%PATH%;someNewPath"
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables
        {
            get
            {
                if (environmentVariables == null)
                {
                    environmentVariables = new Dictionary<string, string>();
                }

                return environmentVariables;
            }
        }

        /// <summary>
        /// Adds the environment variable with the give value to the set of 
        /// environmetn variables to be passed to the sub-process and returns the 
        /// updated commandOptions.   Any time a string
        /// of the form %VAR% is found in a value of a environment variable it is
        /// replaced with the value of the environment variable at the time the
        /// command is launched.  This is useful for example to update the PATH
        /// environment variable eg. "%PATH%;someNewPath"
        /// </summary>
        public CommandOptions AddEnvironmentVariable(string variable, string value)
        {
            EnvironmentVariables[variable] = value;
            return this;
        }

        // We ar friends with the Command class 

        // TODO implement
        // internal bool showCommand;          // Show the command before running it. 

        internal bool noThrow;
        internal bool useShellExecute;
        internal bool noWindow;
        internal bool noWait;
        internal bool elevate;
        internal int timeoutMSec;
        internal string input;
        internal string outputFile;
        internal TextWriter outputStream;
        internal string currentDirectory;
        internal Dictionary<string, string> environmentVariables;
    };

    /// <summary>
    /// Command represents a running of a command lineNumber process.  It is basically
    /// a wrapper over System.Diagnostics.Process, which hides the complexitity
    /// of System.Diagnostics.Process, and knows how to capture output and otherwise
    /// makes calling commands very easy.
    /// </summary>
#if COMMAND_PUBLIC
    public
#endif
    sealed class Command
    {
        /// <summary>
        /// The time the process started.  
        /// </summary>
        public DateTime StartTime { get { return process.StartTime; } }

        /// <summary>
        /// Returns true if the process has exited. 
        /// </summary>
        public bool HasExited { get { return process.HasExited; } }

        /// <summary>
        /// The time the processed Exited.  (HasExited should be true before calling)
        /// </summary>
        public DateTime ExitTime { get { return process.ExitTime; } }

        /// <summary>
        /// The duration of the command (HasExited should be true before calling)
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
        /// The standard output and standard error output from the command.  This
        /// is accumulated in real time so it can vary if the process is still running.
        /// 
        /// This property is NOT available if the CommandOptions.OutputFile or CommandOptions.OutputStream
        /// is specified since the output is being redirected there.   If a large amount of output is 
        /// expected (> 1Meg), the Run.AddOutputStream(Stream) is recommended for retrieving it since
        /// the large string is never materialized at one time. 
        /// </summary>
        public string Output
        {
            get
            {
                if (outputStream != null)
                {
                    throw new InvalidOperationException("Output not available if redirected to file or stream");
                }

                return output.ToString();
            }
        }

        /// <summary>
        /// Returns that CommandOptions structure that holds all the options that affect
        /// the running of the command (like Timeout, Input ...)
        /// </summary>
        public CommandOptions Options { get { return options; } }

        /// <summary>
        /// Run 'commandLine', sending the output to the console, and wait for the command to complete.
        /// This simulates what batch filedo when executing their commands.  It is a bit more verbose
        /// by default, however 
        /// </summary>
        /// <param variable="commandLine">The command lineNumber to run as a subprocess</param>
        /// <param variable="options">Additional qualifiers that control how the process is run</param>
        /// <returns>A Command structure that can be queried to determine ExitCode, Output, etc.</returns>
        public static Command RunToConsole(string commandLine, CommandOptions options = null)
        {
            if (options == null)
            {
                options = new CommandOptions();
            }
            else
            {
                options = options.Clone();
            }

            return Run(commandLine, options.AddOutputStream(Console.Out));
        }


        /// <summary>
        /// Run 'commandLine' as a subprocess and waits for the command to complete.
        /// Output is captured and placed in the 'Output' property of the returned Command 
        /// structure. 
        /// </summary>
        /// <param variable="commandLine">The command lineNumber to run as a subprocess</param>
        /// <param variable="options">Additional qualifiers that control how the process is run</param>
        /// <returns>A Command structure that can be queried to determine ExitCode, Output, etc.</returns>
        public static Command Run(string commandLine, CommandOptions options = null)
        {
            if (options == null)
            {
                options = new CommandOptions();
            }
            else
            {
                options = options.Clone();
            }

            Command run = new Command(commandLine, options);
            run.Wait();
            return run;
        }


        /// <summary>
        /// Launch a new command and returns the Command object that can be used to monitor
        /// the restult.  It does not wait for the command to complete, however you 
        /// can call 'Wait' to do that, or use the 'Run' or 'RunToConsole' methods. */
        /// </summary>
        /// <param variable="commandLine">The command lineNumber to run as a subprocess</param>
        /// <param variable="options">Additional qualifiers that control how the process is run</param>
        /// <returns>A Command structure that can be queried to determine ExitCode, Output, etc.</returns>
        public Command(string commandLine, CommandOptions options)
        {
            this.options = options;
            this.commandLine = commandLine;

            // See if the command is quoted and match it in that case
            Match m = Regex.Match(commandLine, "^\\s*\"(.*?)\"\\s*(.*)");
            if (!m.Success)
            {
                m = Regex.Match(commandLine, @"\s*(\S*)\s*(.*)");    // thing before first space is command
            }

            ProcessStartInfo startInfo = new ProcessStartInfo(m.Groups[1].Value, m.Groups[2].Value);
            process = new Process();
            process.StartInfo = startInfo;
            output = new StringBuilder();

            if (options.elevate)
            {
#if NETSTANDARD1_6
                throw new NotImplementedException("Launching elevated processes is not implemented when TraceEvent is built for NetStandard 1.6");
#else
                options.useShellExecute = true;
                startInfo.Verb = "runas";
                if (options.currentDirectory == null)
                {
                    options.currentDirectory = Directory.GetCurrentDirectory();
                }
#endif
            }

            startInfo.CreateNoWindow = options.noWindow;
            if (options.useShellExecute)
            {
                startInfo.UseShellExecute = true;
#if ! NETSTANDARD1_6
                if (options.noWindow)
                {
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                }
#endif
            }
            else
            {
                if (options.input != null)
                {
                    startInfo.RedirectStandardInput = true;
                }

                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardError = true;
                startInfo.RedirectStandardOutput = true;
#if ! NETSTANDARD1_6
                startInfo.ErrorDialog = false;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
#endif
                startInfo.CreateNoWindow = true;

                process.OutputDataReceived += new DataReceivedEventHandler(OnProcessOutput);
                process.ErrorDataReceived += new DataReceivedEventHandler(OnProcessOutput);
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
                            if (!m.Success)
                            {
                                break;
                            }

                            string varName = m.Groups[1].Value;
                            string varValue;
                            if (startInfo.GetEnvironment().ContainsKey(varName))
                            {
                                varValue = startInfo.GetEnvironment()[varName];
                            }
                            else
                            {
                                varValue = Environment.GetEnvironmentVariable(varName);
                                if (varValue == null)
                                {
                                    varValue = "";
                                }
                            }
                            // replace this instance of the variable with its definition.  
                            int varStart = m.Groups[1].Index - 1;     // -1 because % chars are not in the group
                            int varEnd = varStart + m.Groups[1].Length + 2; // +2 because % chars are not in the group
                            value = value.Substring(0, varStart) + varValue + value.Substring(varEnd, value.Length - varEnd);
                            startAt = varStart + varValue.Length;
                        }
                    }
                    startInfo.GetEnvironment()[key] = value;
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
                {
                    msg += "    Cmd " + startInfo.FileName + " implemented by Cmd.exe, fix by prefixing with 'cmd /c'.";
                }

                throw new ApplicationException(msg, e);
            }

            if (!startInfo.UseShellExecute)
            {
                // startInfo asyncronously collecting output
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            // Send any input to the command 
            if (options.input != null)
            {
                process.StandardInput.Write(options.input);
                process.StandardInput.Dispose();
            }
        }
        /// <summary>
        /// Create a subprocess to run 'commandLine' with no special options. 
        /// <param variable="commandLine">The command lineNumber to run as a subprocess</param>
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
            // we where told not to wait
            if (options.noWait)
            {
                return this;
            }

            bool waitReturned = false;
            bool killed = false;
            try
            {
                process.WaitForExit(options.timeoutMSec);
                waitReturned = true;
                //  TODO : HACK we see to have a race in the async process stuff
                //  If you do Run("cmd /c set") you get truncated output at the
                //  Looks like the problem in the framework.  
                for (int i = 0; i < 10; i++)
                {
                    System.Threading.Thread.Sleep(1);
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    killed = true;
                    Kill();
                }
            }

            // If we created the output stream, we should close it.  
            if (outputStream != null && options.outputFile != null)
            {
                outputStream.Dispose();
            }

            outputStream = null;

            if (waitReturned && killed)
            {
                throw new Exception("Timeout of " + (options.timeoutMSec / 1000) + " sec exceeded\r\n    Cmd: " + commandLine);
            }

            if (process.ExitCode != 0 && !options.noThrow)
            {
                ThrowCommandFailure(null);
            }

            return this;
        }

        /// <summary>
        /// Throw a error if the command exited with a non-zero exit code
        /// printing useful diagnostic information along with the thrown message.
        /// This is useful when NoThrow is specified, and after post-processing
        /// you determine that the command really did fail, and an normal 
        /// Command.Run failure was the appropriate action.  
        /// </summary>
        /// <param name="message">An additional message to print in the throw (can be null)</param>
        public void ThrowCommandFailure(string message)
        {
            if (process.ExitCode != 0)
            {
                string outSpec = "";
                if (outputStream == null)
                {
                    string outStr = output.ToString();
                    // Only show the first lineNumber the last two lines if there are a lot of output. 
                    Match m = Regex.Match(outStr, @"^(\s*\n)?(.+\n)(.|\n)*?(.+\n.*\S)\s*$");
                    if (m.Success)
                    {
                        outStr = m.Groups[2].Value + "    <<< Omitted output ... >>>\r\n" + m.Groups[4].Value;
                    }
                    else
                    {
                        outStr = outStr.Trim();
                    }
                    // Indent the output
                    outStr = outStr.Replace("\n", "\n    ");
                    outSpec = "\r\n  Output: {\r\n    " + outStr + "\r\n  }";
                }
                if (message == null)
                {
                    message = "";
                }
                else if (message.Length > 0)
                {
                    message += "\r\n";
                }

                throw new Exception(message + "Process returned exit code 0x" + process.ExitCode.ToString("x") + "\r\n" +
                                    "  Cmd: " + commandLine + outSpec);
            }
        }

        /// <summary>
        /// Get the underlying process object.  Generally not used. 
        /// </summary>
        public System.Diagnostics.Process Process { get { return process; } }

        /// <summary>
        /// Kill the process (and any child processses (recursively) associated with the 
        /// running command).   Note that it may not be able to kill everything it should
        /// if the child-parent' chain is broken by a child that creates a subprocess and
        /// then dies itself.   This is reasonably uncommon, however. 
        /// </summary>
        public void Kill()
        {

            // We use taskkill because it is built into windows, and knows
            // how to kill all subchildren of a process, which important. 
            // TODO (should we use WMI instead?)
            // Console.WriteLine("Killing process tree " + Id + " Cmd: " + commandLine);
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
#if DEBUG
                if (ticks > 100)
                {
                    Console.WriteLine("ERROR: process is not dead 1 sec after killing " + process.Id);
                    Console.WriteLine("Cmd: " + commandLine);
                }
#endif
            } while (!process.HasExited);

            // If we created the output stream, we should close it.  
            if (outputStream != null && options.outputFile != null)
            {
                outputStream.Dispose();
            }

            outputStream = null;
        }

        /// <summary>
        /// Put double quotes around 'str' if necessary (handles quotes quotes.  
        /// </summary>
        public static string Quote(string str)
        {
            if (str.IndexOf('"') < 0)
            {
                // Replace any " with \"  (and any \" with \\" and and \\" with \\\"  ...)
                str = Regex.Replace(str, "\\*\"", @"\$1");
            }
            return "\"" + str + "\"";
        }

        /// <summary>
        /// Given a string 'commandExe' look for it on the path the way cmd.exe would.   
        /// Returns null if it was not found.   
        /// </summary>
        public static string FindOnPath(string commandExe)
        {
            string ret = ProbeForExe(commandExe);
            if (ret != null)
            {
                return ret;
            }

            if (!commandExe.Contains("\\"))
            {
                foreach (string path in Paths)
                {
                    string baseExe = Path.Combine(path, commandExe);
                    ret = ProbeForExe(baseExe);
                    if (ret != null)
                    {
                        return ret;
                    }
                }
            }
            return null;
        }

        #region private
        private static string ProbeForExe(string path)
        {
            if (File.Exists(path))
            {
                return path;
            }

            foreach (string ext in PathExts)
            {
                string name = path + ext;
                if (File.Exists(name))
                {
                    return name;
                }
            }
            return null;
        }

        private static string[] PathExts
        {
            get
            {
                if (pathExts == null)
                {
                    pathExts = Environment.GetEnvironmentVariable("PATHEXT").Split(';');
                }

                return pathExts;
            }
        }
        private static string[] pathExts;
        private static string[] Paths
        {
            get
            {
                if (paths == null)
                {
                    paths = Environment.GetEnvironmentVariable("PATH").Split(';');
                }

                return paths;
            }
        }
        private static string[] paths;
        #endregion

        #region private
        /* called data comes to either StdErr or Stdout */
        private void OnProcessOutput(object sender, DataReceivedEventArgs e)
        {
            if (outputStream != null)
            {
                outputStream.WriteLine(e.Data);
            }
            else
            {
                output.AppendLine(e.Data);
            }
        }

        /* private state */
        private string commandLine;
        private Process process;
        private StringBuilder output;
        private CommandOptions options;
        private TextWriter outputStream;
        #endregion
    }
}

