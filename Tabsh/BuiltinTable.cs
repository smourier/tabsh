namespace Tabsh;

// either something that changes the shell's own state, which a child process could not do,
// or a name cmd never had a program for. Anything that already ships as an executable is deliberately absent.
internal sealed class BuiltinTable
{
    private readonly Dictionary<string, BuiltinCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Shell _shell;

    public BuiltinTable(Shell shell)
    {
        _shell = shell;

        Add("alias", Res.DescribeAlias, ShellCommands.Alias);
        Add("base64", Res.DescribeBase64, Base64Commands.Convert);
        Add("cd", Res.DescribeCd, NavigationCommands.ChangeDirectory, "chdir");
        Add("clip", Res.DescribeClip, ClipCommands.Clip);
        Add("cls", Res.DescribeCls, ShellCommands.Clear);
        Add("color", Res.DescribeColor, ShellCommands.Color);
        Add("complete", Res.DescribeComplete, ShellCommands.Complete);
        Add("console", Res.DescribeConsole, ShellCommands.ConsoleModes);
        Add("copy", Res.DescribeCopy, FileCommands.Copy);
        Add("del", Res.DescribeDel, FileCommands.Delete, "erase");
        Add("dir", Res.DescribeDir, FileCommands.List);
        Add("echo", Res.DescribeEcho, ShellCommands.Echo);
        Add("guid", Res.DescribeGuid, GuidCommands.Generate, "uuid");
        Add("hash", Res.DescribeHash, HashCommands.Compute);
        Add("exit", Res.DescribeExit, ShellCommands.Exit);
        Add("help", Res.DescribeHelp, ShellCommands.Help, "?");
        Add("history", Res.DescribeHistory, ShellCommands.History);
        Add("keys", Res.DescribeKeys, ShellCommands.Keys);
        Add("lock", Res.DescribeLock, LockCommands.Holders);
        Add("measure", Res.DescribeMeasure, MeasureCommands.Measure);
        Add("md", Res.DescribeMd, FileCommands.MakeDirectory, "mkdir");
        Add("move", Res.DescribeMove, FileCommands.Move);
        Add("path", Res.DescribePath, ShellCommands.Path);
        Add("popd", Res.DescribePopd, NavigationCommands.PopDirectory);
        Add("prompt", Res.DescribePrompt, ShellCommands.Prompt);
        Add("props", Res.DescribeProps, PropertyCommands.Properties);
        Add("pushd", Res.DescribePushd, NavigationCommands.PushDirectory);
        Add("pwd", Res.DescribePwd, NavigationCommands.PrintDirectory);
        Add("rd", Res.DescribeRd, FileCommands.RemoveDirectory, "rmdir");
        Add("ren", Res.DescribeRen, FileCommands.Rename, "rename");
        Add("set", Res.DescribeSet, ShellCommands.Set);
        Add("start", Res.DescribeStart, ShellCommands.Start);
        Add("title", Res.DescribeTitle, ShellCommands.Title);
        Add("type", Res.DescribeType, FileCommands.Type);
        Add("ver", Res.DescribeVer, ShellCommands.Version);
        Add("where", Res.DescribeWhere, ShellCommands.Where, "which");
    }

    public IEnumerable<string> Names => _commands.Keys;

    public IEnumerable<BuiltinCommand> All => _commands.Values.Distinct().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public BuiltinCommand? Find(string name) => _commands.TryGetValue(name, out var command) ? command : null;

    // a built in running straight into its argument, "cd\" and "dir/w",
    // where only a separator starts one, so a name that merely begins with a command is left alone. Longest match wins.
    public BuiltinCommand? FindAttached(string word, out string argument)
    {
        ArgumentNullException.ThrowIfNull(word);

        argument = string.Empty;
        for (var length = word.Length - 1; length > 0; length--)
        {
            if (word[length] is not ('\\' or '/' or '.' or '@'))
                continue;

            var command = Find(word[..length]);
            if (command != null)
            {
                argument = word[length..];
                return command;
            }
        }

        return null;
    }

    public int Run(BuiltinCommand command, IReadOnlyList<string> words, StandardHandles handles)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = new BuiltinContext(_shell, words, handles);
        if (Describes(context))
        {
            context.Release();
            return 0;
        }

        try
        {
            return command.Handler(context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            return context.Fail(exception.Message);
        }
        finally
        {
            context.Release();
        }
    }

    // every built in answers "/?" the same way, so none of them has to remember to.
    // Only as the first word, which is where anyone asking puts it, and is what keeps "echo a /? b" an echo.
    private static bool Describes(BuiltinContext context)
    {
        if (context.Arguments.Count == 0 || context.Arguments[0] is not (_helpSwitch or _helpDash))
            return false;

        var command = context.Shell.Builtins.Find(context.Name);
        if (command == null)
            return false;

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.HelpLine, command.Name, command.Description));
        return true;
    }

    private const string _helpSwitch = "/?";
    private const string _helpDash = "-?";

    private void Add(string name, string description, Func<BuiltinContext, int> handler, params string[] otherNames)
    {
        var command = new BuiltinCommand(name, description, handler);
        _commands[name] = command;
        foreach (var other in otherNames)
        {
            _commands[other] = command;
        }
    }
}
