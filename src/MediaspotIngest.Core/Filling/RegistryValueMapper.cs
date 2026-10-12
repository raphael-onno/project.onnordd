using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaspotIngest.Core.Filling;

/// <summary>
/// Translates what the registry parsed from EuropaCorp's file names into Mediaspot metadata values.
/// Each method returns null when it cannot tell — the cell then stays empty for the user to fill.
/// </summary>
public static partial class RegistryValueMapper
{
    private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FR"] = "FRA", ["EN"] = "ENG", ["US"] = "ENG", ["UK"] = "ENG", ["DE"] = "DEU", ["GER"] = "DEU",
        ["ES"] = "ESP", ["SP"] = "ESP", ["IT"] = "ITA", ["PT"] = "POR", ["POR"] = "POR", ["NL"] = "DUT",
        ["RU"] = "RUS", ["JA"] = "JPN", ["JP"] = "JPN", ["ZH"] = "CHN", ["CN"] = "CHN", ["KO"] = "KOR",
        ["PL"] = "POL", ["TR"] = "TUR", ["AR"] = "ARA", ["HE"] = "HEB", ["SV"] = "SVE", ["DA"] = "DAN",
        ["NO"] = "NOR", ["FI"] = "FIN", ["CS"] = "CES", ["HU"] = "HUN", ["EL"] = "ELL", ["RO"] = "RON",
    };

    private static readonly string[] VideoExtensions = [".mov", ".mxf", ".mp4", ".m4v", ".avi", ".mpg", ".mpeg", ".mts", ".ts"];
    private static readonly string[] AudioExtensions = [".wav", ".aif", ".aiff", ".mp3", ".ac3", ".bwf"];
    private static readonly string[] SubtitleExtensions = [".stl", ".srt", ".scc", ".ttml", ".dfxp", ".itt", ".pac", ".cap", ".rtf", ".xml"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".tga", ".psd", ".bmp"];
    private static readonly string[] DocumentExtensions = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt"];

    /// <summary>"FR-XX" → audio FRA, subtitles none; "FR-EN" → audio FRA, subtitles ENG.</summary>
    public static (string? Audio, string? Subtitle) Languages(string? registryLanguage)
    {
        if (string.IsNullOrWhiteSpace(registryLanguage))
            return (null, null);

        var parts = registryLanguage.Split('-', StringSplitOptions.TrimEntries);
        return (Language(parts[0]), parts.Length > 1 ? Language(parts[1]) : null);
    }

    /// <summary>Two-letter (or legacy three-letter) code → Mediaspot three-letter code; XX means none.</summary>
    public static string? Language(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("XX", StringComparison.OrdinalIgnoreCase))
            return null;
        if (LanguageCodes.TryGetValue(code, out var mapped))
            return mapped;
        return code.Length == 3 ? code.ToUpperInvariant() : null;
    }

    /// <summary>"24 fps" → 24, "23 fps" → 23.98 (registry rounds 23.976 down), "29 fps" → 29.97.</summary>
    public static string? FrameRate(string? registryFps)
    {
        var match = NumberRegex().Match(registryFps ?? "");
        if (!match.Success)
            return null;

        return match.Value switch
        {
            "23" or "23.976" or "23.98" => "23.98",
            "29" or "29.97" => "29.97",
            "59" or "59.94" => "59.96",
            var v when double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d.ToString("0.##", CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>"2.39 (Scope)" → 2.39; "177" → 1.77.</summary>
    public static string? AspectRatio(string? registryRatio)
    {
        var match = NumberRegex().Match(registryRatio ?? "");
        if (!match.Success)
            return null;

        var text = match.Value;
        if (!text.Contains('.') && text.Length == 3)
            text = $"{text[0]}.{text[1..]}";

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            ? ratio.ToString("0.00", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>"1.85 (Flat)" → Flat, "2.39 (Scope)" → Scope.</summary>
    public static string? CinemaMode(string? registryRatio)
    {
        if (registryRatio is null)
            return null;
        if (registryRatio.Contains("scope", StringComparison.OrdinalIgnoreCase))
            return "Scope";
        if (registryRatio.Contains("flat", StringComparison.OrdinalIgnoreCase))
            return "Flat";
        return null;
    }

    public static string? Codec(string? registryCodec, string extension)
    {
        var codec = (registryCodec ?? "").ToUpperInvariant();
        if (codec.StartsWith("PR", StringComparison.Ordinal))
            return "ProRes";
        if (codec.Contains("XDCAM"))
            return "XDCAM";
        if (codec.Contains("H264") || codec.Contains("AVC"))
            return "H264";
        if (codec.Contains("DCP") || codec.Contains("J2K") || codec.Contains("JPEG2000"))
            return "JPEG 2000";

        return extension.ToLowerInvariant() switch
        {
            ".mp4" => "H264",
            ".wav" or ".bwf" => "PCM",
            ".stl" => "STL",
            ".scc" => "SCC",
            ".ttml" => "TTML",
            ".dfxp" => "DFXP",
            ".itt" => "ITT",
            ".pac" => "PAC",
            ".png" => "PNG",
            ".tif" or ".tiff" => "TIFF",
            ".jpg" or ".jpeg" => "JPEG",
            _ => null,
        };
    }

    public static string? Wrapper(string extension) => extension.ToLowerInvariant() switch
    {
        "" => null,
        ".aif" or ".aiff" => "AIF",
        ".jpeg" => "JPG",
        ".tif" => "TIFF",
        ".mpeg" => "MPG",
        var e => e.TrimStart('.').ToUpperInvariant(),
    };

    /// <summary>The Mediaspot asset type, from the element's shape and extension.</summary>
    public static string AssetType(string relativePath, bool isFolder, int category, string? registryCodec)
    {
        if (isFolder)
        {
            if ((registryCodec ?? "").Contains("DCP", StringComparison.OrdinalIgnoreCase) || relativePath.Contains("\\DCP", StringComparison.OrdinalIgnoreCase))
                return "DcpPackage";
            if (relativePath.Contains("IMF", StringComparison.OrdinalIgnoreCase))
                return "ImfPackage";
            // Registry categories 3 and 6 are image sequences (TGA subtitles, DCDM).
            return category is 3 or 6 ? "ImageSequence" : "Generic";
        }

        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        if (VideoExtensions.Contains(extension))
            return "Video";
        if (AudioExtensions.Contains(extension))
            return "Audio";
        if (SubtitleExtensions.Contains(extension))
            return "Subtitle";
        if (ImageExtensions.Contains(extension))
            return "Image";
        if (DocumentExtensions.Contains(extension))
            return "Document";
        return "Generic";
    }

    /// <summary>
    /// The content type from the registry element type (FTR, TLR…), else from the first folder of the path
    /// (FTR\…, PROMO\TLR130\…, BONUS\EPK\…).
    /// </summary>
    public static string? ContentType(string? registryType, string relativePath)
    {
        var fromType = ContentTypeFromCode(registryType);
        if (fromType is not null)
            return fromType;

        var folders = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).ToList();
        foreach (var folder in Enumerable.Reverse(folders))
        {
            var fromFolder = ContentTypeFromCode(folder);
            if (fromFolder is not null)
                return fromFolder;
        }

        // SUBTITLE\<date>\… and AUDIO\… with no trailer or bonus hint belong to the film itself.
        var root = folders.FirstOrDefault()?.ToUpperInvariant();
        return root is "SUBTITLE" or "SUBTITLES" or "AUDIO" ? "Feature" : null;
    }

    private static string? ContentTypeFromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var c = code.ToUpperInvariant();
        return c switch
        {
            _ when c.StartsWith("FTR") => "Feature",
            _ when c.StartsWith("TLR") || c.StartsWith("TRAILER") => "Trailer",
            _ when c.StartsWith("TEASER") || c.StartsWith("TSR") => "Teaser ",
            _ when c.StartsWith("EPK") => "EPK",
            _ when c.StartsWith("ITW") || c.StartsWith("INTERVIEW") => "Interview",
            _ when c.StartsWith("MAKING") || c.StartsWith("MKO") => "Making Of",
            _ when c.StartsWith("SPOT") => "TV Spot",
            _ when c.StartsWith("EXTRAIT") || c.StartsWith("CLIP") => "Clip",
            _ when c.StartsWith("CUTDOWN") || c.StartsWith("PROMO") => "Promo",
            _ when c.StartsWith("BONUS") => "Bonus",
            _ when c.StartsWith("BROLL") || c.StartsWith("B-ROLL") => "B-Roll",
            _ => null,
        };
    }

    /// <summary>
    /// Groups assets into Mediaspot Contents (the importer puts rows with identical Content metadata in the same Content):
    /// the feature is "Original"; a trailer is named by its code (TLR130, TLR-01…); a bonus by its sub-folder (EPK, ITW…).
    /// </summary>
    public static string? ContentVersion(string? contentType, string? registryType, string relativePath)
    {
        var folders = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).ToList();
        switch (contentType?.Trim())
        {
            case "Feature":
                return "Original";

            case "Trailer" or "Teaser":
                if (registryType is { Length: > 3 } type && TrailerCodeRegex().IsMatch(type))
                    return type.ToUpperInvariant();
                return folders.Select(f => f.ToUpperInvariant()).FirstOrDefault(f => TrailerCodeRegex().IsMatch(f) && f.Length > 3)
                    ?? folders.LastOrDefault()?.ToUpperInvariant();

            case null or "":
                return null;

            default:
                // BONUS\EPK\… → EPK; PROMO\CUTDOWN_30s\… → CUTDOWN_30S
                return folders.Count > 1 ? folders[1].ToUpperInvariant() : folders.FirstOrDefault()?.ToUpperInvariant();
        }
    }

    [GeneratedRegex(@"^(TLR|TSR|TEASER)", RegexOptions.IgnoreCase)]
    private static partial Regex TrailerCodeRegex();

    public static string? PictureVersion(string? registryVersion, string relativePath)
    {
        if ((registryVersion ?? "").Contains("textless", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("TEXTLESS", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("_TXTLSS_", StringComparison.OrdinalIgnoreCase))
            return "Textless";
        return null;
    }

    /// <summary>"51" → 5.1, "20" → 2.0, "71" → 7.1.</summary>
    public static string? AudioMix(string? registryAudio) => registryAudio?.Trim() switch
    {
        "51" or "5.1" => "5.1",
        "20" or "2.0" => "2.0",
        "71" or "7.1" => "7.1",
        _ => null,
    };

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex NumberRegex();
}
