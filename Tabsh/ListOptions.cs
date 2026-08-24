namespace Tabsh;

// the switches dir was given, carried together rather than as a row of unexplained booleans down three call signatures.
internal sealed class ListOptions
{
    public bool Bare { get; set; }
    public bool Recurse { get; set; }
    public bool All { get; set; }

    // /r, the alternate data streams of every entry as well as the entries.
    public bool Streams { get; set; }

    // the /o sort key, empty for the default which puts directories first.
    public string Order { get; set; } = string.Empty;
}
