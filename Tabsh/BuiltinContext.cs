namespace Tabsh;

// what a built in command is handed: its arguments, and the three streams it should be using rather than the console,
// because a built in inside a pipeline or behind a redirection has to end up in the same place an external program would.
internal sealed class BuiltinContext
{
    private readonly StandardHandles _handles;
    private readonly IReadOnlyList<string> _words;
    private TextWriter? _output;
    private TextWriter? _error;
    private TextReader? _input;

    public BuiltinContext(Shell shell, IReadOnlyList<string> words, StandardHandles handles)
    {
        ArgumentNullException.ThrowIfNull(words);

        Shell = shell;
        _words = words;
        _handles = handles;
        Arguments = words.Skip(1).ToArray();
    }

    public Shell Shell { get; }
    public ShellEnvironment Environment => Shell.Environment;
    public string Name => _words.Count > 0 ? _words[0] : string.Empty;
    public IReadOnlyList<string> Arguments { get; }

    public TextWriter Output => _output ??= StandardStreams.CreateWriter(_handles.Output);
    public TextWriter Error => _error ??= StandardStreams.CreateWriter(_handles.Error);
    public TextReader Input => _input ??= StandardStreams.CreateReader(_handles.Input);

    public bool WritesToConsole => StandardStreams.IsConsoleOutput(_handles.Output);

    // true when a pipe or a redirection gave this command an input of its own,
    // since a script fed to the shell arrives on the same handle and reading it would eat the rest of the script.
    public bool HasOwnInput => !StandardStreams.IsConsoleInput(_handles.Input);

    public int Fail(string message)
    {
        Error.WriteLine(message);
        return 1;
    }

    public void Release()
    {
        if (_output != null)
        {
            StandardStreams.Release(_output);
        }

        if (_error != null)
        {
            StandardStreams.Release(_error);
        }

        if (_input != null)
        {
            StandardStreams.Release(_input);
        }
    }
}
