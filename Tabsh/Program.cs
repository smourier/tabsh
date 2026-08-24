namespace Tabsh;

internal static class Program
{
    private static int Main()
    {
        // the console belongs to whoever started it,
        // so what it was set to is remembered and put back on the way out.
        ConsoleSession.Capture();
        try
        {
            return Run();
        }
        finally
        {
            ConsoleSession.Restore();
        }
    }

    private static int Run()
    {
        var options = ShellOptions.Parse(Environment.CommandLine);
        var shell = new Shell();

        // colour and cursor movement written by anything running here are obeyed rather than printed,
        // conhost leaves that off until an application asks for it.
        ConsoleSession.Normalize();

        // a name in Chinese is a name, not three question marks. See ConsoleSession.UseUnicode.
        ConsoleSession.UseUnicode();

        ConsoleSession.UseWritableNumbers();

        shell.RunStartupFile();

        if (!options.Quiet && options.Command == null)
        {
            Console.Title = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>()?.Title!;
            Console.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.BannerHeadline, Console.Title, Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version));
            Console.WriteLine(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
            Console.WriteLine();
        }

        var code = 0;
        if (options.Command != null)
        {
            code = shell.ExecuteLine(options.Command);
            if (!options.KeepRunning)
                return code;

            // /k stays, so the command it just ran is separated from the first prompt the same way every later one is.
            Console.WriteLine();
        }

        if (shell.ExitRequested)
            return shell.ExitCode;

        return shell.Run() is var exitCode && exitCode != 0 ? exitCode : code;
    }
}
