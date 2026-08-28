namespace Tabsh;

internal sealed class Shell
{
    private const string _historyFileName = "history.txt";
    private const string _startupFileName = "startup.tabsh";
    private const string _dataDirectoryName = "Tabsh";

    private readonly Executor _executor;

    // how many times Ctrl+C has been pressed at the command now running, counted so the second one can insist.
    private int _interrupts;

    // whether the question below is already on screen, since console events do not wait their turn.
    private int _asking;

    public Shell()
    {
        Builtins = new BuiltinTable(this);
        Completer = new Completer(this);
        Editor = new LineEditor(this);
        _executor = new Executor(this);
    }

    public Executor Executor => _executor;
    public ShellEnvironment Environment { get; } = new();
    public AliasTable Aliases { get; } = new();
    public BuiltinTable Builtins { get; }
    public Completer Completer { get; }
    public LineEditor Editor { get; }
    public bool ExitRequested { get; private set; }
    public int ExitCode { get; private set; }

    public static string HistoryPath => DataPath(_historyFileName);

    // not a batch language, a list of lines each run as if it had been typed.
    public static string StartupPath => DataPath(_startupFileName);

    private static string DataPath(string fileName) => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        _dataDirectoryName,
        fileName);

    public void RunStartupFile()
    {
        string[] lines;
        try
        {
            if (!File.Exists(StartupPath))
                return;

            lines = File.ReadAllLines(StartupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            ExecuteLine(trimmed);
        }
    }

    public void RequestExit(int code)
    {
        ExitRequested = true;
        ExitCode = code;
    }

    // Windows delivers the same event to the command, so the first Ctrl+C is left to it and only the second insists.
    // Ctrl+Break, the harder of the two, does not wait.
    private void OnInterrupt(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;

        if (e.SpecialKey != ConsoleSpecialKey.ControlBreak && Interlocked.Increment(ref _interrupts) <= 1)
            return;

        // one question at a time. An interrupt arriving while it is being asked is the same question.
        if (Interlocked.CompareExchange(ref _asking, 1, 0) != 0)
            return;

        try
        {
            Interrupt();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // no console to ask through, and nothing here is worth killing on a guess.
        }
        finally
        {
            Interlocked.Exchange(ref _asking, 0);
        }
    }

    private void Interrupt()
    {
        var children = _executor.RunningChildren();
        if (children.Count == 0)
            return;

        // asked for now rather than remembered, what a command started can only be seen while it is still there.
        var processes = children.SelectMany(child => child.Running()).ToList();
        if (processes.Count == 0)
            return;

        if (!Confirm(processes, children.All(child => child.TracksTree)))
            return;

        foreach (var child in children)
        {
            child.Terminate();
        }
    }

    // the command is still running and may still be reading the console, so it can take the answer meant for this,
    // in which case nothing is killed and the interrupt can be repeated.
    private static bool Confirm(List<JobProcess> processes, bool tracksTree)
    {
        // a screen of its own, or what the command goes on printing scrolls the list away before it can be read.
        using var screen = ConsoleScreen.Open();

        void writeLine(string text = "")
        {
            if (screen != null)
            {
                screen.WriteLine(text);
            }
            else
            {
                Console.WriteLine(text);
            }
        }

        // a fresh screen needs no room made at the top of it, the shell's own output is not on it.
        if (screen == null)
        {
            writeLine();
        }

        writeLine(processes.Count == 1 ? Res.OneProcessRunning : string.Format(CultureInfo.CurrentCulture, Res.ProcessesRunning, processes.Count));

        foreach (var process in processes)
        {
            writeLine(string.Format(CultureInfo.CurrentCulture, Res.ProcessLine, process.ProcessId, process.Description));
        }

        // with no job behind it the list is the command alone, and a short list would imply it is the whole story.
        if (!tracksTree)
        {
            writeLine(Res.TreeCannotBeKilled);
        }

        // a script has nobody to ask, and an interrupt there means what it says.
        if (Console.IsInputRedirected)
            return true;

        writeLine();
        if (screen != null)
        {
            screen.Write(Res.TerminatePrompt);
        }
        else
        {
            Console.Write(Res.TerminatePrompt);
        }

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var answer = key.KeyChar.ToString();
            if (string.Equals(answer, Res.YesKey, StringComparison.CurrentCultureIgnoreCase))
            {
                writeLine(Res.YesKey);
                return true;
            }

            if (string.Equals(answer, Res.NoKey, StringComparison.CurrentCultureIgnoreCase) || key.Key is ConsoleKey.Escape or ConsoleKey.Enter)
            {
                writeLine(Res.NoKey);
                return false;
            }
        }
    }

    public int Run()
    {
        // a script arriving on a pipe is not somebody's typing, so it neither reads the history nor writes to it.
        var typed = !Console.IsInputRedirected;
        if (typed)
        {
            Editor.History.Load(HistoryPath);
        }

        Console.CancelKeyPress += OnInterrupt;

        try
        {
            while (!ExitRequested)
            {
                var line = ReadLine();
                if (line == null)
                    break;

                if (line.Trim().Length == 0)
                    continue;

                Editor.History.Add(line);
                ExecuteLine(line);

                // cmd compatible, and it keeps the prompt off the last line of a program that ended without a newline.
                // Nothing is written for a line that ran nothing, so Enter on an empty prompt leaves no gap.
                Console.WriteLine();
            }
        }
        finally
        {
            // the entries went out one at a time as they were entered, and this is what removes their repeats.
            if (typed)
            {
                Editor.History.Save(HistoryPath);
            }
        }

        return ExitCode;
    }

    private string? ReadLine()
    {
        var prompt = Environment.FormatPrompt();

        // a redirected input has no keys to read, so it is taken a line at a time and nothing is drawn.
        if (Console.IsInputRedirected)
        {
            Console.Write(prompt);
            return Console.ReadLine();
        }

        Environment.ApplyTitle();
        return Editor.ReadLine(prompt);
    }

    // a byte order mark is a marker, not a character. A piped script that carries one would fail its first line.
    private static string StripByteOrderMark(string line) => line.TrimStart('﻿');

    public int ExecuteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        line = Aliases.Expand(StripByteOrderMark(line));
        if (line.Trim().Length == 0)
            return Environment.LastExitCode;

        // each command is interrupted on its own account, so an unrelated Ctrl+C from earlier does not count towards it.
        Interlocked.Exchange(ref _interrupts, 0);

        SequenceNode sequence;
        try
        {
            sequence = CommandParser.Parse(line, Environment.Resolve);
        }
        catch (CommandSyntaxException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Environment.LastExitCode = 1;
            return 1;
        }

        if (TryChangeDirectory(sequence, out var code))
            return code;

        return _executor.Execute(sequence, StandardHandles.FromConsole());
    }

    // GetFullPath normalises "cd..\.." back to the current directory, "cd.." being a segment the ".." then pops,
    // so Directory.Exists says yes for a word naming nothing at all.
    private bool FirstSegmentExists(string word)
    {
        var end = word.IndexOfAny(['\\', '/']);
        var first = end < 0 ? word : word[..end];

        // a rooted or drive relative word has no first segment to check, and "." and ".." are always there.
        if (first.Length == 0 || first == "." || first == ".." || (first.Length == 2 && first[1] == ':'))
            return true;

        try
        {
            return Directory.Exists(Path.GetFullPath(first, Environment.CurrentDirectory));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // a line that is nothing but the name of a directory goes there, but only when the word cannot mean anything else,
    // so a directory called "dir" cannot take over the command that lists it.
    private bool TryChangeDirectory(SequenceNode sequence, out int code)
    {
        code = 0;
        if (sequence.Items.Count != 1)
            return false;

        var pipeline = sequence.Items[0].Pipeline;
        if (pipeline.Commands.Count != 1 || pipeline.Commands[0] is not SimpleCommand command)
            return false;

        if (command.Words.Count != 1 || command.Redirections.Count != 0)
            return false;

        var word = command.Words[0];
        if (word.Length == 0 || Builtins.Find(word) != null)
            return false;

        // in the namespace a bare name is a child of where we are,
        // and whether it names anything is the namespace's answer, so a no leaves "is not recognized" to be reported.
        if (Environment.Location.IsVirtual && !Path.IsPathFullyQualified(word))
        {
            if (CommandResolver.Resolve(Environment, command.Words, command.RawWords).Kind != ResolvedCommandKind.NotFound)
                return false;

            try
            {
                Environment.ChangeDirectory(word);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }

            Environment.LastExitCode = 0;
            return true;
        }

        // "e:" is passed through as written,
        // GetFullPath given an explicit base ignores the per drive directories and would bring back the bare root.
        string target;
        if (word.Length == 2 && word[1] == ':' && char.IsAsciiLetter(word[0]))
        {
            target = word;
        }
        else
        {
            // "..." and its longer runs become real ".." segments before anything looks at the path, see ShellPath.
            var expanded = ShellPath.Expand(word);
            try
            {
                target = Path.GetFullPath(expanded, Environment.CurrentDirectory);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (!Directory.Exists(target) || !FirstSegmentExists(expanded))
                return false;
        }

        if (CommandResolver.Resolve(Environment, command.Words, command.RawWords).Kind != ResolvedCommandKind.NotFound)
            return false;

        try
        {
            Environment.ChangeDirectory(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(exception.Message);
            code = 1;
        }

        Environment.LastExitCode = code;
        return true;
    }
}
