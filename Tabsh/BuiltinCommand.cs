namespace Tabsh;

internal sealed class BuiltinCommand(string name, string description, Func<BuiltinContext, int> handler, Func<string>? details = null)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public Func<BuiltinContext, int> Handler { get; } = handler;

    // the lines "/?" adds under the description, for the one or two commands that have more to say than a line.
    // Asked for rather than stored, since what they list is worked out and not written down.
    public Func<string>? Details { get; } = details;
}
