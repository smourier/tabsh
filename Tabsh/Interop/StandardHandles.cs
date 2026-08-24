namespace Tabsh.Interop;

// the three handles a command runs with, the console's own unless a redirection replaced one.
internal readonly partial struct StandardHandles
{
    public StandardHandles(nint input, nint output, nint error)
    {
        Input = input;
        Output = output;
        Error = error;
    }

    public nint Input { get; }
    public nint Output { get; }
    public nint Error { get; }

    public StandardHandles WithInput(nint handle) => new(handle, Output, Error);
    public StandardHandles WithOutput(nint handle) => new(Input, handle, Error);
    public StandardHandles WithError(nint handle) => new(Input, Output, handle);

    public static StandardHandles FromConsole() => new(GetStdHandle(STD_INPUT_HANDLE), GetStdHandle(STD_OUTPUT_HANDLE), GetStdHandle(STD_ERROR_HANDLE));

#pragma warning disable IDE1006 // Naming Styles
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);
}
