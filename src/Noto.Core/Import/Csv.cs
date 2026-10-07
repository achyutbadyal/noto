using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Noto.Core.Import;

public static class Csv
{
    static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        MissingFieldFound = null,
        DetectDelimiter = false,
    };

    static readonly CsvConfiguration WriterConfig = new(CultureInfo.InvariantCulture) { NewLine = "\n" };

    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        try
        {
            using var parser = new CsvParser(new StringReader(text.TrimStart('\uFEFF')), Config);
            while (parser.Read()) rows.Add(parser.Record!.ToArray());
        }
        catch (BadDataException e)
        {
            throw new ImportFormatException(e.Message);
        }
        return rows;
    }

    // Rows as dictionaries keyed by header; short rows get empty strings.
    public static List<Dictionary<string, string>> ParseWithHeader(IReadOnlyList<string[]> rows, int headerIndex)
    {
        var header = rows[headerIndex];
        return rows.Skip(headerIndex + 1).Select(r =>
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Length; i++) d[header[i]] = i < r.Length ? r[i] : "";
            return d;
        }).ToList();
    }

    // Cells starting with = + - @ are prefixed so spreadsheets don't evaluate them as formulas.
    public static string Row(IEnumerable<string?> cells, bool guardFormulas = true)
    {
        using var sw = new StringWriter();
        using (var writer = new CsvWriter(sw, WriterConfig))
        {
            foreach (var cell in cells) writer.WriteField(Guard(cell, guardFormulas));
            writer.NextRecord();
        }
        return sw.ToString().TrimEnd('\n');
    }

    static string Guard(string? value, bool guardFormulas)
    {
        var v = value ?? "";
        return guardFormulas && v.Length > 0 && "=+-@\t\r".Contains(v[0]) ? "'" + v : v;
    }
}
