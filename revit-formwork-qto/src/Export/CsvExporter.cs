using System.Text;
using Autodesk.Revit.DB;
using FormworkQTO.Core;

namespace FormworkQTO.Export;

/// <summary>匯出計算結果到 CSV（UTF-8 含 BOM，Excel 可直接開啟中文）。</summary>
public static class CsvExporter
{
    /// <summary>匯出到「我的文件」資料夾，回傳檔案路徑。</summary>
    public static string Export(List<ElementResult> results, string docTitle)
    {
        string safeTitle = string.Join("_", docTitle.Split(Path.GetInvalidFileNameChars()));
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"模板計算_{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var ordered = results
            .OrderBy(r => r.LevelElevation)
            .ThenBy(r => r.CategoryName)
            .ThenBy(r => r.Element.Id.Value)
            .ToList();

        var sb = new StringBuilder();

        sb.AppendLine("=== 彙總（樓層 × 類別）===");
        sb.AppendLine("樓層,類別,元件數,側模(m2),底模(m2),合計(m2)");
        foreach (var g in ordered.GroupBy(r => (r.LevelElevation, r.LevelName, r.CategoryName)))
        {
            sb.AppendLine(string.Join(",",
                Csv(g.Key.LevelName), Csv(g.Key.CategoryName), g.Count(),
                M2(g.Sum(r => r.SideFt2)), M2(g.Sum(r => r.BottomFt2)), M2(g.Sum(r => r.TotalFt2))));
        }

        sb.AppendLine();
        sb.AppendLine("=== 明細 ===");
        sb.AppendLine("元件ID,樓層,類別,類型,側模(m2),底模(m2),接觸扣除(m2),合計(m2)");
        foreach (ElementResult r in ordered)
        {
            sb.AppendLine(string.Join(",",
                r.Element.Id.Value, Csv(r.LevelName), Csv(r.CategoryName), Csv(r.TypeName),
                M2(r.SideFt2), M2(r.BottomFt2), M2(r.DeductedFt2), M2(r.TotalFt2)));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return path;
    }

    public static double ToM2(double ft2) =>
        UnitUtils.ConvertFromInternalUnits(ft2, UnitTypeId.SquareMeters);

    private static string M2(double ft2) => ToM2(ft2).ToString("F2");

    private static string Csv(string s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }
}
