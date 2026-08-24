namespace Tabsh;

// where.exe's own surface, because a name is not always something you already know how to spell.
internal static class WhereCommands
{
    private const int _usageError = 2;
    private const int _notFound = 1;
    private static readonly char[] _wildcards = ['*', '?'];
    private static readonly char[] _separators = [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar];

    public static int Where(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? under = null;
        var quiet = false;
        var quoted = false;
        var details = false;
        var patterns = new List<string>();

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            var argument = context.Arguments[i];
            if (!argument.StartsWith('/'))
            {
                patterns.Add(argument);
                continue;
            }

            switch (char.ToUpperInvariant(argument.Length > 1 ? argument[1] : ' '))
            {
                case 'R':
                    i++;
                    if (i >= context.Arguments.Count)
                    {
                        context.Fail(Res.DirectoryExpected);
                        return _usageError;
                    }

                    under = context.Arguments[i];
                    break;

                case 'Q':
                    quiet = true;
                    break;

                case 'F':
                    quoted = true;
                    break;

                case 'T':
                    details = true;
                    break;

                default:
                    context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
                    return _usageError;
            }
        }

        if (patterns.Count == 0)
        {
            context.Fail(Res.NameExpected);
            return _usageError;
        }

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builtins = new List<string>();

        foreach (var pattern in patterns)
        {
            Search(context, under, pattern, found, seen);

            // a name the shell handles itself is not on any disk, so nothing above could ever have found it.
            if (under == null && pattern.IndexOfAny(_wildcards) < 0 && context.Shell.Builtins.Find(pattern) is BuiltinCommand builtin)
            {
                builtins.Add(builtin.Name);
            }
        }

        if (found.Count == 0 && builtins.Count == 0)
        {
            if (!quiet)
            {
                context.Error.WriteLine(Res.NoFilesFound);
            }

            return _notFound;
        }

        if (quiet)
            return 0;

        foreach (var path in found)
        {
            context.Output.WriteLine(Describe(path, quoted, details));
        }

        foreach (var name in builtins)
        {
            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.BuiltInMarker, name));
        }

        return 0;
    }

    // the current directory and then PATH, unless the pattern named its own places or /R named one to walk.
    private static void Search(BuiltinContext context, string? under, string pattern, List<string> found, HashSet<string> seen)
    {
        var extensions = CommandResolver.GetPathExtensions(context.Environment);
        if (under != null)
        {
            var directory = Resolve(context, under);
            if (directory != null)
            {
                Collect(directory, pattern, extensions, recurse: true, found, seen);
            }

            return;
        }

        // a pattern may carry the places to look on its front, "$windir:*.dll" or "c:\one;c:\two:*.dll".
        if (TrySplitPlaces(context, pattern, out var places, out var rest))
        {
            foreach (var place in places)
            {
                Collect(place, rest, extensions, recurse: false, found, seen);
            }

            return;
        }

        // a pattern that names a directory of its own is looked for there and nowhere else.
        var separator = pattern.LastIndexOfAny(_separators);
        if (separator >= 0 || (pattern.Length > 1 && pattern[1] == ':'))
        {
            var directory = Resolve(context, separator >= 0 ? pattern[..separator] : pattern[..2]);
            if (directory != null)
            {
                Collect(directory, separator >= 0 ? pattern[(separator + 1)..] : pattern[2..], extensions, recurse: false, found, seen);
            }

            return;
        }

        foreach (var directory in CommandResolver.SearchDirectories(context.Environment))
        {
            Collect(directory, pattern, extensions, recurse: false, found, seen);
        }
    }

    private static bool TrySplitPlaces(BuiltinContext context, string pattern, out List<string> places, out string rest)
    {
        places = [];
        rest = pattern;

        // the last colon, and never the one in a drive letter, which is the only colon a plain path has.
        var colon = pattern.LastIndexOf(':');
        if (colon <= 1)
            return false;

        var prefix = pattern[..colon];
        rest = pattern[(colon + 1)..];
        if (prefix.StartsWith('$'))
        {
            prefix = context.Environment.Get(prefix[1..]) ?? string.Empty;
        }

        foreach (var place in prefix.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = Resolve(context, place.Trim('"'));
            if (directory != null)
            {
                places.Add(directory);
            }
        }

        return places.Count > 0;
    }

    private static string? Resolve(BuiltinContext context, string path)
    {
        try
        {
            return ShellPath.Resolve(path, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // the pattern as written, and then with each PATHEXT extension on the end, which is what where.exe does.
    private static void Collect(string directory, string pattern, string[] extensions, bool recurse, List<string> found, HashSet<string> seen)
    {
        if (pattern.Length == 0)
            return;

        Enumerate(directory, pattern, recurse, found, seen);
        foreach (var extension in extensions)
        {
            Enumerate(directory, pattern + extension, recurse, found, seen);
        }
    }

    private static void Enumerate(string directory, string pattern, bool recurse, List<string> found, HashSet<string> seen)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            IgnoreInaccessible = true,
            MatchType = MatchType.Win32,
            AttributesToSkip = 0,
        };

        try
        {
            foreach (var entry in Directory.EnumerateFiles(directory, pattern, options))
            {
                if (seen.Add(entry))
                {
                    found.Add(entry);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // a place that is not there, or will not be listed, simply holds none of what was asked for.
        }
    }

    private static string Describe(string path, bool quoted, bool details)
    {
        var name = quoted ? string.Format(CultureInfo.CurrentCulture, Res.QuotedValue, path) : path;
        if (!details)
            return name;

        var file = new FileInfo(path);
        return string.Format(
            CultureInfo.CurrentCulture,
            Res.WhereDetailLine,
            file.Length,
            file.LastWriteTime.ToString("d", CultureInfo.CurrentCulture),
            file.LastWriteTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            name);
    }
}
