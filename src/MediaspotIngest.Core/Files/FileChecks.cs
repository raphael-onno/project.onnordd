using System.Globalization;
using System.Text;
using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.Files;

/// <summary>
/// The checks the old tool made while moving files to temp (tutorial NRJ12, step 4):
/// the media size against the size announced, and for EBU STL subtitles the count announced in the header
/// against the subtitles really in the file. They warn; they do not block.
/// </summary>
public static class FileChecks
{
    private const int GsiBlockSize = 1024;
    private const int TtiBlockSize = 128;

    /// <summary>A warning about the file at <paramref name="path"/>, or null when everything matches.</summary>
    public static string? Check(IngestItem item, string path)
    {
        var warnings = new List<string>();
        if (SizeWarning(item) is { } size)
            warnings.Add(size);
        if (!item.IsFolder && Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase) && StlWarning(path) is { } stl)
            warnings.Add(stl);
        return warnings.Count == 0 ? null : string.Join(" ; ", warnings);
    }

    /// <summary>
    /// The registry rounds sizes to the megabyte and does not say whether "Go" means 10^9 or 2^30 bytes, so a size
    /// within 1 % of either reading passes. Retrospect's export truncates sizes over 4 GB: no check for those.
    /// </summary>
    public static string? SizeWarning(IngestItem item)
    {
        if (item.ActualSizeBytes is not { } actual || item.ExpectedSizeBytes is not { } expected || expected <= 0)
            return null;
        if (!string.Equals(item.Source, "ltfs", StringComparison.OrdinalIgnoreCase))
            return null;

        var gb = expected / (1024.0 * 1024 * 1024);
        double[] readings = [gb * 1024 * 1024 * 1024, gb * 1_000_000_000];
        if (readings.Any(r => Math.Abs(actual - r) <= Math.Max(r * 0.01, 2 * 1024 * 1024)))
            return null;

        return $"taille {actual / 1e9:0.###} Go au lieu de ~{gb:0.###} Go annoncés";
    }

    /// <summary>EBU Tech 3264: a 1024-byte GSI header then 128-byte TTI blocks; TNB (offset 238) and TNS (offset 243) count them.</summary>
    public static string? StlWarning(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < GsiBlockSize)
                return "STL tronqué (en-tête incomplet)";

            var announcedBlocks = ReadNumber(bytes, 238, 5);
            var announcedSubtitles = ReadNumber(bytes, 243, 5);
            if ((bytes.Length - GsiBlockSize) % TtiBlockSize != 0)
                return "STL tronqué (taille non multiple de 128 octets)";

            var blocks = (bytes.Length - GsiBlockSize) / TtiBlockSize;
            var subtitles = 0;
            for (var i = 0; i < blocks; i++)
            {
                var offset = GsiBlockSize + i * TtiBlockSize;
                var extensionBlock = bytes[offset + 3];
                var comment = bytes[offset + 15];
                // EBN 0xFF marks the last (often only) block of a subtitle; comment blocks are not subtitles.
                if (extensionBlock == 0xFF && comment == 0)
                    subtitles++;
            }

            if (announcedBlocks is { } tnb && tnb != blocks)
                return $"STL : {blocks} blocs au lieu des {tnb} annoncés";
            if (announcedSubtitles is { } tns && tns != subtitles)
                return $"STL : {subtitles} sous-titres au lieu des {tns} annoncés";
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"STL illisible ({e.Message})";
        }
    }

    private static int? ReadNumber(byte[] bytes, int offset, int length) =>
        int.TryParse(Encoding.ASCII.GetString(bytes, offset, length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
