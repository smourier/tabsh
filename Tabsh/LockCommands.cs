namespace Tabsh;

internal static class LockCommands
{
    // a Restart Manager session per match is not free, so a name that matches half of PATH is cut short.
    private const int _matchLimit = 20;
    private static readonly char[] _separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar];
    private static readonly char[] _wildcards = ['*', '?'];

    public static int Holders(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seconds = 0;
        var act = false;
        var force = false;
        FileLockAction? action = null;
        var targets = new List<string>();

        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                targets.Add(argument);
                continue;
            }

            switch (char.ToUpperInvariant(argument.Length > 1 ? argument[1] : ' '))
            {
                case 'M':
                    if (!ConsoleMonitor.TryParseInterval(argument, out seconds))
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidInterval, argument));

                    break;

                case 'K':
                    act = true;
                    if (!TryParseAction(argument, out action))
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));

                    break;

                case 'F':
                    force = true;
                    act = true;
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        if (targets.Count == 0)
            return context.Fail(Res.NameExpected);

        // nothing is ever ended without being named first, so the choosing needs somewhere to ask.
        if (act && !force && (Console.IsInputRedirected || Console.IsOutputRedirected))
            return context.Fail(Res.ActNeedsConsole);

        if (seconds > 0)
            return ConsoleMonitor.Run(
                context,
                seconds,
                writer => ReportAll(context, targets, writer, act: false, force: false, action: null),
                act ? key => Interact(context, targets, action, key) : null,
                act ? Res.LockMonitorKeys : null);

        return ReportAll(context, targets, context.Output, act, force, action);
    }

    // "/k" asks, "/k:c", "/k:r" and "/k:t" say which without asking what, and "/f" asks nothing at all.
    private static bool TryParseAction(string argument, out FileLockAction? action)
    {
        action = null;
        var text = argument.Length > 2 ? argument[2..].TrimStart(':') : string.Empty;
        if (text.Length == 0)
            return true;

        if (string.Equals(text, Res.ActionCloseKey, StringComparison.CurrentCultureIgnoreCase))
        {
            action = FileLockAction.Close;
            return true;
        }

        if (string.Equals(text, Res.ActionRestartKey, StringComparison.CurrentCultureIgnoreCase))
        {
            action = FileLockAction.Restart;
            return true;
        }

        if (string.Equals(text, Res.ActionTerminateKey, StringComparison.CurrentCultureIgnoreCase))
        {
            action = FileLockAction.Terminate;
            return true;
        }

        return false;
    }

    // the key pressed while watching is the action, since the list it applies to is already on the screen and read.
    // Anything else picks up the numbered list instead, and a key that means neither simply repaints.
    private static bool Interact(BuiltinContext context, List<string> targets, FileLockAction? action, ConsoleKeyInfo key)
    {
        var pressed = key.KeyChar.ToString();
        if (string.Equals(pressed, Res.ActionChooseKey, StringComparison.CurrentCultureIgnoreCase))
        {
            ReportAll(context, targets, context.Output, act: true, force: false, action);
            return true;
        }

        var what = action ?? Match(pressed);
        if (what == null)
            return true;

        ReportAll(context, targets, context.Output, act: true, force: true, what);
        return true;
    }

    private static FileLockAction? Match(string answer)
    {
        if (string.Equals(answer, Res.ActionCloseKey, StringComparison.CurrentCultureIgnoreCase))
            return FileLockAction.Close;

        if (string.Equals(answer, Res.ActionRestartKey, StringComparison.CurrentCultureIgnoreCase))
            return FileLockAction.Restart;

        if (string.Equals(answer, Res.ActionTerminateKey, StringComparison.CurrentCultureIgnoreCase))
            return FileLockAction.Terminate;

        return null;
    }

    private static int ReportAll(BuiltinContext context, List<string> targets, TextWriter writer, bool act, bool force, FileLockAction? action)
    {
        var code = 0;
        foreach (var target in targets)
        {
            var matches = Expand(context, target);
            if (matches.Count == 0)
            {
                writer.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.FileNotFound, target));
                code = 1;
                continue;
            }

            foreach (var match in matches)
            {
                var holders = Report(match, writer, act);

                // nothing to end is nothing to ask about, which is the whole of it for a file nobody is holding.
                if (act && holders.Count > 0)
                {
                    Act(match, holders, writer, force, action);
                }
            }

            if (matches.Count >= _matchLimit)
            {
                writer.WriteLine();
                writer.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.MatchLimit, _matchLimit));
            }
        }

        return code;
    }

    private static void Act(LockTarget target, List<JobProcess> holders, TextWriter writer, bool force, FileLockAction? action)
    {
        var chosen = force ? holders : Choose(holders, writer);
        if (chosen.Count == 0)
            return;

        var what = action ?? (force ? FileLockAction.Close : Ask(writer));
        if (what == null)
            return;

        var processIds = new List<uint>();
        foreach (var holder in chosen)
        {
            processIds.Add(holder.ProcessId);
        }

        // Restart Manager reaches every holder through the file itself, so the whole list needs no list at all.
        // Terminating does, since there is no file to hand to TerminateProcess.
        var all = chosen.Count == holders.Count && what.Value != FileLockAction.Terminate;
        writer.WriteLine(FileLocks.Act(target.Path, all ? [] : processIds, what.Value, out var error)
            ? Res.ActDone
            : string.Format(CultureInfo.CurrentCulture, Res.ActFailed, error));
    }

    private static List<JobProcess> Choose(List<JobProcess> holders, TextWriter writer)
    {
        writer.Write(Res.ActPrompt);
        writer.Flush();

        var answer = Console.ReadLine()?.Trim() ?? string.Empty;
        if (answer.Length == 0)
            return [];

        if (string.Equals(answer, Res.ActAllKey, StringComparison.CurrentCultureIgnoreCase))
            return holders;

        var chosen = new List<JobProcess>();
        foreach (var part in answer.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.CurrentCulture, out var number) && number >= 1 && number <= holders.Count)
            {
                chosen.Add(holders[number - 1]);
            }
        }

        return chosen;
    }

    private static FileLockAction? Ask(TextWriter writer)
    {
        writer.Write(Res.ActionPrompt);
        writer.Flush();

        var answer = Console.ReadKey(intercept: true).KeyChar.ToString();
        writer.WriteLine(answer);
        return Match(answer);
    }

    // everything a name could be pointing at, in order: the file it names, the one a command by that name would run,
    // whatever the running processes have loaded, and only then anything merely named like it.
    private static List<LockTarget> Expand(BuiltinContext context, string target)
    {
        if (target.IndexOfAny(_wildcards) >= 0)
            return Paths(Spec(context, target));

        var full = Full(context, target);
        if (full != null && (File.Exists(full) || Directory.Exists(full)))
            return [new LockTarget(full)];

        if (target.Length > 0 && target.IndexOfAny(_separators) < 0)
        {
            foreach (var found in CommandResolver.FindAll(context.Environment, target))
            {
                return [new LockTarget(found)];
            }

            var loaded = Loaded(target);
            if (loaded.Count > 0)
                return loaded;

            var like = Like(context, target + "*");
            if (like.Count == 0)
            {
                like = Like(context, "*" + target + "*");
            }

            if (like.Count > 0)
                return Paths(like);
        }

        // and whatever was typed otherwise, since Restart Manager answers for a path this process cannot stat.
        return full == null ? [] : [new LockTarget(full)];
    }

    // what the name starts with first, so "cm" is cmd.exe rather than everything with a c and an m in it,
    // and only then what merely contains it.
    private static List<LockTarget> Loaded(string target)
    {
        var modules = LoadedModules.Find(target + "*");
        if (modules.Count == 0)
        {
            modules = LoadedModules.Find("*" + target + "*");
        }

        var byPath = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (module.Path.Length == 0)
                continue;

            if (!byPath.TryGetValue(module.Path, out var processes))
            {
                if (byPath.Count >= _matchLimit)
                    continue;

                processes = [];
                byPath.Add(module.Path, processes);
            }

            if (!processes.Contains(module.ProcessId))
            {
                processes.Add(module.ProcessId);
            }
        }

        var found = new List<LockTarget>();
        foreach (var pair in byPath.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            found.Add(new LockTarget(pair.Key, pair.Value));
        }

        return found;
    }

    private static List<LockTarget> Paths(List<string> paths)
    {
        var found = new List<LockTarget>();
        foreach (var path in paths)
        {
            found.Add(new LockTarget(path));
        }

        return found;
    }

    private static string? Full(BuiltinContext context, string target)
    {
        try
        {
            return ShellPath.Resolve(target, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static List<string> Spec(BuiltinContext context, string target)
    {
        var separator = target.LastIndexOfAny(_separators);
        var below = separator >= 0 ? target[..separator] : string.Empty;
        var pattern = separator >= 0 ? target[(separator + 1)..] : target;
        if (below.IndexOfAny(_wildcards) >= 0 || pattern.Length == 0)
            return [];

        var directory = below.Length == 0 ? context.Environment.CurrentDirectory : Full(context, below);
        return directory == null ? [] : Matches(directory, pattern, []);
    }

    // the same walk a command name gets, the current directory then PATH.
    private static List<string> Like(BuiltinContext context, string pattern)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in CommandResolver.SearchDirectories(context.Environment))
        {
            if (found.Count >= _matchLimit)
                break;

            Matches(directory, pattern, found, seen);
        }

        return found;
    }

    private static List<string> Matches(string directory, string pattern, List<string> found, HashSet<string>? seen = null)
    {
        try
        {
            var entries = new List<string>();
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos(pattern))
            {
                entries.Add(entry.FullName);
            }

            // the file system hands them back in its own order, which is not one anybody reads in.
            entries.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (found.Count >= _matchLimit)
                    break;

                if (seen == null || seen.Add(entry))
                {
                    found.Add(entry);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // a directory that will not be listed has nothing to name, and neither has a pattern Windows refuses.
        }

        return found;
    }

    // Restart Manager first, and then whoever was found holding it loaded,
    // because a module is held whether or not restarting its process would help.
    private static List<JobProcess> Report(LockTarget target, TextWriter writer, bool numbered)
    {
        var holders = FileLocks.Holding(target.Path);
        var seen = new HashSet<uint>();
        foreach (var holder in holders)
        {
            seen.Add(holder.ProcessId);
        }

        foreach (var processId in target.LoadedIn)
        {
            if (seen.Add(processId))
            {
                holders.Add(JobInspector.Describe(processId));
            }
        }

        writer.WriteLine();
        writer.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.HoldingLine, target.Path));
        writer.WriteLine();

        if (holders.Count == 0)
        {
            writer.WriteLine(Res.NothingHolding);
            return holders;
        }

        for (var i = 0; i < holders.Count; i++)
        {
            writer.WriteLine(numbered
                ? string.Format(CultureInfo.CurrentCulture, Res.NumberedProcessLine, i + 1, holders[i].ProcessId, holders[i].Description)
                : string.Format(CultureInfo.CurrentCulture, Res.ProcessLine, holders[i].ProcessId, holders[i].Description));
        }

        return holders;
    }

    private sealed class LockTarget(string path, List<uint>? loadedIn = null)
    {
        public string Path { get; } = path;

        // the processes seen with it loaded, which is where the path came from in the first place.
        public IReadOnlyList<uint> LoadedIn { get; } = loadedIn ?? [];
    }
}
