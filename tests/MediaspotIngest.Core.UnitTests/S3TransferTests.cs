using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Files;

namespace MediaspotIngest.Core.UnitTests;

public sealed class S3TransferTests
{
    private const string IMPORT_FOLDER = "s3://s3-mediaspot-production-clientstorage-eu/MSPROOT/AsperaIngoing/asp-cloudawsdemo-759-vdmeuropa/temp/";
    private const string BUCKET = "s3-mediaspot-production-clientstorage-eu";
    private const string PREFIX = "MSPROOT/AsperaIngoing/asp-cloudawsdemo-759-vdmeuropa/temp/";

    [Fact]
    public void The_import_folder_shown_by_mediaspot_gives_the_bucket_and_the_temp_prefix()
    {
        var settings = new S3Settings();

        Assert.True(settings.TrySetFromUrl(IMPORT_FOLDER));
        Assert.Equal(BUCKET, settings.Bucket);
        Assert.Equal(PREFIX, settings.Prefix);
    }

    [Theory]
    [InlineData("film.mov", "MSPROOT/AsperaIngoing/asp-cloudawsdemo-759-vdmeuropa/temp/film.mov")]
    [InlineData(@"DCP_FTR\ASSETMAP", "MSPROOT/AsperaIngoing/asp-cloudawsdemo-759-vdmeuropa/temp/DCP_FTR/ASSETMAP")]
    public void Files_and_files_of_a_folder_go_directly_under_the_temp_prefix(string name, string expectedKey)
    {
        var settings = new S3Settings { Bucket = BUCKET, Prefix = PREFIX };

        Assert.Equal(expectedKey, settings.KeyFor(name));
    }

    [Fact]
    public void The_plan_sends_restored_files_to_the_bucket_temp_folder()
    {
        var profile = new PlatformProfile
        {
            SourceRootPath = @"\\qumulo\restore",
            TransferTarget = TransferTarget.S3,
            S3 = new S3Settings { Bucket = BUCKET, Prefix = PREFIX },
        };
        var item = new IngestItem { Id = "e1", TitleKey = "LUCY", RelativePath = @"FTR\PRHQ\lucy.mov", Location = FileLocation.Source };

        var plan = TransferPlan.ToIngest(profile, [item], "secret");

        var step = Assert.Single(plan.Steps);
        Assert.Equal("S3", plan.Method);
        Assert.Equal($"s3://{BUCKET}/{PREFIX}lucy.mov", step.To);
        Assert.Null(step.Problem);
    }

    [Fact]
    public void An_uploaded_item_stays_in_the_temp_folder_once_its_source_is_gone()
    {
        var profile = new PlatformProfile { SourceRootPath = @"C:\nowhere", TransferTarget = TransferTarget.S3 };
        var item = new IngestItem { Id = "e1", TitleKey = "LUCY", RelativePath = "lucy.mov", TransferredAt = DateTime.Now };

        Assert.Equal(FileLocation.Ingest, new MediaFiles(profile).Locate(item));
    }

    [Theory]
    [InlineData(1L << 30, 64L * 1024 * 1024)]
    [InlineData(1_000L * (1L << 30), 119_304_648L)]
    public void Very_large_masters_get_bigger_parts_to_stay_under_the_s3_part_limit(long length, long expectedPartSize)
    {
        Assert.Equal(expectedPartSize, S3Uploader.PartSize(length));
    }
}
