# 生成 wont-sleep 的图标资源（矢量绘制，可重复运行）。
# 用法：powershell -ExecutionPolicy Bypass -File Assets\source\generate-icon.ps1
# 注意：本文件必须以 UTF-8 with BOM 保存，否则 Windows PowerShell 5.1 会按 ANSI 解析中文。

param(
    [string]$AssetsDirectory = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class IconArt
{
    /// <summary>sleeping = true 时绘制闭眼（未阻止休眠）状态。</summary>
    public static Bitmap Render(int size, bool sleeping)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float s = size;

            // 圆角方形底：清醒态为亮紫渐变，休眠态转为偏暗的低饱和紫灰色。
            var from = sleeping ? Color.FromArgb(255, 0x2A, 0x27, 0x40) : Color.FromArgb(255, 0x4A, 0x2E, 0x8F);
            var to = sleeping ? Color.FromArgb(255, 0x4C, 0x44, 0x68) : Color.FromArgb(255, 0x8B, 0x5C, 0xF6);

            float radius = s * 0.22f;
            using (var path = RoundedRect(new RectangleF(0, 0, s, s), radius))
            using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), from, to))
            {
                g.FillPath(brush, path);
            }

            float cx = s * 0.5f;
            float cy = s * 0.5f;

            if (sleeping)
            {
                DrawClosedEye(g, s, cx, cy);
            }
            else
            {
                DrawOpenEye(g, s, cx, cy);
            }
        }

        return bitmap;
    }

    private static void DrawOpenEye(Graphics g, float s, float cx, float cy)
    {
        float halfWidth = s * 0.36f;
        float inset = s * 0.245f;

        using (var eye = new GraphicsPath())
        {
            eye.AddBezier(
                cx - halfWidth, cy,
                cx - halfWidth * 0.45f, cy - inset,
                cx + halfWidth * 0.45f, cy - inset,
                cx + halfWidth, cy);
            eye.AddBezier(
                cx + halfWidth, cy,
                cx + halfWidth * 0.45f, cy + inset,
                cx - halfWidth * 0.45f, cy + inset,
                cx - halfWidth, cy);

            using (var pen = new Pen(Color.White, s * 0.085f))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawPath(pen, eye);
            }
        }

        float pupilRadius = s * 0.13f;
        using (var pupil = new GraphicsPath())
        {
            pupil.AddEllipse(cx - pupilRadius, cy - pupilRadius, pupilRadius * 2, pupilRadius * 2);

            using (var brush = new PathGradientBrush(pupil))
            {
                brush.CenterColor = Color.FromArgb(255, 0xFF, 0xD9, 0x66);
                brush.SurroundColors = new[] { Color.FromArgb(255, 0xF0, 0x9A, 0x10) };
                brush.FocusScales = new PointF(0.25f, 0.25f);
                g.FillPath(brush, pupil);
            }
        }
    }

    private static void DrawClosedEye(Graphics g, float s, float cx, float cy)
    {
        // 向下弯的眼睑弧线 + 三道短睫线，表示眼睛闭着。
        float halfWidth = s * 0.265f;
        float y = cy - s * 0.015f;
        float depth = s * 0.13f;

        var lidColor = Color.FromArgb(255, 0xD9, 0xD3, 0xEC);
        var p0 = new PointF(cx - halfWidth, y);
        var p1 = new PointF(cx - halfWidth * 0.55f, y + depth);
        var p2 = new PointF(cx + halfWidth * 0.55f, y + depth);
        var p3 = new PointF(cx + halfWidth, y);

        using (var lid = new GraphicsPath())
        {
            lid.AddBezier(p0, p1, p2, p3);

            using (var pen = new Pen(lidColor, s * 0.082f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawPath(pen, lid);
            }
        }

        // 睫线沿曲线取点，保证与眼睑相连。
        using (var pen = new Pen(lidColor, s * 0.045f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;

            foreach (var t in new[] { 0.2f, 0.5f, 0.8f })
            {
                var point = BezierPoint(p0, p1, p2, p3, t);
                var end = new PointF(
                    point.X + s * 0.055f * (t - 0.5f),
                    point.Y + s * 0.055f);
                g.DrawLine(pen, point, end);
            }
        }
    }

    private static PointF BezierPoint(PointF p0, PointF p1, PointF p2, PointF p3, float t)
    {
        float u = 1 - t;
        float a = u * u * u;
        float b = 3 * u * u * t;
        float c = 3 * u * t * t;
        float d = t * t * t;

        return new PointF(
            a * p0.X + b * p1.X + c * p2.X + d * p3.X,
            a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
    }

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
'@ -ReferencedAssemblies System.Drawing

function Get-SquareBitmap {
    param(
        [System.Drawing.Image]$Source,
        [int]$Size
    )

    # 逐级减半，避免一次性大比例缩放导致的模糊。
    $current = $Source
    while ($current.Width -ge $Size * 2) {
        $half = [int]($current.Width / 2)
        $next = New-Object System.Drawing.Bitmap($half, $half, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($next)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($current, 0, 0, $half, $half)
        $graphics.Dispose()

        if (-not [object]::ReferenceEquals($current, $Source)) {
            $current.Dispose()
        }

        $current = $next
    }

    $result = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($result)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($current, (New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)))
    $graphics.Dispose()

    if (-not [object]::ReferenceEquals($current, $Source)) {
        $current.Dispose()
    }

    return $result
}

function Save-Composite {
    param(
        [System.Drawing.Image]$Source,
        [string]$Path,
        [int]$CanvasWidth,
        [int]$CanvasHeight,
        [int]$IconSize
    )

    $icon = Get-SquareBitmap -Source $Source -Size $IconSize
    $canvas = New-Object System.Drawing.Bitmap($CanvasWidth, $CanvasHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $x = [int](($CanvasWidth - $IconSize) / 2)
    $y = [int](($CanvasHeight - $IconSize) / 2)
    $graphics.DrawImage($icon, $x, $y, $IconSize, $IconSize)
    $graphics.Dispose()

    $canvas.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    $icon.Dispose()
}

function Get-IconImageBytes {
    param(
        [System.Drawing.Bitmap]$Bitmap
    )

    # 输出 32 位 BMP（DIB）格式的图标图像数据：
    # BITMAPINFOHEADER + 自下而上的 BGRA 像素 + 全 0 的 AND 掩码（透明度交给 alpha 通道）。
    $size = $Bitmap.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $Bitmap.UnlockBits($data)

    $xorBytes = New-Object byte[] ($size * $size * 4)
    for ($y = 0; $y -lt $size; $y++) {
        $sourceOffset = ($size - 1 - $y) * $stride
        [Array]::Copy($pixels, $sourceOffset, $xorBytes, ($y * $size * 4), ($size * 4))
    }

    $maskRowBytes = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $maskBytes = New-Object byte[] ($maskRowBytes * $size)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    $writer.Write([UInt32]40)                  # biSize
    $writer.Write([Int32]$size)                # biWidth
    $writer.Write([Int32]($size * 2))          # biHeight（XOR + AND）
    $writer.Write([UInt16]1)                   # biPlanes
    $writer.Write([UInt16]32)                  # biBitCount
    $writer.Write([UInt32]0)                   # biCompression = BI_RGB
    $writer.Write([UInt32]($size * $size * 4)) # biSizeImage
    $writer.Write([Int32]0)                    # biXPelsPerMeter
    $writer.Write([Int32]0)                    # biYPelsPerMeter
    $writer.Write([UInt32]0)                   # biClrUsed
    $writer.Write([UInt32]0)                   # biClrImportant
    $writer.Write($xorBytes)
    $writer.Write($maskBytes)
    $writer.Flush()

    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()

    return $bytes
}

function Save-MultiSizeIcon {
    param(
        [System.Drawing.Image]$Source,
        [string]$Path,
        [int[]]$Sizes
    )

    $payloads = New-Object System.Collections.ArrayList

    foreach ($size in $Sizes) {
        $square = Get-SquareBitmap -Source $Source -Size $size
        # 注意：PowerShell 会把函数返回的 byte[] 展开成对象数组，这里显式转回 byte[]。
        [void]$payloads.Add([byte[]](Get-IconImageBytes -Bitmap $square))
        $square.Dispose()
    }

    $output = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($output)

    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$Sizes.Count)

    $offset = 6 + (16 * $Sizes.Count)
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $size = $Sizes[$i]
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$payloads[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $payloads[$i].Length
    }

    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $writer.Write($payloads[$i])
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($Path, $output.ToArray())
    $writer.Dispose()
    $output.Dispose()
}

$icoSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

# 清醒态（正在阻止休眠 / 应用图标）
$awake = [IconArt]::Render(1024, $false)
$awake.Save((Join-Path $AssetsDirectory 'source\AppIcon-master.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# 休眠态（未阻止休眠 / 托盘与窗口图标）
$sleeping = [IconArt]::Render(1024, $true)
$sleeping.Save((Join-Path $AssetsDirectory 'source\AppIconIdle-master.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$pngTargets = @(
    @{ Name = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300 },
    @{ Name = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88 },
    @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Width = 24; Height = 24 },
    @{ Name = 'Square44x44Logo.targetsize-48_altform-lightunplated.png'; Width = 48; Height = 48 },
    @{ Name = 'LockScreenLogo.scale-200.png'; Width = 48; Height = 48 },
    @{ Name = 'StoreLogo.png'; Width = 50; Height = 50 }
)

foreach ($target in $pngTargets) {
    $square = Get-SquareBitmap -Source $awake -Size $target.Width
    $square.Save((Join-Path $AssetsDirectory $target.Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $square.Dispose()
}

Save-Composite -Source $awake -Path (Join-Path $AssetsDirectory 'Wide310x150Logo.scale-200.png') -CanvasWidth 620 -CanvasHeight 300 -IconSize 200
Save-Composite -Source $awake -Path (Join-Path $AssetsDirectory 'SplashScreen.scale-200.png') -CanvasWidth 1240 -CanvasHeight 600 -IconSize 400

Save-MultiSizeIcon -Source $awake -Path (Join-Path $AssetsDirectory 'AppIcon.ico') -Sizes $icoSizes
Save-MultiSizeIcon -Source $sleeping -Path (Join-Path $AssetsDirectory 'AppIconIdle.ico') -Sizes $icoSizes

$awake.Dispose()
$sleeping.Dispose()

Write-Output '图标资源已生成（AppIcon.ico 睁眼 / AppIconIdle.ico 闭眼）。'
