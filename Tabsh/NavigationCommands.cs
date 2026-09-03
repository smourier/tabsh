namespace Tabsh;

internal static class NavigationCommands
{
    public static int ChangeDirectory(BuiltinContext context)
    {
        var arguments = context.Arguments.Where(a => !IsDriveSwitch(a)).ToList();
        if (arguments.Count == 0)
        {
            context.Output.WriteLine(Where(context));
            return 0;
        }

        var name = string.Join(' ', arguments);
        var target = name == "-" ? context.Environment.PreviousDirectory : name;
        try
        {
            context.Environment.ChangeDirectory(target);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }
    }

    private static bool IsDriveSwitch(string argument) =>
        argument.Length == 2 && (argument[0] == '/' || argument[0] == '-') && (argument[1] == 'd' || argument[1] == 'D');

    public static int PushDirectory(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            foreach (var entry in context.Environment.DirectoryStack)
            {
                context.Output.WriteLine(entry);
            }

            return 0;
        }

        var current = context.Environment.CurrentDirectory;
        try
        {
            context.Environment.ChangeDirectory(string.Join(' ', context.Arguments));
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }

        context.Environment.DirectoryStack.Push(current);
        return 0;
    }

    public static int PopDirectory(BuiltinContext context)
    {
        if (context.Environment.DirectoryStack.Count == 0)
            return 0;

        var target = context.Environment.DirectoryStack.Pop();
        try
        {
            context.Environment.ChangeDirectory(target);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }
    }

    public static int PrintDirectory(BuiltinContext context)
    {
        context.Output.WriteLine(Where(context));
        return 0;
    }

    private static string Where(BuiltinContext context) => context.Environment.Location.IsVirtual ? context.Environment.Location.Path : context.Environment.CurrentDirectory;
}
