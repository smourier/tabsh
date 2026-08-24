namespace Tabsh;

// cmd's search order, except at the tail of it,
// where a file that is not a program goes to the association database and a name that is not a file at all to App Paths.
internal static class CommandResolver
{
    private const string _appPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string _defaultPathExtensions = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";
    private const string _commandInterpreter = "cmd.exe";

    // PowerShell 7 is what a script written today expects, Windows PowerShell is the one always there.
    // They take the same switches.
    private static readonly string[] _powerShellNames = ["pwsh.exe", "powershell.exe"];

    // arguments go through verbatim where the raw words are available,
    // a program parses its own command line and find and findstr need the quotes they were given.
    public static ResolvedCommand Resolve(ShellEnvironment environment, IReadOnlyList<string> words, IReadOnlyList<string>? rawWords = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(words);

        if (words.Count == 0)
            return ResolvedCommand.NotFound;

        var arguments = rawWords != null && rawWords.Count == words.Count
            ? string.Join(' ', rawWords.Skip(1))
            : CommandLineBuilder.Build(words.Skip(1));
        var file = Find(environment, words[0]) ?? FindInApplicationPaths(words[0]);
        if (file == null)
            return ResolvedCommand.NotFound;

        switch (Path.GetExtension(file).ToUpperInvariant())
        {
            case ".EXE":
            case ".COM":
                return new ResolvedCommand(ResolvedCommandKind.Executable, file, Join(file, arguments), arguments);

            case ".BAT":
            case ".CMD":
                return new ResolvedCommand(ResolvedCommandKind.Executable, file, BuildBatchCommandLine(environment, file, arguments), arguments);

            case ".PS1":
                var powerShell = FindPowerShell(environment);
                if (powerShell == null)
                    break;

                return new ResolvedCommand(ResolvedCommandKind.Executable, file, BuildPowerShellCommandLine(powerShell, file, arguments), arguments);
        }

        return new ResolvedCommand(ResolvedCommandKind.Document, file, string.Empty, arguments);
    }

    // the only spelling that survives a batch path with a space and arguments carrying quotes of their own.
    private static string BuildBatchCommandLine(ShellEnvironment environment, string file, string arguments)
    {
        var builder = new StringBuilder();
        CommandLineBuilder.Append(builder, CommandInterpreter(environment));
        builder.Append(" /s /c \"").Append(Join(file, arguments)).Append('"');
        return builder.ToString();
    }

    // cmd whatever COMSPEC says, since a shell started from TCC inherits TCC.EXE there and reads cmd's language only approximately,
    // stopping on the byte order mark cmd skips. A COMSPEC naming a cmd.exe is still honoured.
    private static string CommandInterpreter(ShellEnvironment environment)
    {
        var comSpec = environment.Get("COMSPEC");
        if (!string.IsNullOrEmpty(comSpec)
            && string.Equals(Path.GetFileName(comSpec), _commandInterpreter, StringComparison.OrdinalIgnoreCase)
            && File.Exists(comSpec))
            return comSpec;

        return Path.Combine(Environment.SystemDirectory, _commandInterpreter);
    }

    private static string? FindPowerShell(ShellEnvironment environment)
    {
        foreach (var name in _powerShellNames)
        {
            // PowerShell 7 registers itself in App Paths, so an installation kept off PATH is still found.
            var found = Find(environment, name) ?? FindInApplicationPaths(name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string BuildPowerShellCommandLine(string powerShell, string file, string arguments)
    {
        var builder = new StringBuilder();
        CommandLineBuilder.Append(builder, powerShell);
        builder.Append(" -NoLogo -ExecutionPolicy Bypass -File ");
        CommandLineBuilder.Append(builder, file);
        if (arguments.Length > 0)
        {
            builder.Append(' ').Append(arguments);
        }

        return builder.ToString();
    }

    private static string Join(string file, string arguments)
    {
        var builder = new StringBuilder();
        CommandLineBuilder.Append(builder, file);
        if (arguments.Length > 0)
        {
            builder.Append(' ').Append(arguments);
        }

        return builder.ToString();
    }

    public static string? Find(ShellEnvironment environment, string name) => FindAll(environment, name).FirstOrDefault();

    // every match in search order, which is what "where" reports and what makes a shadowed program visible.
    // The same file reached twice is not two matches, and PATH holding a directory twice is common enough to matter.
    public static IEnumerable<string> FindAll(ShellEnvironment environment, string name)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensions = GetPathExtensions(environment);
        if (IsPathQualified(name))
        {
            foreach (var found in Probe(FullPathOrNull(environment, name), extensions))
            {
                if (seen.Add(found))
                {
                    yield return found;
                }
            }

            yield break;
        }

        foreach (var directory in SearchDirectories(environment))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            foreach (var found in Probe(candidate, extensions))
            {
                if (seen.Add(found))
                {
                    yield return found;
                }
            }
        }
    }

    // the current directory first, exactly as cmd does it, then PATH.
    public static IEnumerable<string> SearchDirectories(ShellEnvironment environment)
    {
        yield return environment.CurrentDirectory;

        var path = environment.Get("PATH");
        if (string.IsNullOrEmpty(path))
            yield break;

        foreach (var entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return entry.Trim('"');
        }
    }

    public static string[] GetPathExtensions(ShellEnvironment environment)
    {
        var value = environment.Get("PATHEXT");
        if (string.IsNullOrEmpty(value))
        {
            value = _defaultPathExtensions;
        }

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> Probe(string? candidate, string[] extensions)
    {
        if (candidate == null)
            yield break;

        var found = OnDisk(candidate);
        if (found != null)
        {
            yield return found;
        }

        foreach (var extension in extensions)
        {
            found = OnDisk(candidate + extension);
            if (found != null)
            {
                yield return found;
            }
        }
    }

    // the name the way the disk spells it, since the extension came from PATHEXT and PATHEXT is upper case.
    // A file called tabsh.exe would otherwise be reported as tabsh.EXE, which is not a name anybody would type.
    private static string? OnDisk(string path)
    {
        if (path.IndexOfAny(_wildcards) >= 0)
            return null;

        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(directory) || name.Length == 0)
            return File.Exists(path) ? path : null;

        try
        {
            foreach (var entry in Directory.EnumerateFiles(directory, name))
            {
                return entry;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // a directory that will not be listed cannot be searched, and neither can one that is not there.
        }

        return null;
    }

    private static readonly char[] _wildcards = ['*', '?'];

    private static string? FullPathOrNull(ShellEnvironment environment, string name)
    {
        try
        {
            return ShellPath.Resolve(name, environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsPathQualified(string name) =>
        name.Contains('\\') || name.Contains('/') || (name.Length >= 2 && name[1] == ':');

    private static string? FindInApplicationPaths(string name)
    {
        if (IsPathQualified(name))
            return null;

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var candidate in new[] { name, name + ".exe" })
            {
                using var key = root.OpenSubKey(_appPathsKey + "\\" + candidate);
                if (key?.GetValue(null) is not string value)
                    continue;

                value = value.Trim().Trim('"');
                if (value.Length > 0 && File.Exists(value))
                    return value;
            }
        }

        return null;
    }
}
