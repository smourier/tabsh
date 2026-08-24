namespace Tabsh;

internal sealed class BuiltinCommand(string name, string description, Func<BuiltinContext, int> handler)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public Func<BuiltinContext, int> Handler { get; } = handler;
}
