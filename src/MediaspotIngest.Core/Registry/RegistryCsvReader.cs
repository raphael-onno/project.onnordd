using System.Globalization;
using System.Text;

namespace MediaspotIngest.Core.Registry;

/// <summary>
/// Reads the registry export: a UTF-8, semicolon-separated CSV with a header row.
/// Columns are matched by header name, so their order does not matter and unknown ones are ignored.
/// </summary>
public static class RegistryCsvReader
{
    public static readonly string[] Header =
    [
        "elementId", "titleKey", "titleName", "relativePath", "category", "sizeGb", "isFolder", "fileCount",
        "source", "tapes", "recommendation", "decision",
        "type", "codec", "language", "ratio", "fps", "definition", "audio", "version", "date",
    ];

    public static IReadOnlyList<RegistryElement> Read(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        return Read(reader);
    }

    public static IReadOnlyList<RegistryElement> Read(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("The registry export is empty.");
        var columns = SplitLine(headerLine)
            .Select((name, index) => (name: name.Trim(), index))
            .ToDictionary(c => c.name, c => c.index, StringComparer.OrdinalIgnoreCase);

        foreach (var required in new[] { "elementId", "titleKey", "relativePath" })
        {
            if (!columns.ContainsKey(required))
                throw new InvalidDataException($"The registry export has no '{required}' column.");
        }

        var elements = new List<RegistryElement>();
        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = SplitLine(line);
            string? Get(string name) =>
                columns.TryGetValue(name, out var i) && i < fields.Count && fields[i].Length > 0 ? fields[i] : null;

            var id = Get("elementId") ?? throw new InvalidDataException($"Line {lineNumber}: no elementId.");
            elements.Add(new RegistryElement
            {
                Id = id,
                TitleKey = Get("titleKey") ?? "",
                TitleName = Get("titleName") ?? Get("titleKey") ?? "",
                RelativePath = (Get("relativePath") ?? "").Replace('/', '\\'),
                Category = int.TryParse(Get("category"), out var category) ? category : 0,
                SizeGb = double.TryParse(Get("sizeGb"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size : 0,
                IsFolder = Get("isFolder") is { } folder && (folder == "1" || bool.TryParse(folder, out var b) && b),
                FileCount = int.TryParse(Get("fileCount"), out var count) ? count : null,
                Source = Get("source") ?? "",
                Tapes = (Get("tapes") ?? "").Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Recommendation = Get("recommendation") ?? "",
                Decision = Get("decision"),
                Metadata = new RegistryMetadata
                {
                    Type = Get("type"),
                    Codec = Get("codec"),
                    Language = Get("language"),
                    Ratio = Get("ratio"),
                    FrameRate = Get("fps"),
                    Definition = Get("definition"),
                    Audio = Get("audio"),
                    Version = Get("version"),
                    Date = Get("date"),
                },
            });
        }

        return elements;
    }

    /// <summary>Semicolon-separated, with optional double quotes ("" escapes a quote) — file names may hold anything.</summary>
    internal static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ';')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
