using ClosedXML.Excel;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.UnitTests;

/// <summary>Builds small Legacy Importer templates on disk, shaped like the real ones (5 header rows).</summary>
internal sealed class TemplateBuilder : IDisposable
{
    private readonly List<(string TagName, string Layer, string Requirement, string Values, string Label)> _columns = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MediaspotIngestTests", Guid.NewGuid().ToString("N"));

    public string Directory => _directory;

    public TemplateBuilder Column(string tagName, string layer = "Asset", string requirement = "O", string values = "", string label = "")
    {
        _columns.Add((tagName, layer, requirement, values, label == "" ? tagName : label));
        return this;
    }

    public TemplateBuilder Spacer()
    {
        _columns.Add(("", "", "", "", ""));
        return this;
    }

    public LegacyTemplate Build() => LegacyTemplate.Load(Save());

    public string Save()
    {
        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "template.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Feuil1");
        for (var i = 0; i < _columns.Count; i++)
        {
            var c = _columns[i];
            sheet.Cell(1, i + 1).SetValue(c.TagName);
            sheet.Cell(2, i + 1).SetValue(c.Layer);
            sheet.Cell(3, i + 1).SetValue(c.Requirement);
            sheet.Cell(4, i + 1).SetValue(c.Values);
            sheet.Cell(5, i + 1).SetValue(c.Label);
        }

        workbook.SaveAs(path);
        return path;
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_directory))
            System.IO.Directory.Delete(_directory, recursive: true);
    }
}
