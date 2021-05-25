using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

// See code:#Overview to get started.
/// <summary>
/// #Overview
/// 
/// The code:CommandLineParser is a utility for parsing command lines. Command lines consist of three basic
/// entities. A command can have any (or none) of the following (separated by whitespace of any size).
/// 
///    * PARAMETERS - this are non-space strings. They are positional (logicaly they are numbered). Strings
///        with space can be specified by enclosing in double quotes.
///        
///    * QUALIFERS - Qualifiers are name-value pairs. The following syntax is supported.
///        * -QUALIFER
///        * -QUALIFER:VALUE
///        * -QUALIFER=VALUE
///        * -QUALIFER VALUE
///        
///      The end of a value is delimited by space. Again values with spaces can be encoded by enclosing them
///      the value (or the whole qualifer-value string), in double quotes. The first form (where a value is
///      not specified is only available for boolean qualifers, and boolean values can not use the form where
///      the qualifer and value are separated by space. The '/' character can also be used instead of the '-'
///      to begin a qualifier.
///      
///      Unlike parameters, qualifers are NOT ordered. They may occur in any order with respect to the
///      parameters or other qualifers and THAT ORDER IS NOT COMMUNICATED THROUGH THE PARSER. Thus it is not
///      possible to have qualifers that only apply to specific parameters.
///      
///    * PARAMETER SET SPECIFIER - A parameter set is optional argument that looks like a boolean qualifier
///        (however if NoDashOnParameterSets is set the dash is not need, so it is looks like a parameter),
///        that is special in that it decides what qualifers and positional parameters are allowed. See
///        code:#ParameterSets for more
///        
/// #ParameterSets
/// 
/// Parameter sets are an OPTIONAL facility of code:CommandLineParser that allow more complex command lines
/// to be specified accurately. It not uncommon for a EXE to have several 'commands' that are logically
/// independent of one another. For example a for example For example a program might have 'checkin'
/// 'checkout' 'list' commands, and each of these commands has a different set of parameters that are needed
/// and qualifers that are allowed. (for example checkout will take a list of file names, list needs nothing,
/// and checkin needs a comment). Additionally some qualifers (like say -dataBaseName can apply to any of hte
/// commands). Thus You would like to say that the following command lines are legal
/// 
///     * EXE -checkout MyFile1 MyFile -dataBaseName:MyDatabase
///     * EXE -dataBaseName:MyDatabase -list
///     * EXE -comment "Specifying the comment first" -checkin
///     * EXE -checkin -comment "Specifying the comment afterward"
/// 
/// But the following are not
/// 
///     * EXE -checkout
///     * EXE -checkout -comment "hello"
///     * EXE -list MyFile
/// 
/// You do this by specifying 'checkout', 'list' and 'checkin' as paramters sets. On the command line they
/// look like boolean qualifers, however they have additional sematantics. They must come before any
/// positional parameters (because they affect whether the parameters are allowed and what they are named),
/// and they are mutually exclusive. Each parameter set gets its own set of parameter definitions, and
/// qualifilers can either be associated with a particular parameter set (like -comment) or global to all
/// parameter sets (like -dataBaseName) .
/// 
/// By default parameter set specifiers look like a boolean specifier (begin with a '-' or '/'), however
/// because it is common practice to NOT have a dash for commands, there there is a Property
/// code:CommandLineParser.NoDashOnParameterSets that indicates that the dash is not used. If this was
/// specified then the following command would be legal.
/// 
///     * EXE checkout MyFile1 MyFile -dataBaseName:MyDatabase
/// 
/// #DefaultParameterSet
/// 
/// One parameters set (which has the empty string name), is special in that it is used when no other
/// parmeter set is matched. This is the default parameter set. For example, if -checkout was defined to be
/// the default parameter set, then the following would be legal.
/// 
///     * EXE Myfile1 Myfile
/// 
/// And would implicitly mean 'checkout' Myfile1, Myfile2
/// 
/// If no parameter sets are defined, then all qualifiers and paramters are in the default parameter set.
/// 
/// -------------------------------------------------------------------------
/// #Syntatic ambiguities
/// 
/// Because whitespace can separate a qualifier from its value AND Qualifier from each other, and because
/// parameter values might start with a dash (and thus look like qualifers), the syntax is ambiguous. It is
/// disambigutated with the following rules.
///     * The command line is parsed into 'arguments' that are spearated by whitespace. Any string enclosed
///         in "" will be a single argument even if it has embedded whitespace. Double quote characters can
///         be specified by \" (and a \" literal can be specified by \\" etc).
///     * Arguments are parsed into qualifiers. This parsing stops if a '--' argument is found. Thus all
///         qualifers must come before any '--' argument but parameters that begin with - can be specified by
///         placing them after the '--' argument,
///     * Qualifers are parsed. Because spaces can be used to separate a qualifer from its value, the type of
///         the qualifer must be known to parse it. Boolean values never consume an additional parameter, and
///         non-boolean qualifiers ALWAYS consume the next argument (if there is no : or =). If the empty
///         string is to be specified, it must use the ':' or '=' form. Moreover it is illegal for the values
///         that begin with '-' to use space as a separator. They must instead use the ':' or '=' form. This
///         is because it is too confusing for humans to parse (values look like qualifiers).
///      * Parameters are parsed. Whatever arguments that were not used by qualifers are parameters.
/// 
/// --------------------------------------------------------------------------------------------
/// #DefiningParametersAndQualifiers
/// 
/// The following example shows the steps for defining the parameters and qualifers for the example. Note
/// that the order is important. Qualifers that apply to all commands must be specified first, then each
/// parameter set then finally the default parameter set. Most steps are optional.
///
///     class CommandLineParserExample1
///     {
///         enum Command { checkout, checkin, list };
///         static void Main()
///         {
///             string dataBaseName = "myDefaultDataBase";
///             string comment = "";
///             Command command = checkout;
///             string[] fileNames = null;
///
///             // Step 1 define the parser. 
///             CommandLineParser commandLineParser = new CommandLineParser();      // by default uses Environment.CommandLine
///
///             // Step 2 (optional) define qualifiers that apply to all parameter sets. 
///             commandLineParser.DefineOptionalParameter("dataBaseName", ref dataBaseName, "Help for database.");
///
///             // Step 3A define the checkin command this includes all parameters and qualifers specific to this command
///             commandLineParser.DefineParameterSet("checkin", ref command, Command.checkin, "Help for checkin.");
///             commandLineParser.DefineOptionalQualifers("comment", ref comment, "Help for -comment.");
///
///             // Step 3B define the list command this includes all parameters and qualifers specific to this command
///             commandLineParser.DefineParameterSet("list", ref command, Command.list, "Help for list.");
///
///             // Step 4 (optional) define the default parameter set (in this case checkout). 
///             commandLineParser.DefineDefaultParameterSet("checkout", ref command, Command.checkout, "Help for checkout.");
///             commandLineParser.DefineParamter("fileNames", ref fileNames, "Help for fileNames.");
///
///             // Step 5, do final validation (look for undefined qualifiers, extra parameters ...
///             commandLineParser.CompleteValidation();
///
///             // Step 6 use the parsed values
///             Console.WriteLine("Got {0} {1} {2} {3} {4}", dataBaseName, command, comment, string.Join(',', fileNames));
///         }
///     }
///
/// #RequiredAndOptional
/// 
/// Parameters and qualifiers can be specified as required (the default), or optional. Makeing the default
/// required was choses to make any mistakes 'obvious' since the parser will fail if a required parameter is
/// not present (if the default was optional, it would be easy to make what should have been a required
/// qualifer optional, leading to business logic failiure).
/// 
/// #ParsedValues
/// 
/// The class was designed maximize programmer convinience. For each parameter, only one call is needed to
/// both define the parameter, its help message, and retrive its (strong typed) value. For example
/// 
///      * int count = 5;
///      * parser.DefineOptionalQualifer("count", ref count, "help for optional debugs qualifer");
/// 
/// Defines a qualifer 'count' and will place its value in the local variable 'count' as a integer. Default
/// values are supported by doing nothing, so in the example above the default value will be 5.
/// 
/// Types supported: The parser will support any type that has a static method called 'Parse' taking one
/// string argument and returning that type. This is true for all primtive types, DateTime, Enumerations, and
/// any user defined type that follows this convention.
/// 
/// Array types: The parser has special knowedge of arrays. If the type of a qualifer is an array, then the
/// string value is assumed to be a ',' separated list of strings which will be parsed as the element type of
/// the array. In addition to the ',' syntax, it is also legal to specify the qualifer more than once. For
/// example given the defintion
/// 
///      * int[] counts;
///      * parser.DefineOptionalQualifer("counts", ref counts, "help for optional counts qualifier");
///      
/// The command line
/// 
///     * EXE -counts 5 SomeArg -counts 6 -counts:7
/// 
/// Is the same as
/// 
///     * EXE -counts:5,6,7 SomeArg
///      
/// If a qualifier or parameter is an array type and is required, then the array must have at least one
/// element. If it is optional, then the array can be empty (but in all cases, the array is created, thus
/// null is never returned by the command line parser).
/// 
/// By default is it is illegal for a non-array qualifer to be specified more than once. It is however
/// possible to override this behavior by setting the LastQualiferWins property before defining the qualifer.
/// 
/// -------------------------------------------------------------------------
/// #Misc
/// 
/// Qualifier can have more than one string form (typically a long and a short form). These are specified
/// with the code:DefineAliases method.
/// 
/// After defining all the qualifers and parameters, it is necessary to call the parser to check for the user
/// specifying a qualifer (or parameter) that does not exist. This is the purpose of the
/// code:CompleteValidation method.
/// 
/// When an error is detected at runtime an instance of code:CommandLineParserException is thrown. The error
/// message in this exception was designed to be suitable to print to the user directly.
/// 
/// #CommandLineHelp
/// 
/// The parser also can generate help that is correct for the qualifer and parameter definitions. This can be
/// accessed from the code:CommandLineParser.GetHelp method. It is also possible to get the help for just a
/// particular Parameter set with code:CommandLineParser.GetHelpForParameterSet. This help includes command
/// line syntax, whether the qualifer or parameter is optional or a list, the types of the qualifers and
/// parameters, the help text, and default values.   The help text comes from the 'Define' Methods, and is
/// properly word-wrapped.  Newlines in the help text indicate new paragraphs.   
/// 
/// #AutomaticExceptionProcessingAndHelp
/// 
/// In the CommandLineParserExample1, while the command line parser did a lot of the work there is still work
/// needed to make the application user friendly that pretty much all applications need. These include
///  
///     * Call the code:CommandLineParser constructor and code:CommandLineParser.CompleteValidation
///     * Catch any code:CommandLineParserException and print a friendly message
///     * Define a -? qualifer and wire it up to print the help.
/// 
/// Since this is stuff that all applications will likely need the
/// code:CommandLineParser.ParseForConsoleApplication was created to do all of this for you, thus making it
/// super-easy to make a production quality parser (and concentrate on getting your application logic instead
/// of command line parsing. Here is an example which defines a 'Ping' command. If you will notice there are
/// very few lines of code that are not expressing something very specific to this applications. This is how
/// it should be!
///
///     class CommandLineParserExample2
///     {
///         static void Main()
///         {
///             CommandLineParser.ParseForConsoleApplication(delegate(CommandLineParser commandLineParser)
///             {
///                 // Step 1: Initialize to the defaults
///                 string Host = null;       
///                 int Timeout = 1000;
///                 bool Forever = false;
///
///                 // Step 2: Define the paramters, in this case there is only the default parameter set. 
///                 CommandLineParser.ParseForConsoleApplication(args, delegate(CommandLineParser parser)
///                 {
///                     parser.DefineOptionalQualifier("Timeout", ref Timeout, "Timeout in milliseconds to wait for each reply.");
///                     parser.DefineOptionalQualifier("Forever", ref Forever, "Ping forever.");
///                     parser.DefineDefaultParameterSet("Ping sends a network request to a host to reply to the message (to check for liveness).");
///                     parser.DefineParameter("Host", ref Host, "The Host to send a ping message to.");
///                 });
///
///                 // Step 3, use the parameters
///                 Console.WriteLine("Got {0} {1} {2} {3}", Host, Timeout, Forever);
///             });
///         }
///     }
///
/// Using local variables for the parsed arguments if fine when the program is not complex and the values
/// don't need to be passed around to many routines.  In general, however it is often a better idea to
/// create a class whose sole purpose is to act as a repository for the parsed arguments.   This also nicely
/// separates all command line processing into a single class.   This is how the ping example would look  in
/// that style. Notice that the main program no longer holds any command line processing logic.  and that
/// 'commandLine' can be passed to other routines in bulk easily.  
///
///     class CommandLineParserExample3
///     {
///         static void Main()
///         {
///             CommandLine commandLine = new CommandLine();
///             Console.WriteLine("Got {0} {1} {2} {3}", commandLine.Host, commandLine.Timeout, commandLine.Forever);
///         }
///     }
///     class CommandLine
///     {
///         public CommandLine()
///         {
///             CommandLineParser.ParseForConsoleApplication(args, delegate(CommandLineParser parser)
///             {
///                 parser.DefineOptionalQualifier("Timeout", ref Timeout, "Timeout in milliseconds to wait for each reply.");
///                 parser.DefineOptionalQualifier("Forever", ref Forever, "Ping forever.");
///                 parser.DefineDefaultParameterSet("Ping sends a network request to a host to reply to the message (to check for liveness).");
///                 parser.DefineParameter("Host", ref Host, "The Host to send a ping message to.");
///             });
///         }
///         public string Host = null;
///         public int Timeout = 1000;
///         public bool Forever = false;
///     };
///
/// see code:#Overview for more 
/// </summary>
public class CommandLineParser
{
    // TODO response files.
    /// <summary>
    /// If you are building a console Application, there is a common structure to parsing arguments. You want
    /// the text formated and output for console windows, and you want /? to be wired up to do this. All
    /// errors should be caught and displayed in a nice way.
    /// </summary>
    /// <param name="parseBody">parseBody is the body of the parsing that this outer shell does not provide.
    /// in this delegate, you should be defining all the command line parameters using calls to Define* methods.
    ///  </param>
    public static void ParseForConsoleApplication(Action<CommandLineParser> parseBody)
    {
        try
        {
            bool help = false;
            CommandLineParser parser = new CommandLineParser(Environment.CommandLine);
            parser.DefineOptionalQualifier("?", ref help, "Print this help guide.");

            // We treat help as a paramter set (so required arguments are not needed when /? is specified);
            if (help)
            {
                parser.skipParameterSets = true;
                parser.skipDefinitions = true;
            }
            parseBody(parser);
            if (help)
            {
                // Print the help.  We allow  <parameterSet> /? and /? <parameterSet> to limit help 
                string helpString = "";
                int maxLineWidth = Console.WindowWidth - 1;
                if (parser.args.Count == 2)
                {
                    string parameterSet = ((parser.args[0] != null) ? parser.args[0] : parser.args[1]);
                    if (parameterSet != null)
                    {
                        if (parser.IsQualifier(parameterSet))
                        {
                            parameterSet = parameterSet.Substring(1);     // remove the -
                        }

                        helpString = parser.GetHelpForParameterSet(parameterSet, true, maxLineWidth);
                    }
                }
                if (helpString.Length == 0)
                {
                    helpString = parser.GetHelp(maxLineWidth);
                }

                ShowHelp(helpString);
                Environment.Exit(0);
            }
            parser.CompleteValidation();
        }
        catch (CommandLineParserException e)
        {
            Console.WriteLine("Error: " + e.Message);
            Console.WriteLine("Use -? for help.");
            Environment.Exit(1);
        }
    }

    private static void ShowHelp(string helpString)
    {
        // TODO we do paging, but this is not what we want when it is redirected.  
        bool first = true;
        for (int pos = 0; ;)
        {
            int nextPos = pos;
            int numLines = (first ? Console.WindowHeight : Console.WindowHeight * 3 / 4) - 1;
            first = false;
            for (int j = 0; j < numLines; j++)
            {
                int search = helpString.IndexOf("\r\n", nextPos) + 2;
                if (search >= 2)
                {
                    nextPos = search;
                }
                else
                {
                    nextPos = helpString.Length;
                }
            }

            Console.Write(helpString.Substring(pos, nextPos - pos));
            if (nextPos == helpString.Length)
            {
                break;
            }

            Console.Write("[Press space to continue...]");
            Console.ReadKey();
            Console.Write("\r                               \r");
            pos = nextPos;
        }
    }
    /// <summary>
    /// Qualifiers are command line parameters of the form -NAME:VALUE where NAME is an alphanumeric name and
    /// VALUE is a string. The parser also accepts -NAME: VALUE and -NAME VALUE but not -NAME : VALUE For
    /// boolan parameters, the VALUE can be dropped (which means true), and a empty string VALUE means false.
    /// Thus -NAME means the same as -NAME:true and -NAME: means the same as -NAME:false (and boolean
    /// qualifiers DONT allow -NAME true or -NAME false).
    /// 
    /// The types that are supported are any type that has a static 'Parse' function that takes a string
    /// (this includes all primitive types as well as DateTime, and Enumerations, as well as arrays of
    /// parsable types (values are comma separated without space).
    /// 
    /// See code:#DefiningParametersAndQualifiers
    /// See code:#Overview 
    /// <param name="helpText">Text to print for this qualifer.  It will be word-wrapped.  Newlines indicate
    /// new paragraphs.</param>
    /// </summary>
    public void DefineOptionalQualifier<T>(string name, ref T retVal, string helpText)
    {
        object obj = DefineQualifier(name, typeof(T), retVal, helpText, false);
        if (obj != null)
        {
            retVal = (T)obj;
        }
    }
    /// <summary>
    /// Like code:DeclareOptionalQualifier except it is an error if this parameter is not on the command line. 
    /// <param name="helpText">Text to print for this qualifer.  It will be word-wrapped.  Newlines indicate
    /// new paragraphs.</param> 
    /// </summary>
    public void DefineQualifier<T>(string name, ref T retVal, string helpText)
    {
        object obj = DefineQualifier(name, typeof(T), retVal, helpText, true);
        if (obj != null)
        {
            retVal = (T)obj;
        }
    }
    /// <summary>
    /// DefineParameter declares an unnamed parameter (basically any parameter that is not a
    /// qualifier). These are given ordinal numbers (starting at 0). You should declare the parameter in the
    /// desired order.
    /// 
    /// See code:#DefiningParametersAndQualifiers
    /// See code:#Overview 
    /// <param name="helpText">Text to print for this qualifer.  It will be word-wrapped.  Newlines indicate
    /// new paragraphs.</param> 
    /// </summary>
    public void DefineParameter<T>(string name, ref T retVal, string helpText)
    {
        object obj = DefinePositionalParameter(name, typeof(T), retVal, helpText, true);
        if (obj != null)
        {
            retVal = (T)obj;
        }
    }
    /// <summary>
    /// Like code:DeclareParameter except it is an error if this parameter is not on the command line. 
    /// These must come after non-optional (required) parameters. 
    /// <param name="helpText">Text to print for this qualifer.  It will be word-wrapped.  Newlines indicate
    /// new paragraphs.</param> 
    /// </summary>
    public void DefineOptionalParameter<T>(string name, ref T retVal, string helpText)
    {
        object obj = DefinePositionalParameter(name, typeof(T), retVal, helpText, false);
        if (obj != null)
        {
            retVal = (T)obj;
        }
    }
    /// <summary>
    /// A parameter set defines on of a set of 'commands' that decides how to parse the rest of the command
    /// line.   If this 'command' is present on the command line then 'val' is assigned to 'retVal'. 
    /// Typically 'retVal' is a variable of a enumerated type (one for each command), and 'val' is one
    /// specific value of that enumeration.  
    /// 
    /// * See code:#ParameterSets
    /// * See code:#DefiningParametersAndQualifiers
    /// * See code:#Overview 
    /// <param name="helpText">Text to print for this qualifer.  It will be word-wrapped.  Newlines indicate
    /// new paragraphs.</param> 
    /// </summary>
    public void DefineParameterSet<T>(string name, ref T retVal, T val, string helpText)
    {
        if (DefineParameterSet(name, helpText))
        {
            retVal = val;
        }
    }
    /// <summary>
    /// There is one special parameter set called the default parameter set (whose names is empty) which is
    /// used when a command line does not have one of defined parameter sets. It is always present, even if
    /// this method is not called, so alling this method is optional, however, by calling this method you can
    /// add help text for this case.  If present this call must be AFTER all other parameter set
    /// definitions. 
    /// 
    /// * See code:#DefaultParameterSet 
    /// * See code:#DefiningParametersAndQualifiers
    /// * See code:#Overview
    /// <param name="helpText">Text to print for this qualifer.  It will be word-wrapped.  Newlines indicate
    /// new paragraphs.</param> 
    /// </summary>
    public void DefineDefaultParameterSet(string helpText)
    {
        DefineParameterSet("", helpText);
    }
    /// <summary>
    /// This variation of DefineDefaultParameterSet has a 'retVal' and 'val' parameters.  If the command
    /// line does not match any of the other parameter set defintions, then 'val' is asigned to 'retVal'. 
    /// Typically 'retVal' is a variable of a enumerated type and 'val' is a value of that type.    
    /// 
    /// * See code:DefineDefaultParameterSet for more.
    /// <param name="helpText">Text to print for this qualifer.  It will be word-wrapped.  Newlines indicate
    /// new paragraphs.</param> 
    /// </summary>
    public void DefineDefaultParameterSet<T>(ref T retVal, T val, string helpText)
    {
        if (DefineParameterSet("", helpText))
        {
            retVal = val;
        }
    }
    /// <summary>
    /// Specify additional aliases for an named parameter.  This call must come BEFORE the definition, since
    /// the defintion is the operation that causes the parsing to happen.  
    /// </summary>
    public void DefineAliases(string officalName, params string[] alaises)
    {
        // TODO assert that aliases are defined before the Definition.  
        // TODO confirm no ambiguities (same alias used again).  
        if (aliasDefinitions != null && aliasDefinitions.ContainsKey(officalName))
        {
            throw new CommandLineParserDesignerException("Named parameter " + officalName + " already has been given aliases.");
        }

        if (aliasDefinitions == null)
        {
            aliasDefinitions = new Dictionary<string, string[]>();
        }

        aliasDefinitions.Add(officalName, alaises);
    }

    /// <summary>
    /// By default parameter set specifiers must look like a qualifer (begin with a -), however setting
    /// code:NoDashOnParameterSets will define a parameter set marker not to have any special prefix (just
    /// the name itself.  
    /// </summary>
    public bool NoDashOnParameterSets
    {
        get { return noDashOnParameterSets; }
        set
        {
            if (noDashOnParameterSets != value)
            {
                ThrowIfNotFirst("NoDashOnParameterSets");
            }

            noDashOnParameterSets = value;
        }
    }
    // TODO decide if we should keep this...
    public bool NoSpaceOnQualifierValues
    {
        get { return noSpaceOnQualifierValues; }
        set
        {
            if (noSpaceOnQualifierValues != value)
            {
                ThrowIfNotFirst("NoSpaceOnQualifierValues");
            }

            noSpaceOnQualifierValues = value;
        }
    }
    /// <summary>
    /// If the positional parameters might look like named parameters (typically happens when the tail of the
    /// command line is literal text), it is useful to stop the search for named parameters at the first
    /// positional parameter.  This property enables this behavior for the current parameter set. 
    /// </summary>
    public bool QualifiersMustBeFirst
    {
        get { return qualifiersMustBeFirst; }
        set
        {
            if (qualifiersMustBeFirst != value)
            {
                ThrowIfNotFirst("QualifiersMustBeFirst");
            }

            NoSpaceOnQualifierValues = true;
            qualifiersMustBeFirst = value;
        }
    }
    /// <summary>
    /// By default qualifers may being with a - or a / character.   Setting code:QualifiersUseOnlyDash will
    /// make / invalid qualifer marker (only - can be used)
    /// </summary>
    public bool QualifiersUseOnlyDash
    {
        get { return qualifiersUseOnlyDash; }
        set
        {
            if (qualifiersUseOnlyDash != value)
            {
                ThrowIfNotFirst("OnlyDashForQualifiers");
            }

            qualifiersUseOnlyDash = value;
        }
    }
    /// <summary>
    /// By default, a non-list qualifier can not be specified more than once (since one or the other will
    /// have to be ignored.  Normally an error is thrown.  Setting code:LastQualiferWins makes it legal, and
    /// the last qualifer is the one that is used.  
    /// </summary>
    public bool LastQualiferWins
    {
        get { return lastQualiferWins; }
        set { lastQualiferWins = value; }
    }

    // These routines are typically are not needed because ParseArgsForConsoleApp does the work.  
    public CommandLineParser() : this(Environment.CommandLine) { }
    public CommandLineParser(string commandLine)
    {
        helpNeeded = true;
        this.commandLine = commandLine;
    }
    public CommandLineParser(string[] args)
    {
        helpNeeded = true;
        this.args.AddRange(args);
    }

    /// <summary>
    /// Check for any paramters that the user specified but that were not defined by a Define*Parameter call
    /// and throw an exception if any are found. 
    /// </summary>
    public void CompleteValidation()
    {
        // Check if there are any undefined parameters or ones specified twice.  
        foreach (int encodedPos in dashedParameterEncodedPositions.Values)
        {
            throw new CommandLineParserException("Unexpected qualifier: " + args[GetPosition(encodedPos)] + ".");
        }
        if (paramSetEncountered && !foundParameterSet)
        {
            throw new CommandLineParserException("Could not find parameter set specifier.");
        }

        while (curPosition < args.Count && args[curPosition] == null)
        {
            curPosition++;
        }

        if (curPosition < args.Count)
        {
            throw new CommandLineParserException("Extra positional parameter: " + args[curPosition] + ".");
        }
    }
    /// <summary>
    /// Return a string giving the help for the command, word wrapped at 'maxLineWidth' 
    /// </summary>
    public string GetHelp(int maxLineWidth)
    {
        // Do we have non-default parameter sets?
        bool hasParamSets = false;
        foreach (CommandLineParameter parameter in parameterDescriptions)
        {
            if (parameter.IsParameterSet && parameter.Name != "")
            {
                hasParamSets = true;
            }
        }

        if (!hasParamSets)
        {
            return GetHelpForParameterSet("", true, maxLineWidth);
        }

        StringBuilder sb = new StringBuilder();

        string appName = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().ManifestModule.Name);
        string intro = "The " + appName + " application has a number of commands associated with it, " +
            "each with its own set of parameters and qualifiers.  They are listed below.  " +
            "Options that are common to all commands are listed at the end.";
        Wrap(sb, intro, 0, "", maxLineWidth);

        // Always print the default parameter set first.  
        if (defaultParamSetEncountered)
        {
            sb.AppendLine().Append('-', maxLineWidth - 1).AppendLine();
            sb.Append(GetHelpForParameterSet("", false, maxLineWidth));
        }

        foreach (CommandLineParameter parameter in parameterDescriptions)
        {
            if (parameter.IsParameterSet && parameter.Name.Length != 0)
            {
                sb.AppendLine().Append('-', maxLineWidth - 1).AppendLine();
                sb.Append(GetHelpForParameterSet(parameter.Name, false, maxLineWidth));
            }
        }

        string globalQualifiers = GetHelpGlobalQualifiers(maxLineWidth);
        if (globalQualifiers.Length > 0)
        {
            sb.Append('-', maxLineWidth - 1).AppendLine();
            sb.Append("Qualifiers global to all commands:").AppendLine();
            sb.AppendLine();
            sb.Append(globalQualifiers);
        }

        return sb.ToString();
    }
    /// <summary>
    /// Return the string representing the help for a single paramter set.  If displayGlobalQualifiers is
    /// true than qualifers that apply to all parameter sets is also included, otheriwse it is just the
    /// parameters and qualifers that are specific to that parameters set.  
    /// </summary>
    public string GetHelpForParameterSet(string parameterSetName, bool displayGlobalQualifiers, int maxLineWidth)
    {
        Debug.Assert(helpNeeded);

        // Find the beginning of the parameter set paramters, as well as the end of the global parameters
        // (Which come before any paramters set). 
        int parameterSetBody = 0;         // Points at body of the parameter set (paramters after the parameter set.
        CommandLineParameter parameterSetParameter = null;
        for (int i = 0; i < parameterDescriptions.Count; i++)
        {
            CommandLineParameter parameter = parameterDescriptions[i];
            if (parameter.IsParameterSet)
            {
                parameterSetParameter = parameter;
                if (string.Compare(parameterSetParameter.Name, parameterSetName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    parameterSetBody = i + 1;
                    break;
                }
            }
        }

        if (parameterSetBody == 0 && parameterSetName != "")
        {
            return "";
        }

        // At his point parameterSetBody and globalParametersEnd are properly set. Start generating strings
        StringBuilder sb = new StringBuilder();

        // Create the 'Usage' line;
        string appName = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().ManifestModule.Name);
        sb.Append("Usage: ").Append(appName);
        if (parameterSetName.Length > 0)
        {
            sb.Append(' ');
            sb.Append(parameterSetParameter.Syntax(false, false));
        }

        bool hasQualifiers = false;
        bool hasParameters = false;
        for (int i = parameterSetBody; i < parameterDescriptions.Count; i++)
        {
            CommandLineParameter parameter = parameterDescriptions[i];
            if (parameter.IsParameterSet)
            {
                break;
            }

            if (parameter.IsPositional)
            {
                hasParameters = true;
                sb.Append(' ').Append(parameter.Syntax(false, false));
            }
            else
            {
                hasQualifiers = true;
            }
        }
        sb.AppendLine();

        // Print the help for the command itself.  
        if (parameterSetParameter != null && !string.IsNullOrEmpty(parameterSetParameter.HelpText))
        {
            sb.AppendLine();
            if (parameterSetParameter != null && parameterSetParameter.HelpText != null)
            {
                Wrap(sb.Append("  "), parameterSetParameter.HelpText, 2, "  ", maxLineWidth);
            }
        }

        if (hasParameters)
        {
            sb.AppendLine().Append("  Parameters:").AppendLine();
            for (int i = parameterSetBody; i < parameterDescriptions.Count; i++)
            {
                CommandLineParameter parameter = parameterDescriptions[i];
                if (parameter.IsParameterSet)
                {
                    break;
                }

                if (parameter.IsPositional)
                {
                    ParameterHelp(parameter, sb, QualifierSyntaxWidth, maxLineWidth);
                }
            }
        }

        string globalQualifiers = null;
        if (displayGlobalQualifiers)
        {
            globalQualifiers = GetHelpGlobalQualifiers(maxLineWidth);
        }

        if (hasQualifiers || !string.IsNullOrEmpty(globalQualifiers))
        {
            sb.AppendLine().Append("  Qualifiers:").AppendLine();
            for (int i = parameterSetBody; i < parameterDescriptions.Count; i++)
            {
                CommandLineParameter parameter = parameterDescriptions[i];
                if (parameter.IsParameterSet)
                {
                    break;
                }

                if (parameter.IsNamed)
                {
                    ParameterHelp(parameter, sb, QualifierSyntaxWidth, maxLineWidth);
                }
            }
            if (globalQualifiers != null)
            {
                sb.Append(globalQualifiers);
            }
        }

        return sb.ToString();
    }
    #region private
    private class CommandLineParameter
    {
        public string Name { get { return name; } }
        public Type Type { get { return type; } }
        public object DefaultValue { get { return defaultValue; } }
        public bool IsRequired { get { return isRequired; } }
        public bool IsPositional { get { return isPositional; } }
        public bool IsNamed { get { return !isPositional; } }
        public bool IsParameterSet { get { return isParameterSet; } }
        public string HelpText { get { return helpText; } }
        public override string ToString()
        {
            return "<CommandLineParameter " +
                "Name=\"" + name + "\" " +
                "Type=\"" + type + "\" " +
                "IsRequired=\"" + IsRequired + "\" " +
                "IsPositional=\"" + IsPositional + "\" " +
                "HelpText=\"" + HelpText + "\"/>";
        }
        public string Syntax(bool printType, bool printDefaultValue)
        {
            string ret = name;
            if (IsNamed)
            {
                ret = "-" + ret;
            }

            if (printType)
            {
                // We print out arrays with the ... notiation, so we don't want the [] when we display the type
                Type displayType = Type;
                if (displayType.IsArray)
                {
                    displayType = displayType.GetElementType();
                }

                bool shouldPrint = true;
                // Bool type implied on named parameters
                if (IsNamed && type == typeof(bool))
                {
                    shouldPrint = false;
                    if (defaultValue != null && (bool)defaultValue)
                    {
                        shouldPrint = false;
                    }
                }
                // string type is implied on positional parameters
                if (IsPositional && displayType == typeof(string) && string.IsNullOrEmpty(defaultValue as string))
                {
                    shouldPrint = false;
                }

                if (shouldPrint)
                {

                    ret = ret + (IsNamed ? ":" : ".") + displayType.Name.ToUpper();
                    if (printDefaultValue && defaultValue != null)
                    {
                        string defValue = defaultValue.ToString();
                        if (defValue.Length < 40)   // TODO is this reasonable?
                        {
                            ret += "(" + defValue + ")";
                        }
                    }
                }
            }

            if (Type.IsArray)
            {
                ret = ret + (IsNamed ? "," : " ") + "...";
            }

            if (!IsRequired)
            {
                ret = "[" + ret + "]";
            }

            return ret;
        }

        #region private
        internal CommandLineParameter(string Name, object defaultValue, string helpText, Type type,
            bool isRequired, bool isPositional, bool isParameterSet)
        {
            name = Name;
            this.defaultValue = defaultValue;
            this.type = type;
            this.helpText = helpText;
            this.isRequired = isRequired;
            this.isPositional = isPositional;
            this.isParameterSet = isParameterSet;
        }

        private string name;
        private object defaultValue;
        private string helpText;
        private Type type;
        private bool isRequired;
        private bool isPositional;
        private bool isParameterSet;
        #endregion
    }

    private void ThrowIfNotFirst(string propertyName)
    {
        if (namedArgEncountered || positionalArgEncountered || paramSetEncountered)
        {
            throw new CommandLineParserDesignerException("The property " + propertyName + " can only be set before any calls to Define* Methods.");
        }
    }
    private static bool IsDash(char c)
    {
        // can be any of ASCII dash (0x002d), endash (0x2013), emdash (0x2014) or hori-zontal bar (0x2015).
        return c == '-' || ('\x2013' <= c && c <= '\x2015');
    }
    private bool IsQualifier(string arg)
    {
        if (arg.Length < 1)
        {
            return false;
        }

        if (IsDash(arg[0]))
        {
            return true;
        }

        if (!qualifiersUseOnlyDash && arg[0] == '/')
        {
            return true;
        }

        return false;
    }
    private string ParseParameterName(string arg)
    {
        string ret = null;
        if (arg != null && IsQualifier(arg))
        {
            int endName = arg.IndexOfAny(separators);
            if (endName < 0)
            {
                endName = arg.Length;
            }

            ret = arg.Substring(1, endName - 1);
        }
        return ret;
    }
    /// <summary>
    /// Find the locations of all arguments that look like named parameters. 
    /// </summary>
    private void PreParse()
    {
        if (args == null)
        {
            ParseWords();
        }

        dashedParameterEncodedPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg == null)
            {
                continue;
            }

            string name = ParseParameterName(arg);
            if (name != null)
            {
                if (name.Length == 1 && IsDash(name[0]))
                {
                    args[i] = null;
                    break;
                }
                int position = i;
                if (dashedParameterEncodedPositions.TryGetValue(name, out position))
                {
                    position = SetMulitple(position);
                }
                else
                {
                    position = i;
                }

                dashedParameterEncodedPositions[name] = position;
            }
            else if (qualifiersMustBeFirst)
            {
                break;
            }
        }
    }

    private void ParseWords()
    {
        // TODO review this carefully.
        args = new List<string>();
        int wordStartIndex = -1;
        bool hasExcapedQuotes = false;
        int numQuotes = 0;
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '"')
            {
                numQuotes++;
                if (wordStartIndex < 0)
                {
                    wordStartIndex = i;
                }

                i++;
                for (; ; )
                {
                    if (i >= commandLine.Length)
                    {
                        throw new CommandLineParserException("Unmatched quote at position " + i + ".");
                    }

                    c = commandLine[i];
                    if (c == '"')
                    {
                        if (i > 0 && commandLine[i - 1] == '\\')
                        {
                            hasExcapedQuotes = true;
                        }
                        else
                        {
                            break;
                        }
                    }
                    i++;
                }
            }
            else if (c == ' ' || c == '\t')
            {
                if (wordStartIndex >= 0)
                {
                    AddWord(wordStartIndex, i, numQuotes, hasExcapedQuotes);
                    wordStartIndex = -1;
                    hasExcapedQuotes = false;
                    numQuotes = 0;
                }
            }
            else
            {
                if (wordStartIndex < 0)
                {
                    wordStartIndex = i;
                }
            }
        }
        if (wordStartIndex > 0)
        {
            AddWord(wordStartIndex, commandLine.Length, numQuotes, hasExcapedQuotes);
        }
    }

    private void AddWord(int wordStartIndex, int wordEndIndex, int numQuotes, bool hasExcapedQuotes)
    {
        if (!readExe)
        {
            readExe = true;
            return;
        }
        string word;
        if (numQuotes > 0)
        {
            // Common case, the whole word is quoted, and no escaping happened.   
            if (!hasExcapedQuotes && numQuotes == 1 && commandLine[wordStartIndex] == '"' && commandLine[wordEndIndex - 1] == '"')
            {
                word = commandLine.Substring(wordStartIndex + 1, wordEndIndex - wordStartIndex - 2);
            }
            else
            {
                // Remove "" (except for quoted quotes!)
                StringBuilder sb = new StringBuilder();
                for (int i = wordStartIndex; i < wordEndIndex;)
                {
                    char c = commandLine[i++];
                    if (c != '"')
                    {
                        if (c == '\\' && i < wordEndIndex && commandLine[i] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }
                }
                word = sb.ToString();
            }
        }
        else
        {
            word = commandLine.Substring(wordStartIndex, wordEndIndex - wordStartIndex);
        }

        args.Add(word);
    }

    private int GetNextOccuranceQualifier(string name, string[] aliases)
    {
        if (dashedParameterEncodedPositions == null)
        {
            PreParse();
        }

        int ret = -1;
        string match = null;
        int encodedPos;
        if (dashedParameterEncodedPositions.TryGetValue(name, out encodedPos))
        {
            match = name;
            ret = GetPosition(encodedPos);
        }

        if (aliases != null)
        {
            foreach (string alias in aliases)
            {
                int aliasEncodedPos;
                if (dashedParameterEncodedPositions.TryGetValue(name, out aliasEncodedPos))
                {
                    int aliasPos = GetPosition(aliasEncodedPos);
                    if (aliasPos < ret)
                    {
                        name = alias;
                        encodedPos = aliasEncodedPos;
                        ret = aliasPos;
                    }
                }
            }
        }

        if (match != null)
        {
            if (!IsMulitple(encodedPos))
            {
                dashedParameterEncodedPositions.Remove(match);
            }
            else
            {
                int nextPos = -1;
                for (int i = ret + 1; i < args.Count; i++)
                {
                    if (string.Compare(ParseParameterName(args[i]), name, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        nextPos = i;
                        break;
                    }
                }
                if (nextPos >= 0)
                {
                    dashedParameterEncodedPositions[name] = SetMulitple(nextPos);
                }
                else
                {
                    dashedParameterEncodedPositions.Remove(name);
                }
            }
        }
        return ret;
    }

    private object DefineQualifier(string name, Type type, object defaultValue, string helpText, bool isRequired)
    {
        if (args == null)
        {
            ParseWords();
        }

        namedArgEncountered = true;
        if (helpNeeded)
        {
            AddHelp(new CommandLineParameter(name, defaultValue, helpText, type, isRequired, false, false));
        }

        if (skipDefinitions)
        {
            return null;
        }

        if (positionalArgEncountered && !noSpaceOnQualifierValues)
        {
            throw new CommandLineParserDesignerException("Definitions of Named parameters must come before defintions of positional parameters");
        }

        object ret = null;
        string[] alaises = null;
        if (aliasDefinitions != null)
        {
            aliasDefinitions.TryGetValue(name, out alaises);
        }

        int occuranceCount = 0;
        List<Array> arrayValues = null;
        for (; ; )
        {
            int position = GetNextOccuranceQualifier(name, alaises);
            if (position < 0)
            {
                break;
            }

            string parameterStr = args[position];
            args[position] = null;

            string value = null;
            int colonIdx = parameterStr.IndexOfAny(separators);
            if (colonIdx >= 0)
            {
                value = parameterStr.Substring(colonIdx + 1);
            }

            if (type == typeof(bool))
            {
                if (value == null)
                {
                    value = "true";
                }
                else if (value == "")
                {
                    value = "false";
                }
            }
            else if (value == null)
            {
                int valuePos = position + 1;
                if (noSpaceOnQualifierValues || valuePos >= args.Count || args[valuePos] == null)
                {
                    string message = "Parameter " + name + " is missing a value.";
                    if (noSpaceOnQualifierValues)
                    {
                        message += "  The syntax -" + name + ":<value> must be used.";
                    }

                    throw new CommandLineParserException(message);
                }
                value = args[valuePos];

                // Note that I don't absolutely need to exclude values that look like qualifers, but it does
                // complicate the code, and also leads to confusing error messages when we parse the command
                // in a very different way then the user is expecting. Since you can provide values that
                // begin with a '-' by doing -qualifer:-value instead of -qualifier -value I force the issue
                // by excluding it here.  TODO: this makes negative numbers harder... 
                if (value.Length > 0 && IsQualifier(value))
                {
                    throw new CommandLineParserException("Use " + name + ":" + value + " if " + value +
                        " is meant to be value rather than a named parameter");
                }

                args[valuePos] = null;
            }
            ret = ParseValue(value, type, name);
            if (type.IsArray)
            {
                if (arrayValues == null)
                {
                    arrayValues = new List<Array>();
                }

                arrayValues.Add((Array)ret);
                ret = null;
            }
            else if (occuranceCount > 0 && !lastQualiferWins)
            {
                throw new CommandLineParserException("Parameter " + name + " specified more than once.");
            }

            occuranceCount++;
        }

        if (occuranceCount == 0 && isRequired)
        {
            throw new CommandLineParserException("Required named parameter " + name + " not present.");
        }

        if (arrayValues != null)
        {
            ret = ConcatinateArrays(type, arrayValues);
        }

        return ret;
    }

    private object DefinePositionalParameter(string name, Type type, object defaultValue, string helpText, bool isRequired)
    {
        if (args == null)
        {
            ParseWords();
        }

        if (!isRequired)
        {
            optionalPositionalArgEncountered = true;
        }
        else if (optionalPositionalArgEncountered)
        {
            throw new CommandLineParserDesignerException("Optional positional parameters can't precede required positional parameters");
        }

        positionalArgEncountered = true;
        if (helpNeeded)
        {
            AddHelp(new CommandLineParameter(name, defaultValue, helpText, type, isRequired, true, false));
        }

        if (skipDefinitions)
        {
            return null;
        }

        // Skip any nulled out args (things that used to be named parameters)
        while (curPosition < args.Count && args[curPosition] == null)
        {
            curPosition++;
        }

        object ret = null;
        if (type.IsArray)
        {
            // Pass 1, Get the count
            int count = 0;
            int argPosition = curPosition;
            while (argPosition < args.Count)
            {
                if (args[argPosition++] != null)
                {
                    count++;
                }
            }

            if (count == 0 && isRequired)
            {
                throw new CommandLineParserException("Required positional parameter " + name + " not present.");
            }

            Type elementType = type.GetElementType();
            Array array = Array.CreateInstance(elementType, count);
            argPosition = curPosition;
            int index = 0;
            while (argPosition < args.Count)
            {
                string arg = args[argPosition++];
                if (arg != null)
                {
                    array.SetValue(ParseValue(arg, elementType, name), index++);
                }
            }
            curPosition = args.Count;
            ret = array;
        }
        else if (curPosition < args.Count)     // A 'normal' positional parameter with a value
        {
            ret = ParseValue(args[curPosition++], type, name);
        }
        else // No value
        {
            if (isRequired)
            {
                throw new CommandLineParserException("Required positional parameter " + name + " not present.");
            }
        }

        return ret;
    }
    private bool DefineParameterSet(string name, string helpText)
    {
        if (args == null)
        {
            ParseWords();
        }

        if (!paramSetEncountered && positionalArgEncountered)
        {
            throw new CommandLineParserDesignerException("Positional paramters must not precede paramter set definitions.");
        }

        paramSetEncountered = true;
        positionalArgEncountered = false;               // each parameter set gets a new arg set   
        optionalPositionalArgEncountered = false;
        if (defaultParamSetEncountered)
        {
            throw new CommandLineParserDesignerException("The default parameter set must be defined last.");
        }

        bool isDefaultParameterSet = (name.Length == 0);
        if (isDefaultParameterSet)
        {
            defaultParamSetEncountered = true;
        }

        if (helpNeeded)
        {
            AddHelp(new CommandLineParameter(name, null, helpText, typeof(bool), true, noDashOnParameterSets, true));
        }

        if (skipParameterSets)
        {
            return false;
        }

        // Have we just finish with the parameter set that was actually on the command line?
        if (foundParameterSet)
        {
            skipDefinitions = true;
            skipParameterSets = true;       // if yes, we are done, ignore all parameter set definitions. 
            return false;
        }

        bool ret = isDefaultParameterSet;
        if (!isDefaultParameterSet)
        {
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (arg == null)
                {
                    continue;
                }

                if (IsQualifier(arg))
                {
                    if (!noDashOnParameterSets &&
                        string.Compare(arg, 1, name, 0, int.MaxValue, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        dashedParameterEncodedPositions.Remove(name);
                        args[i] = null;
                        ret = true;
                        break;
                    }
                }
                else
                {
                    if (noDashOnParameterSets && (string.Compare(arg, name, StringComparison.OrdinalIgnoreCase) == 0))
                    {
                        args[i] = null;
                        ret = true;
                    }
                    break;
                }
            }
        }

        foundParameterSet = ret;
        skipDefinitions = !foundParameterSet;
        return ret;
    }

    private Array ConcatinateArrays(Type arrayType, List<Array> arrays)
    {
        int totalCount = 0;
        for (int i = 0; i < arrays.Count; i++)
        {
            totalCount += arrays[i].Length;
        }

        Type elementType = arrayType.GetElementType();
        Array ret = Array.CreateInstance(elementType, totalCount);
        int pos = 0;
        for (int i = 0; i < arrays.Count; i++)
        {
            Array source = arrays[i];
            for (int j = 0; j < source.Length; j++)
            {
                ret.SetValue(source.GetValue(j), pos++);
            }
        }
        return ret;
    }

    private object ParseValue(string valueString, Type type, string parameterName)
    {
        try
        {
            if (type == typeof(string))
            {
                return valueString;
            }
            else if (type == typeof(bool))
            {
                return bool.Parse(valueString);
            }
            else if (type == typeof(int))
            {
                if (valueString.Length > 2 && valueString[0] == '0' && (valueString[1] == 'x' || valueString[1] == 'X'))
                {
                    return int.Parse(valueString.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier);
                }
                else
                {
                    return int.Parse(valueString);
                }
            }
            else if (type.IsEnum)
            {
                return Enum.Parse(type, valueString, true);
            }
            else if (type == typeof(string[]))
            {
                return valueString.Split(',');
            }
            else if (type.IsArray)
            {
                // TODO I need some way of handling string with , in them.  
                Type elementType = type.GetElementType();
                string[] elementStrings = valueString.Split(',');
                Array array = Array.CreateInstance(elementType, elementStrings.Length);
                for (int i = 0; i < elementStrings.Length; i++)
                {
                    array.SetValue(ParseValue(elementStrings[i], elementType, parameterName), i);
                }

                return array;
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (valueString.Length == 0)
                {
                    return null;
                }

                return ParseValue(valueString, type.GetGenericArguments()[0], parameterName);
            }
            else
            {
                System.Reflection.MethodInfo parseMethod = type.GetMethod("Parse", new Type[] { typeof(string) });
                if (parseMethod == null)
                {
                    throw new CommandLineParserException("Could not find a parser for type " + type.Name + " for parameter " + parameterName);
                }

                return parseMethod.Invoke(null, new object[] { valueString });
            }
        }
        catch (Exception e)
        {
            if (e is System.Reflection.TargetInvocationException)
            {
                e = e.InnerException;
            }

            string paramStr = "";
            if (parameterName != null)
            {
                paramStr = " for parameter " + parameterName;
            }

            if (type.IsEnum && e is ArgumentException)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("The value '").Append(valueString).Append("'").Append(paramStr).Append(" is not a member of the enumeration ").Append(type.Name).Append(".").AppendLine();
                sb.Append("The legal values are either a decimal integer or:").AppendLine();
                foreach (string name in Enum.GetNames(type))
                {
                    sb.Append("    ").Append(name).AppendLine();
                }

                throw new CommandLineParserException(sb.ToString());
            }
            else if (e is FormatException)
            {
                throw new CommandLineParserException("The value '" + valueString + "' can not be parsed to a " + type.Name + paramStr + ".");
            }
            else
            {
                throw new CommandLineParserException("Failure while converting '" + valueString + "' to a " + type.Name + paramStr + ".");
            }
        }
    }

    private void AddHelp(CommandLineParameter parameter)
    {
        if (parameterDescriptions == null)
        {
            parameterDescriptions = new List<CommandLineParameter>();
        }

        parameterDescriptions.Add(parameter);
    }
    private static void ParameterHelp(CommandLineParameter parameter, StringBuilder sb, int firstColumnWidth, int maxLineWidth)
    {
        // TODO alias information. 
        sb.Append("    ").Append(parameter.Syntax(true, true).PadRight(firstColumnWidth)).Append(' ');
        string helpText = parameter.HelpText;
        if (typeof(Enum).IsAssignableFrom(parameter.Type))
        {
            helpText = helpText + "  Legal values: " + string.Join(", ", Enum.GetNames(parameter.Type)) + ".";
        }

        Wrap(sb, helpText, firstColumnWidth + 5, new string(' ', firstColumnWidth + 5), maxLineWidth);
    }
    private static void Wrap(StringBuilder sb, string text, int startColumn, string linePrefix, int maxLineWidth)
    {
        if (text != null)
        {
            bool first = true;
            int column = startColumn;
            string previousWord = "";
            string[] paragraphs = text.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string paragraph in paragraphs)
            {
                if (!first)
                {
                    sb.AppendLine().AppendLine();
                    column = 0;
                }
                string[] words = paragraph.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);        // Split on whitespace
                foreach (string word in words)
                {
                    if (column + word.Length > maxLineWidth)
                    {
                        sb.AppendLine();
                        column = 0;
                    }
                    if (column == 0)
                    {
                        sb.Append(linePrefix);
                        column = linePrefix.Length;
                    }
                    else if (!first)
                    {
                        // add an extra space at the end of sentences. 
                        if (previousWord.Length > 0 && previousWord[previousWord.Length - 1] == '.')
                        {
                            sb.Append(' ');
                            column++;
                        }
                        sb.Append(' ');
                        column++;
                    }
                    sb.Append(word);
                    previousWord = word;
                    column += word.Length;
                    first = false;
                }
            }
        }
        sb.AppendLine();
    }
    private string GetHelpGlobalQualifiers(int maxLineWidth)
    {
        if (!paramSetEncountered)
        {
            return "";
        }

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < parameterDescriptions.Count; i++)
        {
            CommandLineParameter parameter = parameterDescriptions[i];
            if (parameter.IsParameterSet)
            {
                break;
            }

            if (parameter.IsNamed)
            {
                ParameterHelp(parameter, sb, QualifierSyntaxWidth, maxLineWidth);
            }
        }
        return sb.ToString();
    }


    // TODO expose the ability to change this?
    private static char[] separators = new char[] { ':', '=' };

    // tweeks the user can specify
    private bool noDashOnParameterSets;
    private bool noSpaceOnQualifierValues;
    private bool qualifiersMustBeFirst;
    private bool qualifiersUseOnlyDash;
    private bool lastQualiferWins;

    private bool helpNeeded;

    // In order to produce help, we need to remember everything useful about all the paramters.  This list
    // does this.  
    private List<CommandLineParameter> parameterDescriptions;
    private int qualifierSyntaxWidth;
    public int QualifierSyntaxWidth
    {
        get
        {
            // Find the optimize size for hte 'first column' of the help text. 
            if (qualifierSyntaxWidth == 0)
            {
                qualifierSyntaxWidth = 35;
                if (parameterDescriptions != null)
                {
                    int maxSyntaxWidth = 0;
                    foreach (CommandLineParameter parameter in parameterDescriptions)
                    {
                        maxSyntaxWidth = Math.Max(maxSyntaxWidth, parameter.Syntax(true, true).Length);
                    }

                    qualifierSyntaxWidth = Math.Max(8, maxSyntaxWidth + 1); // +1 leaves an extra space
                }
            }
            return qualifierSyntaxWidth;
        }
    }

    private Dictionary<string, string[]> aliasDefinitions;       // May be null.  

    // We steal the top bit to prepresent whether the parameter occurs more than once. 
    private const int MultiplePositions = unchecked((int)0x80000000);
    private static int SetMulitple(int encodedPos) { return encodedPos | MultiplePositions; }
    private static int GetPosition(int encodedPos) { return encodedPos & ~MultiplePositions; }
    private static bool IsMulitple(int encodedPos) { return (encodedPos & MultiplePositions) != 0; }

    // Because we do all the parsing for a single parameter at once, it is useful to know quickly if the
    // parameter even exists, exists once, or exists more than once.  This dictionary holds this.  It is
    // initialized in code:PreParseQualifiers
    private Dictionary<string, int> dashedParameterEncodedPositions;
    // As we parse named parameters we remove them from the command line by nulling them out.  This arrays
    // starts out as the argument list and ends up only having positional parameters non-null. 
    private List<string> args;
    private string commandLine;
    // As we scan positional paramters, this variable keeps track of where we are. 
    private int curPosition;
    // The syntax supports '--' which indicate that only positional parameters are after that point. 
    // 'maxNamedIndex' points at this point (or args.Length if -- does not occur).  
    private bool skipParameterSets;             // Have we found the parameter set qualifer. 
    private bool skipDefinitions;               // Should we skip all subsequent definitions? 
    private bool foundParameterSet;             // Have we found the parameter set
    private bool readExe;

    private bool paramSetEncountered;
    private bool defaultParamSetEncountered;
    private bool positionalArgEncountered;
    private bool optionalPositionalArgEncountered;
    private bool namedArgEncountered;
    #endregion
}

/// <summary>
/// Run time parsing error throw this exception.   These are expected to be caught and the error message
/// printed out to the user.   Thus the messages should be 'user friendly'.  
/// </summary>
public class CommandLineParserException : ApplicationException
{
    public CommandLineParserException(string message) : base(message) { }
}

/// <summary>
/// This exception represents a compile time error in the command line parsing.  These should not happen in
/// correctly written programs.
/// </summary>
public class CommandLineParserDesignerException : Exception
{
    public CommandLineParserDesignerException(string message) : base(message) { }
}

