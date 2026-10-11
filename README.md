# Image Watermark Portable

Windows 10/11 x64 極簡批次圖片浮水印工具。單一 Self-contained EXE，免安裝 .NET Runtime。

![畫面預覽](assets/screenshot.png)

## 功能

- 選擇來源／目的資料夾，批次加入**由左上往右下 +30°、滿版重複排列**的半透明文字浮水印
- 設定：浮水印文字（Unicode，含繁體中文，上限 200 字）、透明度（5–80%，預設 25%）、文字大小（12–120 px，預設 36）、**浮水印顏色（Color Picker，預設白色 #FFFFFF）**
- 啟動即顯示**內建範例圖預覽**；設定變更以 200 ms debounce 即時更新
- **預覽區可拖曳其他圖片（jpg/jpeg/png/webp）更換預覽**；僅更換預覽，不影響來源資料夾選擇
- 遞迴處理所有子資料夾，目的地保留相對路徑與原檔名／副檔名
- 保留原格式與像素尺寸：JPG→JPG（品質 95）、PNG→PNG（含透明）、WebP→WebP
- EXIF Orientation 先正規化再輸出，並清除方向標記避免重複旋轉
- 批次處理時**預覽區即時顯示正在處理的圖片**（800 px 縮圖、120 ms 節流），狀態列同步顯示 `處理中 i/n：檔名`
- 進度條／百分比／總數／成功／失敗即時統計，UI 不凍結
- 單檔失敗略過繼續，錯誤與摘要寫入目的地根目錄 `watermark-errors.log`（UTF-8）
- 同名輸出直接覆寫；先寫同目錄暫存檔再原子替換，失敗不留半張圖
- 路徑安全：拒絕來源＝目的、目的地在來源內、來源在目的地內；junction/reparse point 不遞迴

## 使用方式

1. 執行 `dist\ImageWatermarkPortable.exe`
2. 「瀏覽...」選擇**來源資料夾**與**目的資料夾**（兩者不可相同或互相嵌套）
3. 輸入浮水印文字、調整透明度／文字大小／顏色，下方預覽即時更新（亦可直接拖曳圖片到預覽框更換預覽）
4. 按「開始處理」，觀察進度與成功／失敗統計
5. 完成後於目的地資料夾檢視輸出與 `watermark-errors.log`

## 建置與發佈

需求：.NET 10 SDK

```powershell
# 還原 + 測試（39 項）
dotnet test .\ImageWatermarkPortable.slnx -c Release

# 發佈單檔 Self-contained EXE
dotnet publish .\src\ImageWatermarkPortable\ImageWatermarkPortable.csproj `
  -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true
```

（亦可用 profile：`dotnet publish .\src\ImageWatermarkPortable\ImageWatermarkPortable.csproj -c Release -p:PublishProfile=win-x64`）

## 專案結構

```text
ImageWatermarkPortable/
├─ src/ImageWatermarkPortable/
│  ├─ Program.cs
│  ├─ Forms/MainForm.cs            表單 UI、預覽、debounce、拖曳更換預覽
│  ├─ Models/WatermarkOptions.cs   文字／透明度／字級設定
│  ├─ Models/BatchProgress.cs      進度模型
│  └─ Services/
│     ├─ ImageDiscoveryService.cs  遞迴列舉、排除 junction
│     ├─ WatermarkRenderer.cs      浮水印排版與合成（預覽與輸出共用）
│     ├─ ImageEncoderService.cs    JPG/PNG/WebP 編碼
│     ├─ BatchProcessingService.cs 批次迴圈、錯誤隔離、統計
│     ├─ SafeFileWriter.cs         暫存寫入與安全替換
│     ├─ ErrorLogService.cs        watermark-errors.log
│     └─ PathSecurity.cs           路徑正規化與嵌套檢查
├─ tests/ImageWatermarkPortable.Tests/  39 項自動化測試
├─ tools/IconGenerator/                icon.ico 產生器
├─ tools/SmokeRunner/                  無介面批次煙霧測試
├─ assets/icon.ico, sample1.jpg, screenshot.png  圖示、內建預覽圖、畫面截圖
└─ dist/ImageWatermarkPortable.exe     交付 EXE
```

## 測試

```powershell
dotnet test .\ImageWatermarkPortable.slnx -c Release
```

自動化結果：**39/39 通過**。

## 第三方套件與授權（重要）

| 套件 | 版本 | 授權 |
|---|---|---|
| SixLabors.ImageSharp | 3.1.12 | Six Labors Split License |
| SixLabors.ImageSharp.Drawing | 2.1.5 | Six Labors Split License |
| SixLabors.Fonts（相依） | 2.x | Six Labors Split License |

- **授權審查必讀**：ImageSharp 3.x 起採 Six Labors Split License，個人／小型組織免費， larger 商業用途需購買授權。**正式商用前務必依最新條款審核**（規格書 §6）。
- 已知版本約束：ImageSharp **3.2.0** 與 ImageSharp.Drawing **2.2.0** 起，建置時會強制檢查 `SixLaborsLicenseKey`（付費授權）；4.x 同樣要求授權金鑰。因此本專案鎖定 **3.1.12 + 2.1.5**（最後可自由建置組合）。若取得授權，可升級至 4.x 並消除下列弱點警告。
- 已知弱點警告：`SixLabors.ImageSharp 3.1.12` 被 NuGet 標記 NU1902/NU1903（GHSA-gwg2、GHSA-j3p4、GHSA-j9gm、GHSA-jjfr、GHSA-wmxv）。官方僅於 4.x 修補，升級需付費授權；評估請見各 advisory，並於商用決策時一併考量。
- 其他相依：SixLabors.PolygonClipper（MIT）、System.*（MIT）。

## 規格偏離記錄

- 顏色選擇器：原規格 §3.2 定義文字顏色固定 `#FFFFFF`，依使用者 2026-10-11 指示改為 Color Picker 可選，預設值仍為白色。

## 已知限制（規格 §12 實測項）

- 透明 PNG/WebP：浮水印會合成到透明區域（呈現半透明白）；如需完全不影響透明區需另訂規則
- 動畫 WebP/PNG：偵測到多幀即以失敗記錄並略過，不輸出第一幀
- metadata：以像素正確與透明為優先，ICC/GPS 等不保證完整保留
- 深色/純白背景可改用對比色（Color Picker 手動選擇）；自動深淺自適應為後續版本項目
- 未提供暫停／取消按鈕；關閉視窗時詢問並安全終止

## 交付物

- `dist/ImageWatermarkPortable.exe`（單檔、self-contained、win-x64）
- 本 README、第三方授權聲明（上表）
