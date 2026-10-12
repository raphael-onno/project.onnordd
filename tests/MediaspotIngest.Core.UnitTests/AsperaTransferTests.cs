using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Files;

namespace MediaspotIngest.Core.UnitTests;

public sealed class AsperaTransferTests : IDisposable
{
    private const string ASPERA_HOST = "aspera.example.com";
    private const string ASPERA_LOGIN = "asp-cloudawsdemo-759-vdmeuropa";
    private const string TITLE_KEY = "20-ANS-D-ECART";
    private const string RELATIVE_PATH = @"FTR\PRHQ\film.mov";

    private readonly string _sourceRoot = Path.Combine(Path.GetTempPath(), "MediaspotIngestTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void An_upload_resumes_never_overwrites_and_targets_the_temp_folder_of_the_account()
    {
        var uploader = new AsperaUploader(Settings());

        var args = uploader.BuildArguments(@"\\qumulo\restore\film.mov", removeSource: false);

        Assert.Equal(["-d", "-k", "2", "--overwrite=never", "-l", "500m", @"\\qumulo\restore\film.mov", $"{ASPERA_LOGIN}@{ASPERA_HOST}:/temp"], args);
    }

    [Fact]
    public void A_move_asks_ascp_to_remove_the_source_once_transferred()
    {
        var uploader = new AsperaUploader(Settings());

        var args = uploader.BuildArguments("film.mov", removeSource: true);

        Assert.Contains("--remove-after-transfer", args);
    }

    [Fact]
    public void A_ssh_port_and_a_relative_remote_directory_are_honoured()
    {
        var settings = Settings();
        settings.SshPort = 33001;
        settings.RemoteDirectory = @"provider\temp\";

        var args = new AsperaUploader(settings).BuildArguments("film.mov", removeSource: false);

        Assert.Contains("33001", args);
        Assert.Equal($"{ASPERA_LOGIN}@{ASPERA_HOST}:/provider/temp", args[^1]);
    }

    [Fact]
    public void The_plan_uploads_restored_files_to_the_aspera_account()
    {
        var item = RestoredItem();
        var profile = Profile();

        var step = Assert.Single(TransferPlan.ToIngest(profile, [item]).Steps);

        Assert.Equal(Path.Combine(_sourceRoot, TITLE_KEY, RELATIVE_PATH), step.From);
        Assert.Equal($"{ASPERA_LOGIN}@{ASPERA_HOST}:/temp/film.mov", step.To);
        Assert.Null(step.Problem);
    }

    [Fact]
    public void An_uploaded_item_stays_in_the_ingest_folder_even_once_its_source_is_gone()
    {
        var profile = Profile();
        var item = RestoredItem();
        item.TransferredAt = DateTime.Now;

        Assert.Equal(FileLocation.Ingest, new MediaFiles(profile).Locate(item));
    }

    [Fact]
    public void A_restored_item_not_yet_uploaded_is_in_the_source_area()
    {
        var profile = Profile();
        var item = RestoredItem();
        Directory.CreateDirectory(Path.Combine(_sourceRoot, TITLE_KEY, "FTR", "PRHQ"));
        File.WriteAllText(Path.Combine(_sourceRoot, TITLE_KEY, RELATIVE_PATH), "media");

        Assert.Equal(FileLocation.Source, new MediaFiles(profile).Locate(item));
        Assert.Equal(5, item.ActualSizeBytes);
    }

    private static AsperaSettings Settings() => new() { Host = ASPERA_HOST, Login = ASPERA_LOGIN };

    private PlatformProfile Profile() => new()
    {
        SourceRootPath = _sourceRoot,
        TransferTarget = TransferTarget.Aspera,
        Aspera = Settings(),
    };

    private static IngestItem RestoredItem() => new()
    {
        Id = "e1",
        TitleKey = TITLE_KEY,
        RelativePath = RELATIVE_PATH,
        Location = FileLocation.Source,
    };

    public void Dispose()
    {
        if (Directory.Exists(_sourceRoot))
            Directory.Delete(_sourceRoot, recursive: true);
    }
}
