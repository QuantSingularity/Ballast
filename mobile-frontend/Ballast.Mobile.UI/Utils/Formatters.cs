using System.Globalization;

namespace Ballast.Mobile.UI.Utils;

public static class Formatters
{
    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["USD"] = "$",
        ["EUR"] = "\u20AC",
        ["GBP"] = "\u00A3",
        ["JPY"] = "\u00A5",
        ["CHF"] = "CHF "
    };

    public static string Money(decimal value, string currency, int digits = 0)
    {
        string symbol = Symbols.GetValueOrDefault(currency, currency + " ");
        string number = Math.Abs(value).ToString($"N{digits}", CultureInfo.InvariantCulture);
        return value < 0 ? $"-{symbol}{number}" : $"{symbol}{number}";
    }

    public static string Compact(double value)
    {
        double abs = Math.Abs(value);
        string sign = value < 0 ? "-" : "";
        if (abs >= 1e9) return $"{sign}{(abs / 1e9).ToString("F1", CultureInfo.InvariantCulture)}B";
        if (abs >= 1e6) return $"{sign}{(abs / 1e6).ToString("F1", CultureInfo.InvariantCulture)}M";
        if (abs >= 1e3) return $"{sign}{(abs / 1e3).ToString("F0", CultureInfo.InvariantCulture)}K";
        return $"{sign}{abs.ToString("F0", CultureInfo.InvariantCulture)}";
    }

    public static string Pct(double value, int digits = 0) =>
        (value * 100).ToString($"F{digits}", CultureInfo.InvariantCulture) + "%";

    public static string ShortDate(DateOnly date) => date.ToString("MMM d", CultureInfo.InvariantCulture);

    public static string Tone(string status) => status switch
    {
        "Pass" or "Ok" or "Approved" or "Released" => "good",
        "Warning" or "PendingApproval" or "Draft" => "warn",
        "Breach" or "Rejected" or "Cancelled" => "bad",
        _ => "neutral"
    };

    public static string Humanize(string status)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < status.Length; i++)
        {
            if (i > 0 && char.IsUpper(status[i]) && char.IsLower(status[i - 1])) sb.Append(' ');
            sb.Append(status[i]);
        }
        return sb.ToString();
    }
}
