namespace Tabsh;

internal static class FileCommands
{
    public static int List(BuiltinContext context)
    {
        var options = new ListOptions();
        var targets = new List<string>();

        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                targets.Add(argument);
                continue;
            }

            var option = argument[1..];
            switch (char.ToUpperInvariant(option.Length > 0 ? option[0] : ' '))
            {
                case 'B':
                    options.Bare = true;
                    break;

                case 'S':
                    options.Recurse = true;
                    break;

                case 'A':
                    options.All = true;
                    break;

                case 'R':
                    options.Streams = true;
                    break;

                case 'O':
                    options.Order = option[1..].TrimStart(':');
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        // standing somewhere in the shell namespace,
        // a dir with nothing named is a listing of that, not of the directory the process is still sitting in.
        if (targets.Count == 0 && context.Environment.Location.IsVirtual)
            return ListShell(context, context.Environment.Location, options);

        if (targets.Count == 0)
        {
            targets.Add(".");
        }

        var code = 0;
        foreach (var target in targets)
        {
            var listed = Resolve(context, target) is ShellLocation location
                ? ListShell(context, location, options)
                : ListTarget(context, target, options);

            code = listed != 0 ? 1 : code;
        }

        return code;
    }

    // the namespace folder a target names, or null when the file system should answer instead.
    // A bare name is read in the namespace only while standing in it, so a wildcard still reaches the file system.
    private static ShellLocation? Resolve(BuiltinContext context, string target)
    {
        var location = context.Environment.Location;
        if (target.StartsWith(_namespaceRoot))
            return Entered(new ShellLocation(), target[_namespaceRoot.Length..].TrimStart(':'), fromRoot: true);

        if (!location.IsVirtual || Path.IsPathFullyQualified(target))
            return null;

        var fromRoot = target.Length > 0 && (target[0] == Path.DirectorySeparatorChar || target[0] == Path.AltDirectorySeparatorChar);
        return Entered(new ShellLocation(location), target, fromRoot);
    }

    private static ShellLocation? Entered(ShellLocation location, string path, bool fromRoot) => location.EnterPath(path, fromRoot) ? location : null;

    // a shell folder has names and little else,
    // and a size or free space means nothing for This PC, so those columns are left empty rather than filled with a nought.
    private static int ListShell(BuiltinContext context, ShellLocation location, ListOptions options)
    {
        var children = location.Children();
        children.Sort((a, b) =>
        {
            var byKind = b.IsFolder.CompareTo(a.IsFolder);
            return byKind != 0 ? byKind : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        if (options.Bare)
        {
            foreach (var child in children)
            {
                context.Output.WriteLine(child.Name);
            }

            return 0;
        }

        context.Output.WriteLine();
        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryOf, location.Path));
        context.Output.WriteLine();

        foreach (var child in children)
        {
            var date = child.Modified?.ToString("dd/MM/yyyy  HH:mm", CultureInfo.CurrentCulture) ?? string.Empty;
            var size = Marker(child) is string marker
                ? (_markerIndent + marker).PadRight(_sizeWidth)
                : child.Size.ToString("N0", CultureInfo.CurrentCulture).PadLeft(_sizeWidth);

            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.ShellEntryLine, date, size, child.Name));
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.ShellItemCount, children.Count));
        return 0;
    }

    // null when the entry has a real size to put there. Not everything in a shell folder is a folder or a file,
    // the desktop holds a Control Panel that only opens and reports no SFGAO_FOLDER.
    private static string? Marker(ShellChild child)
    {
        if (child.IsFolder)
            return Res.DirectoryMarker;

        return child.Size < 0 ? Res.ItemMarker : null;
    }

    private static int ListTarget(BuiltinContext context, string target, ListOptions options)
    {
        string full;
        try
        {
            full = ShellPath.Resolve(target, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException exception)
        {
            return context.Fail(exception.Message);
        }

        string directory;
        string pattern;
        if (Directory.Exists(full))
        {
            directory = full;
            pattern = "*";
        }
        else
        {
            directory = Path.GetDirectoryName(full) ?? full;
            pattern = Path.GetFileName(full);
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
        }

        if (!Directory.Exists(directory))
            return context.Fail(Res.FileNotFoundShort);

        return ListDirectory(context, directory, pattern, options);
    }

    private static int ListDirectory(BuiltinContext context, string directory, string pattern, ListOptions options)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(directory).EnumerateFileSystemInfos(pattern)
                .Where(e => options.All || (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }

        Sort(entries, options.Order);

        if (options.Bare)
        {
            foreach (var entry in entries)
            {
                context.Output.WriteLine(options.Recurse ? entry.FullName : entry.Name);
            }
        }
        else
        {
            WriteHeader(context, directory);

            long bytes = 0;
            var files = 0;
            var directories = 0;
            foreach (var entry in entries)
            {
                var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                var size = Marker(entry, isDirectory);
                context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryEntryLine, entry.LastWriteTime, size, Name(entry)));

                if (options.Streams)
                {
                    WriteStreams(context, entry);
                }

                if (isDirectory)
                {
                    directories++;
                }
                else
                {
                    files++;
                    bytes += entry is FileInfo counted ? counted.Length : 0;
                }
            }

            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.FileCountSummary, files, bytes));
            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryCountSummary, directories, FreeSpace(directory)));
        }

        if (!options.Recurse)
            return 0;

        foreach (var child in SafeSubdirectories(directory))
        {
            ListDirectory(context, child, pattern, options);
        }

        return 0;
    }

    // the size column, or what stands in for it: a directory says so, and a reparse point says which kind it is,
    // because a junction and a symbolic link are not the same thing however alike they look here.
    private static string Marker(FileSystemInfo entry, bool isDirectory)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            var marker = FileLink.KindOf(entry.FullName) switch
            {
                FileLinkKind.Junction => Res.JunctionMarker,
                FileLinkKind.Symlink => isDirectory ? Res.SymlinkDirectoryMarker : Res.SymlinkMarker,
                _ => isDirectory ? Res.DirectoryMarker : null,
            };

            if (marker != null)
                return (_markerIndent + marker).PadRight(_sizeWidth);
        }

        if (isDirectory)
            return (_markerIndent + Res.DirectoryMarker).PadRight(_sizeWidth);

        return (entry is FileInfo file ? file.Length : 0).ToString("N0", CultureInfo.CurrentCulture).PadLeft(_sizeWidth);
    }

    // a link with nothing to say about where it points is a link you have to go and look up.
    private static string Name(FileSystemInfo entry)
    {
        var target = entry.LinkTarget;
        if (string.IsNullOrEmpty(target))
            return entry.Name;

        return entry.Name + string.Format(CultureInfo.CurrentCulture, Res.LinkTargetSuffix, target);
    }

    private static void WriteStreams(BuiltinContext context, FileSystemInfo entry)
    {
        // a directory has streams of its own and they are worth the same look,
        // but walking into a reparse point to find them would be walking somewhere else entirely.
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            return;

        foreach (var stream in FileStreams.Of(entry.FullName))
        {
            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.StreamLine, stream.Size, entry.Name + stream.Name));
        }
    }

    private const string _namespaceRoot = "@";
    private const int _sizeWidth = 19;
    private const string _markerIndent = "    ";

    private static void WriteHeader(BuiltinContext context, string directory)
    {
        context.Output.WriteLine();
        var root = Path.GetPathRoot(directory);
        if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
        {
            try
            {
                var drive = new DriveInfo(root);
                context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.VolumeInDrive, root[0], drive.VolumeLabel));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // an unlabelled or unavailable volume is not worth a message.
            }
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryOf, directory));
        context.Output.WriteLine();
    }

    private static long FreeSpace(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(directory);
            return string.IsNullOrEmpty(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void Sort(List<FileSystemInfo> entries, string order)
    {
        var descending = order.StartsWith('-');
        var key = descending ? order[1..] : order;

        Comparison<FileSystemInfo> comparison = key.ToUpperInvariant() switch
        {
            "S" => (a, b) => SizeOf(a).CompareTo(SizeOf(b)),
            "D" => (a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime),
            "E" => (a, b) => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),

            // the default puts directories first, which is the order that makes a listing useful for getting around.
            _ => (a, b) =>
            {
                var byKind = IsDirectory(b).CompareTo(IsDirectory(a));
                return byKind != 0 ? byKind : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            },
        };

        entries.Sort(descending ? (a, b) => comparison(b, a) : comparison);
    }

    private static bool IsDirectory(FileSystemInfo entry) => (entry.Attributes & FileAttributes.Directory) != 0;

    private static long SizeOf(FileSystemInfo entry) => entry is FileInfo file ? file.Length : 0;

    public static int Type(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        var code = 0;
        foreach (var argument in context.Arguments)
        {
            var matches = Expand(context.Environment, argument, includeDirectories: false);
            if (matches.Count == 0)
            {
                code = context.Fail(string.Format(CultureInfo.CurrentCulture, Res.FileNotFound, argument));
                continue;
            }

            foreach (var match in matches)
            {
                try
                {
                    using var reader = new StreamReader(match, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        context.Output.WriteLine(line);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    code = context.Fail(exception.Message);
                }
            }
        }

        return code;
    }

    public static int Copy(BuiltinContext context) => Transfer(context, move: false);

    public static int Move(BuiltinContext context) => Transfer(context, move: true);

    private static int Transfer(BuiltinContext context, bool move)
    {
        var arguments = context.Arguments.Where(a => !a.StartsWith('/')).ToList();
        if (arguments.Count < 2)
            return context.Fail(Res.SyntaxIncorrect);

        var destination = ShellPath.Resolve(arguments[^1], context.Environment.CurrentDirectory);
        var intoDirectory = Directory.Exists(destination) || arguments.Count > 2;
        var count = 0;

        for (var i = 0; i < arguments.Count - 1; i++)
        {
            var matches = Expand(context.Environment, arguments[i], includeDirectories: move);
            if (matches.Count == 0)
                return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.FileNotFound, arguments[i]));

            foreach (var source in matches)
            {
                var target = intoDirectory ? Path.Combine(destination, Path.GetFileName(source)) : destination;
                try
                {
                    if (move)
                    {
                        if (Directory.Exists(source))
                        {
                            Directory.Move(source, target);
                        }
                        else
                        {
                            File.Move(source, target, overwrite: true);
                        }
                    }
                    else
                    {
                        File.Copy(source, target, overwrite: true);
                    }

                    count++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return context.Fail(exception.Message);
                }
            }
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, move ? Res.FilesMoved : Res.FilesCopied, count));
        return 0;
    }

    public static int Rename(BuiltinContext context)
    {
        if (context.Arguments.Count < 2)
            return context.Fail(Res.SyntaxIncorrect);

        var source = ShellPath.Resolve(context.Arguments[0], context.Environment.CurrentDirectory);
        var directory = Path.GetDirectoryName(source);
        if (string.IsNullOrEmpty(directory))
            return context.Fail(Res.SyntaxIncorrect);

        // the second argument of a rename is a name, never a path, which is the one thing that separates it from move.
        var target = Path.Combine(directory, Path.GetFileName(context.Arguments[1]));
        try
        {
            if (Directory.Exists(source))
            {
                Directory.Move(source, target);
            }
            else
            {
                File.Move(source, target);
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }
    }

    public static int Delete(BuiltinContext context)
    {
        var force = false;
        var recurse = false;
        var patterns = new List<string>();
        foreach (var argument in context.Arguments)
        {
            if (argument.StartsWith('/'))
            {
                force = force || string.Equals(argument, "/f", StringComparison.OrdinalIgnoreCase);
                recurse = recurse || string.Equals(argument, "/s", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            patterns.Add(argument);
        }

        if (patterns.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        var code = 0;
        foreach (var pattern in patterns)
        {
            var matches = Expand(context.Environment, pattern, includeDirectories: false);
            if (recurse)
            {
                matches.AddRange(ExpandRecursive(context.Environment, pattern));
            }

            if (matches.Count == 0)
            {
                code = context.Fail(string.Format(CultureInfo.CurrentCulture, Res.CouldNotFind, pattern));
                continue;
            }

            foreach (var match in matches)
            {
                try
                {
                    if (force)
                    {
                        File.SetAttributes(match, FileAttributes.Normal);
                    }

                    File.Delete(match);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    code = context.Fail(exception.Message);
                }
            }
        }

        return code;
    }

    public static int MakeDirectory(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        foreach (var argument in context.Arguments)
        {
            try
            {
                Directory.CreateDirectory(ShellPath.Resolve(argument, context.Environment.CurrentDirectory));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return context.Fail(exception.Message);
            }
        }

        return 0;
    }

    public static int RemoveDirectory(BuiltinContext context)
    {
        var recurse = false;
        var targets = new List<string>();
        foreach (var argument in context.Arguments)
        {
            if (argument.StartsWith('/'))
            {
                recurse = recurse || string.Equals(argument, "/s", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            targets.Add(argument);
        }

        if (targets.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        foreach (var target in targets)
        {
            try
            {
                Directory.Delete(ShellPath.Resolve(target, context.Environment.CurrentDirectory), recurse);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return context.Fail(exception.Message);
            }
        }

        return 0;
    }

    private static List<string> Expand(ShellEnvironment environment, string pattern, bool includeDirectories)
    {
        string full;
        try
        {
            full = ShellPath.Resolve(pattern, environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return [];
        }

        if (File.Exists(full))
            return [full];

        if (includeDirectories && Directory.Exists(full))
            return [full];

        var directory = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(directory) || name.Length == 0 || !Directory.Exists(directory))
            return [];

        try
        {
            return includeDirectories
                ? Directory.EnumerateFileSystemEntries(directory, name).ToList()
                : Directory.EnumerateFiles(directory, name).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<string> ExpandRecursive(ShellEnvironment environment, string pattern)
    {
        var full = ShellPath.Resolve(pattern, environment.CurrentDirectory);
        var directory = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(directory) || name.Length == 0 || !Directory.Exists(directory))
            return [];

        var found = new List<string>();
        foreach (var child in SafeSubdirectories(directory))
        {
            try
            {
                found.AddRange(Directory.EnumerateFiles(child, name, SearchOption.AllDirectories));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // continue, an unreadable subtree does not stop the rest.
            }
        }

        return found;
    }
}
