using System.Globalization;

namespace TurfGuess.Core.Util;

public static class Percent
{
    /// <summary>
    /// Formats a share (0..1) as a percentage with one decimal, e.g. "47.9%". A share that would
    /// round to exactly 50.0% gets more decimals ("50.04%"), so a close round never reads as a tie.
    /// </summary>
    public static string Share(double share)
    {
        double value = share * 100;
        string text = "";
        for (int decimals = 1; decimals <= 4; decimals++)
        {
            text = value.ToString("F" + decimals, CultureInfo.InvariantCulture);
            if (double.Parse(text, CultureInfo.InvariantCulture) != 50.0) break;
        }
        return text + "%";
    }
}
