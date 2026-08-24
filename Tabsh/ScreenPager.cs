namespace Tabsh;

// dir /p, a screenful at a time. Without a console there is nothing to fill and nothing to wait for.
internal sealed class ScreenPager(BuiltinContext context, bool pause)
{
    private const int _reservedRows = 2;
    private int _written;

    public void WriteLine(string text)
    {
        context.Output.WriteLine(text);
        if (!pause || Console.IsOutputRedirected || Console.IsInputRedirected || !context.WritesToConsole)
            return;

        _written++;
        if (_written < Math.Max(1, Console.WindowHeight - _reservedRows))
            return;

        _written = 0;
        context.Output.Write(Res.PressAnyKey);
        context.Output.Flush();
        Console.ReadKey(intercept: true);
        context.Output.WriteLine();
    }
}
