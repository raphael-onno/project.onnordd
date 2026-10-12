using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Template;
using MediaspotIngest.Core.Validation;

namespace MediaspotIngest.App.ViewModels;

/// <summary>An item in the batch grid, with its validation result.</summary>
public sealed partial class ItemRow : ObservableObject
{
    public ItemRow(IngestItem item) => Item = item;

    public IngestItem Item { get; }

    [ObservableProperty]
    private ItemStatus _status;

    [ObservableProperty]
    private IReadOnlyList<CellIssue> _issues = [];

    public string TitleKey => Item.TitleKey;

    public string FileName => Item.FileName;

    public string RelativePath => Item.RelativePath;

    public string AssetType => Cell("assetType");

    public string ContentType => Cell("contentType");

    public FileLocation Location => Item.Location;

    public string Size => FormatSize(Item.ActualSizeBytes ?? Item.ExpectedSizeBytes);

    /// <summary>Size or STL check warning from the last time the file was located.</summary>
    public string? FileWarning => Item.FileWarning;

    public long? MediaspotAssetId => Item.MediaspotAssetId;

    public string? MediaspotStatus => Item.MediaspotStatus;

    public string IssuesText => Issues.Count == 0 ? "" : string.Join("\n", Issues);

    public int IssueCount => Issues.Count;

    public void Evaluate(RowValidator? validator)
    {
        Issues = validator?.Validate(Item) ?? [];
        Status = ItemStatusRules.Evaluate(Item, allCellsValid: validator is not null && Issues.Count == 0);
        OnPropertyChanged(string.Empty);
    }

    private string Cell(string tag) => Item.GetCell(tag) is { } v && !LegacyTemplate.IsEmpty(v) ? v : "";

    public static string FormatSize(long? bytes) => bytes switch
    {
        null => "",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} Go",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} Mo",
        >= 1024 => $"{bytes / 1024.0:0} Ko",
        _ => $"{bytes} o",
    };
}

/// <summary>One template column of the selected item, editable in the detail panel.</summary>
public sealed partial class CellRow : ObservableObject
{
    private readonly Action<CellRow> _changed;

    public CellRow(TemplateColumn column, string? value, string? issue, Action<CellRow> changed)
    {
        Column = column;
        _value = value ?? "";
        _issue = issue;
        _changed = changed;
        Options = new ObservableCollection<string>(column.AllowedValues.Prepend(LegacyTemplate.Skip));
    }

    public TemplateColumn Column { get; }

    public string Label => string.IsNullOrEmpty(Column.Label) ? Column.TagName : Column.Label;

    public string TagName => Column.TagName;

    public string Layer => Column.Layer;

    public bool IsMandatory => Column.Requirement is ColumnRequirement.Mandatory or ColumnRequirement.MandatoryIfSeason;

    public bool IsReadOnly => Column.IsAutomatic;

    public bool HasOptions => Column.AllowedValues.Count > 0;

    public ObservableCollection<string> Options { get; }

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private string? _issue;

    partial void OnValueChanged(string value) => _changed(this);
}
