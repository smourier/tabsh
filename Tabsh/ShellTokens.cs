namespace Tabsh;

// the words a title or a tab colour may be written with, one public property per token.
// The help lists them by reading them back, so there is no second list to keep in step with this one.
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
internal sealed class ShellTokens(ShellEnvironment environment)
{
    private static readonly char[] _separators = ['\\', '/'];

    // what the prompt would show for $P, which is the namespace path when the shell is somewhere the file system is not.
    public string Path => environment.Location.IsVirtual ? environment.Location.Path : environment.CurrentDirectory;

    // the last part of it, which is what a window with no room for the rest is better off showing.
    // A root has no last part, so it stands for itself with the separator taken off.
    public string Name
    {
        get
        {
            var path = Path;
            var last = Segment(path, out _);
            return last.Length > 0 ? last : path.TrimEnd(_separators);
        }
    }

    public string Parent
    {
        get
        {
            var path = Path;
            Segment(path, out var start);
            return start <= 0 ? string.Empty : Segment(path[..start], out _);
        }
    }

    // empty where there is no drive, which is every namespace place that is not a folder on one.
    public string Drive => Path.Length >= 2 && Path[1] == ':' ? Path[..2] : string.Empty;

#pragma warning disable CA1822 // Mark members as static
    public string User => _user;
    public string Domain => _domain;
    public string Machine => _machine;
    public string Product => _product;
    public string Version => _version;
    public string Pid => _pid;

    // the word only when it is deserved, so a template written with it says nothing at all when it is not.
    public string Admin => _elevated ? Res.TitleAdministrator : string.Empty;

    public string LastExitCode => environment.LastExitCode.ToString(CultureInfo.CurrentCulture);
    public string Date => DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
    public string Time => DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
#pragma warning restore CA1822 // Mark members as static

    // the parts of where you are, the drive counting as the first of them.
    // "{Segment[1]}" is what tells one project tree from another, whatever either of them happens to hold below.
    public string Segment(int index)
    {
        var parts = Parts();
        return index >= 0 && index < parts.Length ? parts[index] : string.Empty;
    }

    // where you are cut back to its first parts, so everything under one tree answers with the same words.
    // "{Upto[1]}" names the same tree whether you are standing in it or six folders below it.
    public string Upto(int count)
    {
        if (count < 0)
            return string.Empty;

        var parts = Parts();
        return string.Join(_separators[0], parts.Take(Math.Min(count + 1, parts.Length)));
    }

    private string[] Parts() => Path.Split(_separators, StringSplitOptions.RemoveEmptyEntries);

    private static string Segment(string path, out int start)
    {
        var trimmed = path.TrimEnd(_separators);
        start = trimmed.LastIndexOfAny(_separators);
        return start < 0 ? trimmed : trimmed[(start + 1)..];
    }

    private static readonly string _user = Environment.UserName;
    private static readonly string _domain = Environment.UserDomainName;
    private static readonly string _machine = Environment.MachineName;
    private static readonly string _pid = Environment.ProcessId.ToString(CultureInfo.CurrentCulture);
    private static readonly string _product = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? string.Empty;
    private static readonly string _version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? string.Empty;
    private static readonly bool _elevated = Elevation.IsElevated();
}
