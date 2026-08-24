namespace Tabsh;

// a handle still on the console uses the console writer rather than a stream of our own, the only way the wrapping,
// the code page and the cursor stay in step with everything else this process prints.
internal static class StandardStreams
{
    private static readonly StandardHandles _console = StandardHandles.FromConsole();

    public static bool IsConsoleOutput(nint handle) => handle == _console.Output;
    public static bool IsConsoleError(nint handle) => handle == _console.Error;
    public static bool IsConsoleInput(nint handle) => handle == _console.Input;

    public static bool IsRedirected(in StandardHandles handles) =>
        handles.Input != _console.Input || handles.Output != _console.Output || handles.Error != _console.Error;

    public static TextWriter CreateWriter(nint handle)
    {
        if (IsConsoleOutput(handle))
            return Console.Out;

        if (IsConsoleError(handle))
            return Console.Error;

        var stream = new FileStream(new SafeFileHandle(handle, ownsHandle: false), FileAccess.Write);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    public static TextReader CreateReader(nint handle)
    {
        if (IsConsoleInput(handle))
            return Console.In;

        var stream = new FileStream(new SafeFileHandle(handle, ownsHandle: false), FileAccess.Read);
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    }

    // the console writers belong to the process, not to the command that borrowed them.
    public static void Release(TextWriter writer)
    {
        if (!ReferenceEquals(writer, Console.Out) && !ReferenceEquals(writer, Console.Error))
        {
            writer.Dispose();
        }
    }

    public static void Release(TextReader reader)
    {
        if (!ReferenceEquals(reader, Console.In))
        {
            reader.Dispose();
        }
    }
}
