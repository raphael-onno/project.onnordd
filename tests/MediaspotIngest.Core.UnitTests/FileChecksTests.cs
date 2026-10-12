using System.Text;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Files;

namespace MediaspotIngest.Core.UnitTests;

public sealed class FileChecksTests : IDisposable
{
    private const long ONE_GIB = 1024L * 1024 * 1024;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MediaspotIngestTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(10 * ONE_GIB)]
    [InlineData(10_737_000_000)]
    [InlineData(10_000_000_000)]
    public void A_restored_size_matching_the_registry_in_either_gigabyte_reading_passes(long actual)
    {
        var item = new IngestItem { Source = "ltfs", ExpectedSizeBytes = 10 * ONE_GIB, ActualSizeBytes = actual };

        Assert.Null(FileChecks.SizeWarning(item));
    }

    [Fact]
    public void A_restored_file_much_smaller_than_announced_is_reported()
    {
        var item = new IngestItem { Source = "ltfs", ExpectedSizeBytes = 10 * ONE_GIB, ActualSizeBytes = 4 * ONE_GIB };

        Assert.NotNull(FileChecks.SizeWarning(item));
    }

    [Fact]
    public void Sizes_from_retrospect_are_not_checked_because_its_export_truncates_them()
    {
        var item = new IngestItem { Source = "retro", ExpectedSizeBytes = 10 * ONE_GIB, ActualSizeBytes = 4 * ONE_GIB };

        Assert.Null(FileChecks.SizeWarning(item));
    }

    [Fact]
    public void An_stl_whose_header_matches_its_subtitles_passes()
    {
        var path = Stl(announcedBlocks: 3, announcedSubtitles: 2, extensionBlocks: [0xFE, 0xFF, 0xFF]);

        Assert.Null(FileChecks.StlWarning(path));
    }

    [Fact]
    public void An_stl_announcing_more_subtitles_than_it_holds_is_reported()
    {
        var path = Stl(announcedBlocks: 2, announcedSubtitles: 5, extensionBlocks: [0xFF, 0xFF]);

        Assert.Equal("STL : 2 sous-titres au lieu des 5 annoncés", FileChecks.StlWarning(path));
    }

    [Fact]
    public void A_truncated_stl_is_reported()
    {
        var path = Stl(announcedBlocks: 1, announcedSubtitles: 1, extensionBlocks: [0xFF], extraBytes: 40);

        Assert.Equal("STL tronqué (taille non multiple de 128 octets)", FileChecks.StlWarning(path));
    }

    /// <summary>A minimal EBU STL: 1024-byte GSI with TNB/TNS, then one 128-byte TTI block per extension byte given.</summary>
    private string Stl(int announcedBlocks, int announcedSubtitles, byte[] extensionBlocks, int extraBytes = 0)
    {
        Directory.CreateDirectory(_directory);
        var bytes = new byte[1024 + 128 * extensionBlocks.Length + extraBytes];
        Encoding.ASCII.GetBytes(announcedBlocks.ToString("00000")).CopyTo(bytes, 238);
        Encoding.ASCII.GetBytes(announcedSubtitles.ToString("00000")).CopyTo(bytes, 243);
        for (var i = 0; i < extensionBlocks.Length; i++)
            bytes[1024 + i * 128 + 3] = extensionBlocks[i];

        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".stl");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
