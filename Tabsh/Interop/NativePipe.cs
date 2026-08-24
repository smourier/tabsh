namespace Tabsh.Interop;

// both ends non inheritable on purpose, ProcessLauncher marks only the end a given child is meant to get,
// so the other cannot leak in and hold the pipe open past the writer's exit.
internal sealed partial class NativePipe : IDisposable
{
    private nint _readEnd;
    private nint _writeEnd;

    public NativePipe()
    {
        if (!CreatePipe(out _readEnd, out _writeEnd, 0, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public nint ReadEnd => _readEnd;
    public nint WriteEnd => _writeEnd;

    public void CloseReadEnd() => Close(ref _readEnd);
    public void CloseWriteEnd() => Close(ref _writeEnd);

    public void Dispose()
    {
        Close(ref _readEnd);
        Close(ref _writeEnd);
    }

    private static void Close(ref nint handle)
    {
        var value = Interlocked.Exchange(ref handle, 0);
        if (value != 0)
        {
            Functions.CloseHandle(value);
        }
    }

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out nint hReadPipe, out nint hWritePipe, nint lpPipeAttributes, uint nSize);

}
