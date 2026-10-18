namespace Darklands.Content.Formats.Text;

/// <summary>
/// Signed bounds helper used by the MSG card scanner.
/// </summary>
/// <remarks>
/// The original helper (R029+0x4F76, clamp_range) compares signed words and returns upper, lower, or
/// the input value; this helper does the same for the data parser.
/// </remarks>
public static class MsgScannerBounds
{
    public static ushort ClampSignedWord(ushort value, ushort lowerBound, ushort upperBound)
    {
        var signedValue = unchecked((short)value);
        var signedLower = unchecked((short)lowerBound);
        var signedUpper = unchecked((short)upperBound);

        if (signedUpper < signedValue)
        {
            return upperBound;
        }

        return signedLower > signedValue ? lowerBound : value;
    }
}
