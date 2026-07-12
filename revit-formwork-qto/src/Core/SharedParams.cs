using Autodesk.Revit.DB;

namespace FormworkQTO.Core;

/// <summary>
/// 建立並綁定「側模面積」「底模面積」兩個共用參數（面積型別、實例參數），
/// 綁定到五個結構類別。計算結果寫回參數後，原生明細表、標籤都能直接取用。
/// </summary>
public static class SharedParams
{
    public const string SideAreaParam = "側模面積";
    public const string BottomAreaParam = "底模面積";
    private const string GroupName = "FormworkQTO";

    /// <summary>確保兩個參數已綁定到專案。必須在已開啟的 Transaction 內呼叫。</summary>
    public static void EnsureBound(Document doc)
    {
        Autodesk.Revit.ApplicationServices.Application app = doc.Application;
        string originalFile = app.SharedParametersFilename;

        // 用暫存共用參數檔，不動使用者自己的共用參數檔
        string tmpFile = Path.Combine(Path.GetTempPath(), "FormworkQTO_SharedParams.txt");
        if (!File.Exists(tmpFile)) File.Create(tmpFile).Dispose();
        app.SharedParametersFilename = tmpFile;

        try
        {
            DefinitionFile defFile = app.OpenSharedParameterFile()
                ?? throw new InvalidOperationException("無法開啟共用參數檔：" + tmpFile);
            DefinitionGroup group = defFile.Groups.get_Item(GroupName)
                ?? defFile.Groups.Create(GroupName);

            CategorySet categories = app.Create.NewCategorySet();
            foreach (BuiltInCategory bic in FormworkCalculator.ConcreteCategories)
                categories.Insert(Category.GetCategory(doc, bic));

            foreach (string name in new[] { SideAreaParam, BottomAreaParam })
            {
                Definition definition = group.Definitions.get_Item(name)
                    ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(name, SpecTypeId.Area));

                if (!doc.ParameterBindings.Contains(definition))
                {
                    doc.ParameterBindings.Insert(
                        definition, app.Create.NewInstanceBinding(categories), GroupTypeId.Data);
                }
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(originalFile))
                app.SharedParametersFilename = originalFile;
        }
    }
}
