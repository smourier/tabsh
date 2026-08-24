namespace Tabsh.Interop;

// one process found inside a job, as much as could be learned about it.
internal sealed class JobProcess(uint processId, string imagePath, string? commandLine, string? unavailable = null)
{
    public uint ProcessId { get; } = processId;

    // empty when the process could not be opened, which is what an elevated one looks like from here.
    public string ImagePath { get; } = imagePath;

    // null when Windows would not say. It is a Windows 8.1 and later question, and an answer nobody is owed.
    public string? CommandLine { get; } = commandLine;

    // being unable to read a process and the process having ended are not the same thing,
    // and a list about to be killed is the wrong place to guess between them.
    public string? Unavailable { get; } = unavailable;

    // the most informative thing there is to show, which is the command line where there is one.
    public string Description
    {
        get
        {
            if (!string.IsNullOrEmpty(CommandLine))
                return CommandLine;

            if (ImagePath.Length > 0)
                return ImagePath;

            return Unavailable ?? Res.ProcessUnreadable;
        }
    }
}
