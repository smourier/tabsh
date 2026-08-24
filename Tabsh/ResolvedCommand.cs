namespace Tabsh;

internal sealed class ResolvedCommand(ResolvedCommandKind kind, string path, string commandLine, string arguments)
{
    public static ResolvedCommand NotFound { get; } = new(ResolvedCommandKind.NotFound, string.Empty, string.Empty, string.Empty);

    public ResolvedCommandKind Kind { get; } = kind;

    // the file that was found, which for a batch file is the batch file and not the cmd.exe that will run it.
    public string Path { get; } = path;

    // what CreateProcess is given, interpreter included.
    public string CommandLine { get; } = commandLine;

    // the arguments alone, which is the shape ShellExecuteEx wants for a document.
    public string Arguments { get; } = arguments;
}
