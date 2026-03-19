namespace FlipperZero.NET.Commands.Ui;

/// <summary>
/// Flipper screen font selection for <see cref="UiDrawStrCommand"/>.
/// </summary>
public enum UiFont : byte
{
    /// <summary>Primary font — bold, larger glyphs.</summary>
    Primary = 0,

    /// <summary>Secondary font — regular, smaller glyphs (default).</summary>
    Secondary = 1,

    /// <summary>Big number font — large numerals.</summary>
    BigNumbers = 2,
}
