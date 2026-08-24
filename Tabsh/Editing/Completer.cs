namespace Tabsh.Editing;

// directories come first and keep their trailing separator,
// which is what makes the next TAB search inside them and lets Enter read as "go there" rather than "run that".
internal sealed class Completer(Shell shell)
{
    private static readonly char[] _pathSeparators = ['\\', '/', ':'];
    private const string _namespaceRoot = "@";
    private static readonly string[] _directoryOnlyCommands = ["cd", "chdir", "pushd", "rd", "rmdir"];

    public CompletionSession? Create(string text, int cursor)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokenStart = FindTokenStart(text, cursor);
        var raw = text[tokenStart..cursor];
        var token = raw.Replace("\"", string.Empty);

        var commandPosition = IsCommandPosition(text, tokenStart);
        string commandWord;

        if (commandPosition && shell.Builtins.FindAttached(token, out var attachedArgument) is BuiltinCommand attached)
        {
            // "cd\" is a command and a path in one word, and a built in name cannot contain a quote,
            // so the raw text splits where the unquoted one does.
            var nameLength = token.Length - attachedArgument.Length;
            tokenStart += nameLength;
            raw = raw[nameLength..];
            token = attachedArgument;
            commandPosition = false;
            commandWord = attached.Name;
        }
        else
        {
            commandWord = commandPosition ? string.Empty : CommandWordAt(text, tokenStart);
        }

        var candidates = new List<CompletionCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // what is here comes first, always.
        // The other way round buries it behind the hundred and thirty programs on PATH that begin with an R.
        var directoriesOnly = _directoryOnlyCommands.Contains(commandWord, StringComparer.OrdinalIgnoreCase);

        // in the namespace what is in front of you is that folder's children, not the directory the process sits in.
        // A token with a separator is a path whatever the shell is showing.
        if (token.StartsWith(_namespaceRoot))
        {
            AddNamespaceEntries(candidates, seen, token, directoriesOnly);
        }
        else if (shell.Environment.Location.IsVirtual && token.IndexOfAny(_pathSeparators) < 0)
        {
            AddShellEntries(candidates, seen, token, directoriesOnly);
        }
        else
        {
            AddFileSystemEntries(candidates, seen, token, directoriesOnly);
        }

        // with nothing typed TAB is looking around rather than naming a command,
        // and offering every built in would bury the directory.
        if (commandPosition && token.Length > 0)
        {
            AddCommandNames(candidates, seen, token);
        }

        if (candidates.Count == 0)
            return null;

        return new CompletionSession(tokenStart, raw, candidates);
    }

    private void AddFileSystemEntries(List<CompletionCandidate> candidates, HashSet<string> seen, string token, bool directoriesOnly)
    {
        var separator = token.LastIndexOfAny(_pathSeparators);
        var prefix = separator >= 0 ? token[..(separator + 1)] : string.Empty;
        var namePrefix = separator >= 0 ? token[(separator + 1)..] : token;

        // a dot run names a directory rather than the start of one, so TAB looks inside it.
        if (ShellPath.IsDotRun(namePrefix) && namePrefix.Length >= 2)
        {
            prefix = token + "\\";
            namePrefix = string.Empty;
        }

        string searchDirectory;
        try
        {
            searchDirectory = prefix.Length == 0
                ? shell.Environment.CurrentDirectory
                : ShellPath.Resolve(prefix, shell.Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (!Directory.Exists(searchDirectory))
            return;

        var directories = new List<string>();
        var files = new List<string>();
        try
        {
            foreach (var entry in new DirectoryInfo(searchDirectory).EnumerateFileSystemInfos())
            {
                if (namePrefix.Length > 0 && !entry.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // with nothing typed the answer should be the listing dir gives, which leaves these out.
                // Once a name is started the entry has been asked for, which is what makes ".g" reach .git.
                if (namePrefix.Length == 0 && (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    continue;

                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry.Name);
                }
                else if (!directoriesOnly)
                {
                    files.Add(entry.Name);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        directories.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            Add(candidates, seen, Quote(prefix + directory + "\\"), isDirectory: true);
        }

        foreach (var file in files)
        {
            Add(candidates, seen, Quote(prefix + file), isDirectory: false);
        }
    }

    // a name written from the namespace root, split at the last separator exactly as a path is.
    private void AddNamespaceEntries(List<CompletionCandidate> candidates, HashSet<string> seen, string token, bool directoriesOnly)
    {
        var separator = token.LastIndexOfAny(_pathSeparators);
        var prefix = separator >= 0 ? token[..(separator + 1)] : _namespaceRoot;
        var namePrefix = separator >= 0 ? token[(separator + 1)..] : token[_namespaceRoot.Length..];

        // the part between the "@" and the last separator is the way down to the folder being listed.
        var below = prefix[_namespaceRoot.Length..].TrimStart(':');

        var location = new ShellLocation();
        if (!location.EnterPath(below, fromRoot: true))
            return;

        foreach (var child in Ordered(location.Children()))
        {
            if (directoriesOnly && !child.IsFolder)
                continue;

            if (namePrefix.Length > 0 && !child.Name.StartsWith(namePrefix, StringComparison.CurrentCultureIgnoreCase))
                continue;

            Add(candidates, seen, Quote(prefix + child.Name), child.IsFolder);
        }
    }

    private static List<ShellChild> Ordered(List<ShellChild> children)
    {
        children.Sort((a, b) =>
        {
            var byKind = b.IsFolder.CompareTo(a.IsFolder);
            return byKind != 0 ? byKind : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        return children;
    }

    // no trailing separator here, a shell child is reached by its name and only its name.
    private void AddShellEntries(List<CompletionCandidate> candidates, HashSet<string> seen, string token, bool directoriesOnly)
    {
        foreach (var child in Ordered(shell.Environment.Location.Children()))
        {
            if (directoriesOnly && !child.IsFolder)
                continue;

            if (token.Length > 0 && !child.Name.StartsWith(token, StringComparison.CurrentCultureIgnoreCase))
                continue;

            Add(candidates, seen, Quote(child.Name), child.IsFolder);
        }
    }

    // sorted apart rather than together, or a built in lands in the middle of the programs it shares a letter with.
    // Only ever called with something typed, an empty token would walk every directory on PATH.
    private void AddCommandNames(List<CompletionCandidate> candidates, HashSet<string> seen, string token)
    {
        // a token that already names a path is a path, whatever position it is in.
        if (token.IndexOfAny(_pathSeparators) >= 0)
            return;

        var builtins = new List<string>();
        foreach (var name in shell.Builtins.Names.Concat(shell.Aliases.Names))
        {
            if (name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                builtins.Add(name);
            }
        }

        // the current directory is skipped here, it has already been offered as part of the file system.
        var programs = new List<string>();
        var extensions = CommandResolver.GetPathExtensions(shell.Environment);
        foreach (var directory in CommandResolver.SearchDirectories(shell.Environment).Skip(1))
        {
            programs.AddRange(ExecutablesIn(directory, token, extensions));
        }

        builtins.Sort(StringComparer.OrdinalIgnoreCase);
        programs.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var name in builtins.Concat(programs))
        {
            Add(candidates, seen, Quote(name), isDirectory: false);
        }
    }

    private static IEnumerable<string> ExecutablesIn(string directory, string token, string[] extensions)
    {
        List<string> found = [];
        try
        {
            if (!Directory.Exists(directory))
                return found;

            foreach (var file in Directory.EnumerateFiles(directory, token + "*"))
            {
                var extension = Path.GetExtension(file);
                if (extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(Path.GetFileName(file));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // a PATH entry pointing at a disconnected drive is not worth failing the completion over.
        }

        return found;
    }

    private static void Add(List<CompletionCandidate> candidates, HashSet<string> seen, string text, bool isDirectory)
    {
        if (seen.Add(text))
        {
            candidates.Add(new CompletionCandidate(text, isDirectory));
        }
    }

    private static string Quote(string value) => value.IndexOfAny([' ', '\t']) >= 0 ? "\"" + value + "\"" : value;

    // where the token being completed starts, which is after the last delimiter that was not inside quotes.
    private static int FindTokenStart(string text, int cursor)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < cursor; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted)
                continue;

            if (c is ' ' or '\t' or '|' or '&' or '(' or ')' or '<' or '>')
            {
                start = i + 1;
            }
        }

        return start;
    }

    // the first word of a command is a command, and so is the first word after a pipe or a separator.
    // A word after a redirection operator is a file name, not a command, which is why those are not counted here.
    private static bool IsCommandPosition(string text, int tokenStart)
    {
        for (var i = tokenStart - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c is ' ' or '\t')
                continue;

            return c is '|' or '&' or '(';
        }

        return true;
    }

    // the command this token is an argument of, which is what decides whether files are worth offering at all.
    private static string CommandWordAt(string text, int tokenStart)
    {
        var commandStart = 0;
        var quoted = false;
        for (var i = 0; i < tokenStart; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && c is '|' or '&' or '(')
            {
                commandStart = i + 1;
            }
        }

        while (commandStart < tokenStart && (text[commandStart] == ' ' || text[commandStart] == '\t'))
        {
            commandStart++;
        }

        var end = commandStart;
        while (end < tokenStart && text[end] is not (' ' or '\t'))
        {
            end++;
        }

        return text[commandStart..end].Trim('"');
    }
}
