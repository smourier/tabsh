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

    // the /a selectors, D R H A S I L O, each of which a leading "-" turns around.
    public string Attributes { get; set; } = string.Empty;

    // /t, which of the three times is shown and sorted on. W written, C created, A accessed.
    public char TimeField { get; set; } = 'W';

    public bool Wide { get; set; }

    // /d is /w read down the columns rather than across the rows.
    public bool DownColumns { get; set; }

    public bool Pause { get; set; }
    public bool Lower { get; set; }
    public bool Owner { get; set; }
    public bool ShortNames { get; set; }
}
