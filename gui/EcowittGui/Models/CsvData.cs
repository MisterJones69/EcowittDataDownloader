using System.Globalization;
using System.Text.RegularExpressions;

namespace EcowittGui.Models;

/// <summary>Kind of CSV file the gateway writes to its SD card.</summary>
public enum SdFileKind
{
    /// <summary>YYYYMM{A..Z}.csv: main station (indoor, outdoor, wind, rain, pressure, solar).</summary>
    Main,
    /// <summary>YYYYMMAllsensors_{A..Z}.csv: all channel / add-on sensors.</summary>
    AllSensors,
    /// <summary>Any other CSV file.</summary>
    Other,
}

/// <summary>All parts (A, B, C, ...) of one month of one file kind; read and shown merged.</summary>
public sealed class SdMonthSet
{
    public required SdFileKind Kind { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public required IReadOnlyList<FileInfo> Files { get; init; }

    public string Label => Kind == SdFileKind.Other
        ? Path.GetFileName(Files[0].FullName)
        : new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"));

    public string Details
    {
        get
        {
            var size = Files.Sum(f => f.Length);
            var parts = Files.Count == 1 ? "1 Datei" : $"{Files.Count} Teile";
            return $"{parts}, {FormatSize(size)}";
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0} KB";
        }
        return $"{bytes} B";
    }
}

public static partial class SdCatalog
{
    [GeneratedRegex(@"^(\d{4})(\d{2})(Allsensors_)?([A-Za-z])\.csv$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameRegex();

    /// <summary>Groups all CSV files below <paramref name="dir"/> by kind and month, newest month first.</summary>
    public static List<SdMonthSet> Scan(string dir)
    {
        var result = new List<SdMonthSet>();
        if (!Directory.Exists(dir))
        {
            return result;
        }
        var groups = new Dictionary<(SdFileKind, int, int), List<FileInfo>>();
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.csv", SearchOption.AllDirectories))
        {
            var m = FileNameRegex().Match(file.Name);
            int month = m.Success ? int.Parse(m.Groups[2].Value) : 0;
            if (!m.Success || month < 1 || month > 12)
            {
                result.Add(new SdMonthSet { Kind = SdFileKind.Other, Files = [file] });
                continue;
            }
            var kind = m.Groups[3].Success ? SdFileKind.AllSensors : SdFileKind.Main;
            var key = (kind, int.Parse(m.Groups[1].Value), month);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }
            list.Add(file);
        }
        foreach (var ((kind, year, month), files) in groups)
        {
            result.Add(new SdMonthSet
            {
                Kind = kind,
                Year = year,
                Month = month,
                Files = files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            });
        }
        return result
            .OrderBy(s => s.Kind)
            .ThenByDescending(s => s.Year)
            .ThenByDescending(s => s.Month)
            .ThenBy(s => s.Label)
            .ToList();
    }
}

/// <summary>Column of a loaded CSV table with what the GUI derived about it.</summary>
public sealed class CsvColumn
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    /// <summary>Name plus the user-assigned sensor name, if known (e.g. "CH1 Temperature(°C) – Pool").</summary>
    public required string DisplayName { get; init; }
    /// <summary>No value in any row ("--" or blank): the sensor does not exist.</summary>
    public bool IsEmpty { get; init; }
    /// <summary>All values parse as numbers, so the column can be charted.</summary>
    public bool IsNumeric { get; init; }
}

/// <summary>One month (all parts merged) of a gateway CSV file.</summary>
public sealed class CsvTable
{
    public required IReadOnlyList<CsvColumn> Columns { get; init; }
    /// <summary>Rows sorted by time; each row has one value per column (missing values are "").</summary>
    public required IReadOnlyList<string[]> Rows { get; init; }
    /// <summary>Time of each row (from the "Time" column), DateTime.MinValue if unparsable.</summary>
    public required IReadOnlyList<DateTime> Times { get; init; }

    public static bool IsMissing(string value) => value.Length == 0 || value == "--";

    public static double ParseNumber(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    /// <summary>Reads and merges the files; rows with the same timestamp are kept once.</summary>
    public static CsvTable Load(IEnumerable<string> files, ChannelNames names)
    {
        var headers = new List<string>();
        var headerIndex = new Dictionary<string, int>();
        var rawRows = new List<(string[] header, string[] values)>();

        foreach (var file in files)
        {
            using var reader = new StreamReader(file, System.Text.Encoding.UTF8);
            var headerLine = reader.ReadLine();
            if (headerLine == null)
            {
                continue;
            }
            var header = SplitLine(headerLine);
            foreach (var h in header)
            {
                if (!headerIndex.ContainsKey(h))
                {
                    headerIndex[h] = headers.Count;
                    headers.Add(h);
                }
            }
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length > 0)
                {
                    rawRows.Add((header, SplitLine(line)));
                }
            }
        }

        // map every row onto the merged header
        int timeCol = headerIndex.GetValueOrDefault("Time", -1);
        int stampCol = headerIndex.GetValueOrDefault("Timestamp", -1);
        var seen = new HashSet<string>();
        var rows = new List<(DateTime time, string[] row)>(rawRows.Count);
        foreach (var (header, values) in rawRows)
        {
            var row = new string[headers.Count];
            Array.Fill(row, "");
            for (int i = 0; i < header.Length && i < values.Length; i++)
            {
                row[headerIndex[header[i]]] = values[i];
            }
            var key = stampCol >= 0 ? row[stampCol] : (timeCol >= 0 ? row[timeCol] : null);
            if (key != null && !seen.Add(key))
            {
                continue;
            }
            var time = timeCol >= 0 && DateTime.TryParseExact(row[timeCol], ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;
            rows.Add((time, row));
        }
        rows.Sort((a, b) => a.time.CompareTo(b.time));

        var columns = new List<CsvColumn>(headers.Count);
        for (int c = 0; c < headers.Count; c++)
        {
            bool empty = true, numeric = true;
            foreach (var (_, row) in rows)
            {
                var v = row[c];
                if (IsMissing(v))
                {
                    continue;
                }
                empty = false;
                if (numeric && double.IsNaN(ParseNumber(v)))
                {
                    numeric = false;
                    break;
                }
            }
            var (category, display) = ColumnCategories.Classify(headers[c], names);
            columns.Add(new CsvColumn
            {
                Index = c,
                Name = headers[c],
                Category = category,
                DisplayName = display,
                IsEmpty = empty,
                IsNumeric = !empty && numeric && c != stampCol,
            });
        }

        return new CsvTable
        {
            Columns = columns,
            Rows = rows.Select(r => r.row).ToList(),
            Times = rows.Select(r => r.time).ToList(),
        };
    }

    private static string[] SplitLine(string line)
    {
        // the gateway writes plain comma separated values with a trailing comma and no quoting
        var parts = line.Split(',');
        if (parts.Length > 0 && parts[^1].Length == 0)
        {
            Array.Resize(ref parts, parts.Length - 1);
        }
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i].Trim();
        }
        return parts;
    }
}

/// <summary>Groups gateway CSV columns by sensor (type) for the column picker.</summary>
public static partial class ColumnCategories
{
    [GeneratedRegex(@"^CH(\d+) ")]
    private static partial Regex Wh31Regex();

    [GeneratedRegex(@"^WN34 CH(\d+)")]
    private static partial Regex Wn34Regex();

    [GeneratedRegex(@"CH(\d+)")]
    private static partial Regex ChannelRegex();

    /// <summary>Display order of the categories; unknown ones go last, alphabetically.</summary>
    public static int Order(string category)
    {
        string[] fixedOrder = ["Zeit", "Außen", "Innen", "Wind", "Luftdruck", "Sonne & UV", "Regen", "Regen (Piezo)"];
        int i = Array.IndexOf(fixedOrder, category);
        return i >= 0 ? i : 100;
    }

    public static (string category, string displayName) Classify(string name, ChannelNames names)
    {
        static (string, string) Plain(string category, string name) => (category, name);

        if (name is "Time" or "Timestamp")
        {
            return Plain("Zeit", name);
        }

        // channel sensors (Allsensors files); named after the sensor names from live_data.json
        var m = Wh31Regex().Match(name);
        if (m.Success)
        {
            int ch = int.Parse(m.Groups[1].Value);
            var label = names.Wh31.TryGetValue(ch, out var n) ? $"CH{ch} · {n}" : $"CH{ch}";
            return (label, names.Wh31.ContainsKey(ch) ? $"{name} – {n}" : name);
        }
        m = Wn34Regex().Match(name);
        if (m.Success)
        {
            int ch = int.Parse(m.Groups[1].Value);
            var label = names.Wn34.TryGetValue(ch, out var n) ? $"WN34 CH{ch} · {n}" : "WN34 Temperaturfühler";
            return (label, names.Wn34.ContainsKey(ch) ? $"{name} – {n}" : name);
        }
        if (name.StartsWith("WH35", StringComparison.Ordinal))
        {
            return Plain("Blattnässe (WH35)", name);
        }
        if (name.StartsWith("Thunder", StringComparison.Ordinal))
        {
            return Plain("Gewitter", name);
        }
        if (name.StartsWith("AQIN", StringComparison.Ordinal))
        {
            return Plain("Luftqualität (AQIN)", name);
        }
        if (name.StartsWith("Soil", StringComparison.Ordinal))
        {
            var ch = ChannelRegex().Match(name);
            return Plain(ch.Success ? $"Boden CH{ch.Groups[1].Value}" : "Boden", name);
        }
        if (name.StartsWith("Water", StringComparison.Ordinal))
        {
            return Plain("Leckage", name);
        }
        if (name.StartsWith("PM2.5", StringComparison.Ordinal))
        {
            return Plain("Feinstaub", name);
        }
        if (name.StartsWith("LDS", StringComparison.Ordinal))
        {
            return Plain("Abstand (LDS)", name);
        }
        if (name.StartsWith("WQT", StringComparison.Ordinal))
        {
            return Plain("Wasserqualität (WQT)", name);
        }

        // main station files
        if (name.StartsWith("Indoor", StringComparison.Ordinal))
        {
            return Plain("Innen", name);
        }
        if (name.StartsWith("Piezo", StringComparison.Ordinal))
        {
            return Plain("Regen (Piezo)", name);
        }
        if (name.Contains("Rain", StringComparison.Ordinal))
        {
            return Plain("Regen", name);
        }
        if (name.StartsWith("Wind", StringComparison.Ordinal) || name.StartsWith("windDir", StringComparison.Ordinal)
            || name.StartsWith("Gust", StringComparison.Ordinal))
        {
            return Plain("Wind", name);
        }
        if (name.Contains("Pressure", StringComparison.Ordinal))
        {
            return Plain("Luftdruck", name);
        }
        if (name.StartsWith("Solar", StringComparison.Ordinal) || name.StartsWith("UV", StringComparison.Ordinal))
        {
            return Plain("Sonne & UV", name);
        }
        if (name.StartsWith("Outdoor", StringComparison.Ordinal) || name.StartsWith("Dew", StringComparison.Ordinal)
            || name.StartsWith("Feels", StringComparison.Ordinal) || name.StartsWith("BGT", StringComparison.Ordinal)
            || name.StartsWith("WBGT", StringComparison.Ordinal) || name.StartsWith("VPD", StringComparison.Ordinal))
        {
            return Plain("Außen", name);
        }
        return Plain("Sonstige", name);
    }
}
