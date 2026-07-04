# RevitAutoAnnotation — Revit 2026 施工圖自動標註外掛

在出施工圖前自動幫你標註構件，免去手動標尺寸的重複工作。
提供 WPF 面板切換不同標註模式，一鍵對**目前的平面視圖**批次標註。

## 功能（四種標註模式）

| 模式 | 自動產生的內容 |
|------|----------------|
| 牆體尺寸標註 | 每道直線牆的**牆長**尺寸（沿牆偏移放置）＋**牆厚**尺寸（牆中點） |
| 門窗標註 | 門窗**標籤**（依載入的門/窗標籤族群顯示，可用含寬×高的標籤）＋**開口寬度**尺寸（用族群 Left/Right 參考面） |
| 柱位放樣圖 | 柱**標籤**＋柱心至最近格線的 **X / Y 定位尺寸**（柱心在格線上時自動略過） |
| 梁定位圖 | 梁**標籤**（放在梁中點）＋梁心至最近**平行格線**的定位尺寸 |

面板選項：是否建立尺寸／標籤、是否標牆厚、標註偏移距離（mm）。

## 專案結構

```
RevitAutoAnnotation/
├── App.cs                          # IExternalApplication：建立 Ribbon 按鈕
├── Commands/
│   └── ShowAnnotationWindowCommand.cs  # 開啟非模態 WPF 面板
├── Core/
│   ├── AnnotationMode.cs           # 標註模式列舉
│   ├── AnnotationSettings.cs       # 面板設定與執行結果
│   └── AnnotationRequestHandler.cs # IExternalEventHandler：分派到各服務
├── Services/
│   ├── AnnotationHelper.cs         # 共用：取面參考、格線參考、找最近格線、放標籤
│   ├── WallDimensionService.cs     # 牆長／牆厚
│   ├── OpeningAnnotationService.cs # 門窗標籤＋開口寬度
│   ├── ColumnLayoutService.cs      # 柱位放樣
│   └── BeamLocationService.cs      # 梁定位
├── UI/
│   ├── AnnotationWindow.xaml       # WPF 面板
│   └── AnnotationWindow.xaml.cs
├── RevitAutoAnnotation.csproj
└── RevitAutoAnnotation.addin       # Revit 外掛清單
```

## 建置（需 Windows）

需求：Windows、.NET 8 SDK、Visual Studio 2022（或直接用 dotnet CLI）、Revit 2026。

```powershell
cd RevitAutoAnnotation
dotnet build -c Release
```

Revit API 參考預設用 NuGet 套件 `Nice3point.Revit.Api.RevitAPI / RevitAPIUI`（2026 版）。
若公司環境無法連 NuGet，csproj 內已附註解，可改為直接參考
`C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll / RevitAPIUI.dll`。

## 安裝

把以下檔案複製到 Revit 外掛資料夾 `%AppData%\Autodesk\Revit\Addins\2026\`：

```
%AppData%\Autodesk\Revit\Addins\2026\
├── RevitAutoAnnotation.addin
└── RevitAutoAnnotation\
    └── RevitAutoAnnotation.dll   （bin\Release 下的輸出）
```

重啟 Revit → Ribbon 出現「自動標註」頁籤。

## 使用方式

1. 開啟要出圖的**平面視圖**（樓層平面或結構平面）。
2. 點 Ribbon「自動標註」→ 開啟面板（非模態，可一直開著切視圖）。
3. 選模式（牆體／門窗／柱位放樣／梁定位）、勾選項、設定偏移距離。
4. 按「執行標註」，狀態列會回報建立的尺寸與標籤數量。
5. 每次執行是一個 Transaction，不滿意可直接 Ctrl+Z 一次全部復原。

## 使用前置條件與已知限制

- **標籤需要對應的標籤族群已載入專案**（門標籤、窗標籤、柱標籤、結構構架標籤）。未載入時該構件會計入「略過」。
- 門窗寬度尺寸需要族群定義 **Left / Right 參考面**（Revit 內建門窗族群都有；自製族群請在族群編輯器中設定參考面的「參考」屬性）。
- 柱／梁定位尺寸需要族群的**中心參考面**（CenterLeftRight / CenterFrontBack，內建結構柱、梁族群都有）。
- 目前僅處理**直線**牆／梁，弧形構件會略過。
- 尺寸樣式使用專案目前的預設尺寸類型；標註位置為幾何規則放置（沿構件偏移固定距離），出圖前可再微調。

## 之後擴充新模式的方法

1. 在 `AnnotationMode` 加一個列舉值。
2. 在 `Services/` 新增一個 `XxxService.Annotate(doc, view, settings)`。
3. 在 `AnnotationRequestHandler.Execute` 的 switch 加一行分派。
4. 在 `AnnotationWindow.xaml` 加一個 RadioButton。
