namespace Tabsh;

internal sealed class InterruptibleWriter(TextWriter inner, BuiltinContext context) : TextWriter(inner.FormatProvider)
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        context.ThrowIfInterrupted();
        inner.Write(value);
    }

    public override void Write(string? value)
    {
        context.ThrowIfInterrupted();
        inner.Write(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        context.ThrowIfInterrupted();
        inner.Write(buffer, index, count);
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        context.ThrowIfInterrupted();
        inner.Write(buffer);
    }

    public override void WriteLine()
    {
        context.ThrowIfInterrupted();
        inner.WriteLine();
    }

    public override void WriteLine(string? value)
    {
        context.ThrowIfInterrupted();
        inner.WriteLine(value);
    }

    public override void WriteLine(ReadOnlySpan<char> buffer)
    {
        context.ThrowIfInterrupted();
        inner.WriteLine(buffer);
    }

    public override void Flush() => inner.Flush();
}
