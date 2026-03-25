using System.Text.Json;

namespace KodaClaw.Cli;

/// <summary>
/// Dual-mode output: human-readable text or structured JSON.
/// </summary>
public static class OutputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void WriteJson(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    public static void WriteSuccess(string message) =>
        Console.WriteLine($"✓ {message}");

    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"✗ {message}");
        Console.ResetColor();
    }

    public static void WriteTable(IEnumerable<string[]> rows, string[] headers)
    {
        var allRows = rows.ToList();
        var widths = headers.Select((h, i) =>
            Math.Max(h.Length, allRows.Count > 0 ? allRows.Max(r => i < r.Length ? r[i].Length : 0) : 0)
        ).ToArray();

        var header = string.Join("  ", headers.Select((h, i) => h.PadRight(widths[i])));
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        foreach (var row in allRows)
        {
            Console.WriteLine(string.Join("  ", row.Select((c, i) => c.PadRight(widths[i]))));
        }
    }
}
