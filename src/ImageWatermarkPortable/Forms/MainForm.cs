using System.Diagnostics;
using System.Reflection;
using ImageWatermarkPortable.Models;
using ImageWatermarkPortable.Services;
using SixLabors.ImageSharp.Processing;
using SxImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using SxImageFactory = SixLabors.ImageSharp.Image;
using SxRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageWatermarkPortable.Forms;

public sealed class MainForm : Form
{
    private readonly TextBox _txtSource = new();
    private readonly TextBox _txtDestination = new();
    private readonly TextBox _txtWatermarkText = new();
    private readonly Button _btnBrowseSource = new();
    private readonly Button _btnBrowseDestination = new();
    private readonly TrackBar _trkOpacity = new();
    private readonly TrackBar _trkFontSize = new();
    private readonly Button _btnColor = new();
    private readonly Label _lblColorHint = new();
    private readonly Label _lblOpacityValue = new();
    private readonly Label _lblFontSizeValue = new();
    private readonly GroupBox _grpPreview = new();
    private readonly PictureBox _picPreview = new();
    private readonly Label _lblPreviewPlaceholder = new();
    private readonly Button _btnStart = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _lblProgress = new();
    private readonly Label _lblStats = new();
    private readonly Label _lblStatus = new();

    private readonly ImageDiscoveryService _discovery = new();
    private readonly WatermarkRenderer _renderer = new();
    private readonly ImageEncoderService _encoder = new();
    private readonly SafeFileWriter _safeWriter = new();
    private readonly BatchProcessingService _batch;

    private readonly System.Windows.Forms.Timer _previewDebounce = new() { Interval = 200 };
    private readonly System.Windows.Forms.Timer _progressThrottle = new() { Interval = 100 };
    private readonly object _previewSync = new();
    private readonly List<Control[]> _layoutRows = new();

    private SxImage? _previewSource;
    private Color _watermarkColor = Color.White;
    private int _previewVersion;
    private int _scanVersion;
    private CancellationTokenSource? _batchCts;
    private BatchProgress? _pendingProgress;
    private bool _processing;
    private bool _pendingClose;

    public MainForm()
    {
        _batch = new BatchProcessingService(_renderer, _encoder, _safeWriter);
        Icon = LoadAppIcon();
        InitializeUi();

        Shown += async (_, _) => await LoadDefaultPreviewAsync();

        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            _ = RenderPreviewAsync();
        };
        _progressThrottle.Tick += (_, _) =>
        {
            _progressThrottle.Stop();
            FlushProgress();
        };
    }

    private void InitializeUi()
    {
        Text = $"Image Watermark Portable {GetInformationalVersion()}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 720);
        ClientSize = new Size(780, 720);

        var pad = 12;
        var labelWidth = 88;
        var gap = 8;
        var fieldX = pad + labelWidth + gap;
        var btnWidth = 90;
        var fieldWidth = ClientSize.Width - pad * 2 - labelWidth - gap - gap - btnWidth;

        var lblSource = MakeLabel("來源資料夾", pad);
        _txtSource.Size = new Size(fieldWidth, 27);
        _txtSource.Left = fieldX;
        _txtSource.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ConfigureBrowseButton(_btnBrowseSource, _txtSource, ClientSize.Width - pad - btnWidth);
        RegisterRow(lblSource, _txtSource, _btnBrowseSource);

        var lblDestination = MakeLabel("目的資料夾", pad);
        _txtDestination.Size = new Size(fieldWidth, 27);
        _txtDestination.Left = fieldX;
        _txtDestination.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ConfigureBrowseButton(_btnBrowseDestination, _txtDestination, ClientSize.Width - pad - btnWidth);
        RegisterRow(lblDestination, _txtDestination, _btnBrowseDestination);

        var lblText = MakeLabel("浮水印文字", pad);
        _txtWatermarkText.MaxLength = 200;
        _txtWatermarkText.Size = new Size(ClientSize.Width - pad * 2 - labelWidth - gap, 27);
        _txtWatermarkText.Left = fieldX;
        _txtWatermarkText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        RegisterRow(lblText, _txtWatermarkText);

        var lblOpacity = MakeLabel("透明度", pad);
        ConfigureSlider(_trkOpacity, fieldX - 6, 400, 5, 80, 25);
        _lblOpacityValue.Text = "25%";
        _lblOpacityValue.Size = new Size(70, 23);
        _lblOpacityValue.Left = fieldX + 400;
        RegisterRow(lblOpacity, _trkOpacity, _lblOpacityValue);

        var lblFontSize = MakeLabel("文字大小", pad);
        ConfigureSlider(_trkFontSize, fieldX - 6, 400, 12, 120, 36);
        _lblFontSizeValue.Text = "36（基準 px）";
        _lblFontSizeValue.Size = new Size(110, 23);
        _lblFontSizeValue.Left = fieldX + 400;
        RegisterRow(lblFontSize, _trkFontSize, _lblFontSizeValue);

        var lblColor = MakeLabel("浮水印顏色", pad);
        _btnColor.FlatStyle = FlatStyle.Flat;
        _btnColor.FlatAppearance.BorderColor = Color.Gray;
        _btnColor.Size = new Size(110, 27);
        _btnColor.Left = fieldX;
        _btnColor.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _btnColor.Click += BtnColor_Click;
        ApplyColorToButton(_watermarkColor);
        _lblColorHint.Text = "點擊選擇顏色";
        _lblColorHint.AutoSize = true;
        _lblColorHint.ForeColor = Color.Gray;
        _lblColorHint.Left = fieldX + 120;
        RegisterRow(lblColor, _btnColor, _lblColorHint);

        _grpPreview.Text = "圖片預覽（可拖曳圖片到此更換）";
        _grpPreview.Location = new Point(pad, 200);
        _grpPreview.Size = new Size(ClientSize.Width - pad * 2, 297);
        _grpPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        _picPreview.Dock = DockStyle.Fill;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = Color.White;
        _picPreview.AllowDrop = true;
        _grpPreview.Controls.Add(_picPreview);

        _lblPreviewPlaceholder.Text = "載入預覽範例中...";
        _lblPreviewPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
        _lblPreviewPlaceholder.Dock = DockStyle.Fill;
        _lblPreviewPlaceholder.ForeColor = Color.Gray;
        _grpPreview.Controls.Add(_lblPreviewPlaceholder);
        _grpPreview.Controls.SetChildIndex(_lblPreviewPlaceholder, 0);
        Controls.Add(_grpPreview);

        _grpPreview.AllowDrop = true;
        _picPreview.AllowDrop = true;
        foreach (var surface in new Control[] { _grpPreview, _picPreview })
        {
            surface.DragEnter += Preview_DragEnter;
            surface.DragDrop += Preview_DragDrop;
        }

        _btnStart.Text = "開始處理";
        _btnStart.Size = new Size(120, 34);
        _btnStart.Location = new Point(pad, 780 - pad - 150);
        _btnStart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnStart.Click += BtnStart_Click;
        Controls.Add(_btnStart);

        _progressBar.Location = new Point(pad + 130, 780 - pad - 148);
        _progressBar.Size = new Size(400, 30);
        _progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_progressBar);

        _lblProgress.Location = new Point(pad + 540, 780 - pad - 146);
        _lblProgress.Size = new Size(220, 25);
        _lblProgress.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _lblProgress.Text = "0%   0 / 0";
        Controls.Add(_lblProgress);

        _lblStats.Location = new Point(pad + 130, 780 - pad - 114);
        _lblStats.Size = new Size(400, 25);
        _lblStats.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _lblStats.Text = "成功 0   失敗 0";
        Controls.Add(_lblStats);

        _lblStatus.Location = new Point(pad, 780 - pad - 76);
        _lblStatus.Size = new Size(780 - pad * 2, 25);
        _lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _lblStatus.ForeColor = Color.DimGray;
        _lblStatus.Text = "請選擇來源與目的資料夾，並輸入浮水印文字。";
        Controls.Add(_lblStatus);

        _txtSource.TextChanged += (_, _) => { OnSourceChanged(); UpdateStartButton(); };
        _txtDestination.TextChanged += (_, _) => UpdateStartButton();
        _txtWatermarkText.TextChanged += (_, _) =>
        {
            _previewDebounce.Stop();
            _previewDebounce.Start();
            UpdateStartButton();
        };
        _trkOpacity.Scroll += (_, _) =>
        {
            _lblOpacityValue.Text = $"{_trkOpacity.Value}%";
            _previewDebounce.Stop();
            _previewDebounce.Start();
        };
        _trkFontSize.Scroll += (_, _) =>
        {
            _lblFontSizeValue.Text = $"{_trkFontSize.Value}（基準 px）";
            _previewDebounce.Stop();
            _previewDebounce.Start();
        };

        UpdateStartButton();
    }

    private Label MakeLabel(string text, int left)
        => new() { Text = text, AutoSize = true, Left = left };

    private void ConfigureBrowseButton(Button button, TextBox target, int left)
    {
        button.Text = "瀏覽...";
        button.Size = new Size(90, 29);
        button.Left = left;
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Click += (_, _) => BrowseFor(target);
    }

    private void ConfigureSlider(TrackBar trackBar, int left, int width, int min, int max, int value)
    {
        trackBar.TickStyle = TickStyle.None;
        trackBar.Minimum = min;
        trackBar.Maximum = max;
        trackBar.Value = value;
        trackBar.Left = left;
        trackBar.Width = width;
        trackBar.Anchor = AnchorStyles.Top | AnchorStyles.Left;
    }

    private void RegisterRow(params Control[] controls)
    {
        foreach (var control in controls)
        {
            if (!Controls.Contains(control))
            {
                Controls.Add(control);
            }
        }

        _layoutRows.Add(controls);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutRows();
    }

    private void LayoutRows()
    {
        const int pad = 12;
        const int rowGap = 6;
        var y = 12;

        foreach (var row in _layoutRows)
        {
            var rowHeight = row.Max(c => c.Height);
            foreach (var control in row)
            {
                control.Top = y + (rowHeight - control.Height) / 2;
            }
            y += rowHeight + rowGap;
        }

        var previewTop = y + 2;
        var bottomLimit = _btnStart.Top - 12;
        _grpPreview.Left = pad;
        _grpPreview.Top = previewTop;
        _grpPreview.Width = ClientSize.Width - pad * 2;
        _grpPreview.Height = Math.Max(120, bottomLimit - previewTop);
    }

    private void BrowseFor(TextBox target)
    {
        using var dlg = new FolderBrowserDialog();
        if (Directory.Exists(target.Text))
        {
            dlg.SelectedPath = target.Text;
        }
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dlg.SelectedPath;
        }
    }

    private WatermarkOptions BuildOptions() => new()
    {
        Text = _txtWatermarkText.Text.Trim(),
        OpacityPercent = _trkOpacity.Value,
        FontSizePx = _trkFontSize.Value,
        Color = _watermarkColor,
    };

    private void BtnColor_Click(object? sender, EventArgs e)
    {
        using var dlg = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = _watermarkColor,
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _watermarkColor = dlg.Color;
            ApplyColorToButton(_watermarkColor);
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }
    }

    private void ApplyColorToButton(Color color)
    {
        _btnColor.BackColor = color;
        _btnColor.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var luminance = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        _btnColor.ForeColor = luminance > 128 ? Color.Black : Color.White;
    }

    private void OnSourceChanged()
    {
        lock (_previewSync)
        {
            _previewSource?.Dispose();
            _previewSource = null;
        }
        _previewVersion++;
        SetPreviewImage(null);
        _lblPreviewPlaceholder.Text = "掃描中...";

        var version = ++_scanVersion;
        var source = _txtSource.Text.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            _ = LoadDefaultPreviewAsync();
            return;
        }
        if (!Directory.Exists(source))
        {
            _lblPreviewPlaceholder.Text = "來源資料夾不存在";
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Yield();
            try
            {
                var first = _discovery.FindFirstValidImage(source);
                if (version != _scanVersion) return;

                if (first is null)
                {
                    BeginInvoke(() =>
                    {
                        if (version == _scanVersion) _lblPreviewPlaceholder.Text = "此資料夾沒有可預覽的圖片（無圖片）";
                    });
                    return;
                }

                SxImage loaded;
                await using (var fs = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    loaded = await SxImageFactory.LoadAsync<SxRgba32>(fs);
                }
                loaded.Mutate(x => x.AutoOrient());

                if (version != _scanVersion)
                {
                    loaded.Dispose();
                    return;
                }

                lock (_previewSync)
                {
                    if (version != _scanVersion)
                    {
                        loaded.Dispose();
                        return;
                    }
                    _previewSource?.Dispose();
                    _previewSource = loaded;
                }

                BeginInvoke(() =>
                {
                    if (version == _scanVersion)
                    {
                        _ = RenderPreviewAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                if (version != _scanVersion) return;
                BeginInvoke(() =>
                {
                    if (version == _scanVersion) _lblPreviewPlaceholder.Text = $"無法預覽: {ex.Message}";
                });
            }
        });
    }

    private const string DefaultPreviewResource = "ImageWatermarkPortable.sample1.jpg";
    private const string AppIconResource = "ImageWatermarkPortable.icon.ico";

    private static string GetInformationalVersion()
    {
        var version = typeof(MainForm).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version)) return "1.0";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(AppIconResource);
            if (stream is not null)
            {
                using var icon = new Icon(stream);
                return (Icon)icon.Clone();
            }
        }
        catch
        {
            // fall through to system icon
        }
        return SystemIcons.Application;
    }

    private async Task LoadDefaultPreviewAsync()
    {
        if (!string.IsNullOrWhiteSpace(_txtSource.Text)) return;

        var version = ++_scanVersion;
        try
        {
            var loaded = await Task.Run(() =>
            {
                using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(DefaultPreviewResource)
                    ?? throw new InvalidOperationException("找不到內建預覽範例圖。");
                var img = SxImageFactory.Load<SxRgba32>(stream);
                img.Mutate(x => x.AutoOrient());
                return img;
            });

            AdoptPreviewSource(version, loaded);
        }
        catch (Exception ex)
        {
            if (version == _scanVersion)
            {
                _lblPreviewPlaceholder.Text = $"無法載入預覽範例: {ex.Message}";
            }
        }
    }

    private void AdoptPreviewSource(int version, SxImage loaded)
    {
        if (version != _scanVersion)
        {
            loaded.Dispose();
            return;
        }

        lock (_previewSync)
        {
            if (version != _scanVersion)
            {
                loaded.Dispose();
                return;
            }
            _previewSource?.Dispose();
            _previewSource = loaded;
        }

        _ = RenderPreviewAsync();
    }

    private void Preview_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = GetDroppedImagePath(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void Preview_DragDrop(object? sender, DragEventArgs e)
    {
        var path = GetDroppedImagePath(e);
        if (path is null) return;
        LoadDroppedPreview(path);
    }

    private static string? GetDroppedImagePath(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return null;
        return files.FirstOrDefault(ImageDiscoveryService.IsSupportedFile);
    }

    private void LoadDroppedPreview(string path)
    {
        var version = ++_scanVersion;
        _lblPreviewPlaceholder.Text = "載入圖片中...";

        _ = Task.Run(async () =>
        {
            SxImage? loaded = null;
            try
            {
                await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    loaded = await SxImageFactory.LoadAsync<SxRgba32>(fs);
                }
                loaded.Mutate(x => x.AutoOrient());

                var adopted = loaded;
                BeginInvoke(() => AdoptPreviewSource(version, adopted));
            }
            catch (Exception ex)
            {
                loaded?.Dispose();
                BeginInvoke(() =>
                {
                    if (version == _scanVersion)
                    {
                        _lblPreviewPlaceholder.Text = $"無法載入圖片: {ex.Message}";
                    }
                });
            }
        });
    }

    private async Task RenderPreviewAsync()
    {
        var version = ++_previewVersion;
        var options = BuildOptions();

        SxImage? local;
        lock (_previewSync)
        {
            if (_previewSource is null)
            {
                _lblPreviewPlaceholder.Text = "無圖片可預覽";
                _picPreview.Image = null;
                return;
            }
            local = _previewSource.Clone();
        }

        try
        {
            var bmp = await Task.Run(() =>
            {
                try
                {
                    return _renderer.RenderPreview(local, options.Text, options.OpacityPercent, options.FontSizePx, 1200, options.Color);
                }
                finally
                {
                    local.Dispose();
                }
            });

            if (version != _previewVersion)
            {
                bmp.Dispose();
                return;
            }

            SetPreviewImage(bmp);
        }
        catch (Exception ex)
        {
            if (version == _previewVersion)
            {
                _lblPreviewPlaceholder.Text = $"預覽失敗: {ex.Message}";
                _lblPreviewPlaceholder.Visible = true;
            }
        }
    }

    private void SetPreviewImage(Bitmap? bmp)
    {
        var old = _picPreview.Image;
        _picPreview.Image = bmp;
        old?.Dispose();
        _lblPreviewPlaceholder.Visible = bmp is null;
        if (bmp is null && _lblPreviewPlaceholder.Text.Length == 0)
        {
            _lblPreviewPlaceholder.Text = "尚未載入預覽圖片";
        }
    }

    private void UpdateStartButton()
    {
        var sourceOk = !string.IsNullOrWhiteSpace(_txtSource.Text) && Directory.Exists(_txtSource.Text);
        var destOk = !string.IsNullOrWhiteSpace(_txtDestination.Text);
        var textOk = !string.IsNullOrWhiteSpace(_txtWatermarkText.Text);
        _btnStart.Enabled = !_processing && sourceOk && destOk && textOk;
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_processing) return;

        var source = _txtSource.Text.Trim();
        var destination = _txtDestination.Text.Trim();

        if (!PathSecurity.TryValidatePaths(source, destination, out var pathError))
        {
            MessageBox.Show(this, pathError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var options = BuildOptions();
        if (string.IsNullOrWhiteSpace(options.Text))
        {
            MessageBox.Show(this, "浮水印文字不可為空白。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(destination);
            var probe = Path.Combine(destination, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"目的資料夾無法寫入：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _processing = true;
        _batchCts = new CancellationTokenSource();
        UpdateStartButton();
        _lblStatus.Text = "掃描來源資料夾中...";

        var files = await Task.Run(() => _discovery.EnumerateImages(source, _batchCts!.Token));

        if (files.Count == 0)
        {
            _processing = false;
            _batchCts.Dispose();
            _batchCts = null;
            _lblStatus.Text = "無圖片：來源資料夾沒有符合支援格式的圖片。";
            _lblProgress.Text = "0%   0 / 0";
            UpdateStartButton();
            return;
        }

        _progressBar.Maximum = files.Count;
        _progressBar.Value = 0;
        _lblProgress.Text = $"0%   0 / {files.Count}";
        _lblStats.Text = "成功 0   失敗 0";
        _lblStatus.Text = $"處理中，共 {files.Count} 張...";

        var progress = new Progress<BatchProgress>(OnProgress);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await Task.Run(() => _batch.ProcessAsync(files, source, destination, options, progress, _batchCts!.Token));
            sw.Stop();
            _lblStatus.Text =
                $"完成：耗時 {result.Elapsed:hh\\:mm\\:ss}　成功 {result.Succeeded}　失敗 {result.Failed}（日誌：{Path.Combine(destination, "watermark-errors.log")}）";
            _lblStats.Text = $"成功 {result.Succeeded}   失敗 {result.Failed}";
            _progressBar.Value = _progressBar.Maximum;
            _lblProgress.Text = $"100%   {result.Total} / {result.Total}";
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "作業已停止。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"批次處理發生錯誤：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblStatus.Text = "批次處理失敗。";
        }
        finally
        {
            _processing = false;
            _progressThrottle.Stop();
            _pendingProgress?.Dispose();
            _pendingProgress = null;
            _batchCts?.Dispose();
            _batchCts = null;
            UpdateStartButton();

            if (_pendingClose)
            {
                BeginInvoke(Close);
            }
        }
    }

    private void OnProgress(BatchProgress p)
    {
        var dropped = _pendingProgress;
        _pendingProgress = p;
        if (dropped is not null && !ReferenceEquals(dropped, p))
        {
            dropped.Dispose();
        }

        if (!_progressThrottle.Enabled)
        {
            _progressThrottle.Start();
        }
        if (p.Processed >= p.Total)
        {
            FlushProgress();
        }
    }

    private void FlushProgress()
    {
        var p = _pendingProgress;
        if (p is null) return;
        _pendingProgress = null;

        if (p.Total <= 0)
        {
            p.Dispose();
            return;
        }

        _progressBar.Maximum = p.Total;
        _progressBar.Value = Math.Min(p.Processed, p.Total);
        _lblProgress.Text = $"{p.Percent}%   {p.Processed} / {p.Total}";
        _lblStats.Text = $"成功 {p.Succeeded}   失敗 {p.Failed}";

        if (p.Preview is not null)
        {
            SetPreviewImage(p.Preview);
        }
        else
        {
            p.Dispose();
        }

        if (_processing && !string.IsNullOrEmpty(p.CurrentFile))
        {
            _lblStatus.Text = $"處理中 {p.Processed} / {p.Total}：{p.CurrentFile}";
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_processing)
        {
            var answer = MessageBox.Show(
                this,
                "作業尚未完成，確定要終止並關閉程式嗎？\n未完成的檔案將不會輸出，日誌會記錄本次狀態。",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            _pendingClose = true;
            Enabled = false;
            _lblStatus.Text = "正在安全終止...請稍候";
            try
            {
                _batchCts?.Cancel();
            }
            catch
            {
                // ignore
            }
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewDebounce.Dispose();
            _progressThrottle.Dispose();
            lock (_previewSync)
            {
                _previewSource?.Dispose();
                _previewSource = null;
            }
            var img = _picPreview.Image;
            _picPreview.Image = null;
            img?.Dispose();
        }
        base.Dispose(disposing);
    }
}
