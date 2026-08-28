namespace Tabsh;

// a colour worked out from a piece of text, the same one every time, on every machine and after every restart.
// Nothing random goes near it, so a window that was green this morning is green again tomorrow.
internal static class StableColor
{
    private const uint _offsetBasis = 2166136261;
    private const uint _prime = 16777619;

    // a tab is read at a glance and has text over it, so the lightness stays low and the saturation short of shouting.
    private const float _saturation = 0.55f;

    // the hue is taken in steps wide enough to see rather than off a continuous circle.
    // A circle hands out neighbours a shade apart, which reads as a drawing fault where two plain colours read as two.
    private const uint _hues = 60;
    private static readonly float[] _brightnesses = [0.30f, 0.40f, 0.50f];

    public static string Of(string text, uint seed, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(text);

        // two paths that Windows would call the same name deserve the same colour, so the folding follows /case.
        var folded = comparison is StringComparison.OrdinalIgnoreCase or StringComparison.CurrentCultureIgnoreCase or StringComparison.InvariantCultureIgnoreCase
            ? text.ToUpperInvariant()
            : text;

        // the hue comes off the low bits and the lightness off the high ones,
        // so two names near each other in one are still apart in the other, which is what makes the hundred and eighty.
        var hash = Hash(folded, seed);
        var hue = hash % _hues / (float)_hues;
        var brightness = _brightnesses[hash / _hues % (uint)_brightnesses.Length];
        var color = new Hsl(hue, _saturation, brightness).ToD3DCOLORVALUE();
        return string.Create(CultureInfo.InvariantCulture, $"#{color.BR:x2}{color.BG:x2}{color.BB:x2}");
    }

    // FNV-1a over the UTF-16 units, written out here rather than taken from the framework.
    // String.GetHashCode is seeded per process, and a colour off it would change every time the shell started.
    private static uint Hash(string text, uint seed)
    {
        var hash = _offsetBasis ^ seed;
        foreach (var c in text)
        {
            hash = (hash ^ (byte)c) * _prime;
            hash = (hash ^ (byte)(c >> 8)) * _prime;
        }

        return hash;
    }
}
