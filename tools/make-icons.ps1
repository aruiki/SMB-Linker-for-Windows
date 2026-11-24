# Generates every icon used by SmbAutoMount (original artwork, MIT licensed with the project).
#   src/SmbAutoMount/Assets/app.ico              application icon
#   src/SmbAutoMount/Assets/DriveIcons/*.ico     built-in drive icons for File Explorer
#
# Style: flat pictograms - solid fills only, no gradients, no gloss, no shadows, no outlines.
# Overlays are separated from the drive by a transparent "knock-out" gap, so the icons work
# on light and dark backgrounds alike. Everything is drawn at up to 1024 px and downscaled.
#
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\src\SmbAutoMount\Assets'
$driveDir = Join-Path $assets 'DriveIcons'
New-Item -ItemType Directory -Force $driveDir | Out-Null

# ---------- palette ----------
$ColTop = 'DCE0E6'   # drive top
$ColBody = '343B47'      # drive front / NAS body
$ColDetail = '4B5462'    # slots, bays
$ColCap = 'B9C0CA'       # pipe end caps
$ColMetal = '9AA3AE'     # lock shackle

# ---------- helpers ----------
function C([string]$hex, [int]$a = 255) {
    $h = $hex.TrimStart('#')
    [System.Drawing.Color]::FromArgb($a, [Convert]::ToInt32($h.Substring(0, 2), 16), [Convert]::ToInt32($h.Substring(2, 2), 16), [Convert]::ToInt32($h.Substring(4, 2), 16))
}
function Brush([string]$hex) { New-Object System.Drawing.SolidBrush (C $hex) }
function PtF([single]$x, [single]$y) { New-Object System.Drawing.PointF $x, $y }
function NewPath { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $p.FillMode = 'Winding'; $p }
function RoundRect($p, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = [Math]::Max(0.5, [Math]::Min($r * 2, [Math]::Min($w, $h)))
    $p.StartFigure()
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
}
function Polygon($p, [single[]]$xy) {
    $pts = for ($i = 0; $i -lt $xy.Count; $i += 2) { PtF $xy[$i] $xy[$i + 1] }
    $p.AddPolygon([System.Drawing.PointF[]]$pts)
}
# Erase a ring around a silhouette so the overlay floats with a clean gap.
function Knockout($g, $path, [single]$gap) {
    $clear = [System.Drawing.Color]::Transparent
    $pen = New-Object System.Drawing.Pen $clear, ($gap * 2)
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $mode = $g.CompositingMode
    $g.CompositingMode = 'SourceCopy'
    $g.FillPath((New-Object System.Drawing.SolidBrush $clear), $path)
    $g.DrawPath($pen, $path)
    $g.CompositingMode = $mode
}

# ---------- drive ----------
function Draw-Drive($g, [int]$s, [string]$accent) {
    $state = $g.Save()
    if ($script:Target -le 24) {
        # keep the slab readable at small sizes (Windows hand-tunes its 16 px icons the same way)
        $k = if ($script:Target -le 20) { 1.5 } else { 1.22 }
        $g.TranslateTransform($s * 0.5, $s * 0.55); $g.ScaleTransform(1.06, $k); $g.TranslateTransform(-$s * 0.5, -$s * 0.55)
    }
    # top face
    $top = NewPath; Polygon $top @(($s * 0.20), ($s * 0.30), ($s * 0.80), ($s * 0.30), ($s * 0.92), ($s * 0.42), ($s * 0.08), ($s * 0.42))
    $g.FillPath((Brush $ColTop), $top)
    # front face (square top corners, rounded bottom corners)
    $front = NewPath; RoundRect $front ($s * 0.08) ($s * 0.42) ($s * 0.84) ($s * 0.25) ($s * 0.035)
    $front.AddRectangle((New-Object System.Drawing.RectangleF ($s * 0.08), ($s * 0.42), ($s * 0.84), ($s * 0.06)))
    $g.FillPath((Brush $ColBody), $front)
    # LED + slot
    $g.FillEllipse((Brush $accent), ($s * 0.155), ($s * 0.505), ($s * 0.07), ($s * 0.07))
    if ($script:Target -ge 32) {
        $slot = NewPath; RoundRect $slot ($s * 0.30) ($s * 0.525) ($s * 0.50) ($s * 0.03) ($s * 0.015)
        $g.FillPath((Brush $ColDetail), $slot)
    }
    # network pipe with end caps
    $pipe = NewPath; RoundRect $pipe ($s * 0.16) ($s * 0.64) ($s * 0.68) ($s * 0.10) ($s * 0.05)
    $g.FillPath((Brush $accent), $pipe)
    foreach ($cx in 0.10, 0.80) {
        $c = NewPath; RoundRect $c ($s * $cx) ($s * 0.625) ($s * 0.10) ($s * 0.13) ($s * 0.04)
        $g.FillPath((Brush $ColCap), $c)
    }
    $g.Restore($state)
}

# ---------- overlays (flat pictograms, top-right) ----------
function Draw-Overlay($g, [int]$s, [string]$accent, [string]$kind) {
    if ($script:Target -lt 24) { return }   # unreadable at 16/20 px; the pipe colour carries the meaning
    $ox = $s * 0.53; $oy = $s * 0.00; $w = $s * 0.46
    function X([single]$v) { $ox + $w * $v }
    function Y([single]$v) { $oy + $w * $v }
    $shape = NewPath
    $white = NewPath   # white details drawn on top of the accent shape
    $metal = NewPath   # grey details

    switch ($kind) {
        'home' {
            Polygon $shape @((X 0.02), (Y 0.50), (X 0.50), (Y 0.08), (X 0.98), (Y 0.50), (X 0.86), (Y 0.50), (X 0.86), (Y 0.96), (X 0.14), (Y 0.96), (X 0.14), (Y 0.50))
            $shape.AddRectangle((New-Object System.Drawing.RectangleF (X 0.68), (Y 0.12), ($w * 0.12), ($w * 0.26)))
            RoundRect $white (X 0.40) (Y 0.62) ($w * 0.20) ($w * 0.34) ($w * 0.06)
            $white.AddRectangle((New-Object System.Drawing.RectangleF (X 0.40), (Y 0.70), ($w * 0.20), ($w * 0.26)))
        }
        'lock' {
            $shackle = NewPath
            $shackle.AddArc((X 0.26), (Y 0.06), ($w * 0.48), ($w * 0.52), 180, 180)
            $shackle.AddLine((X 0.74), (Y 0.32), (X 0.74), (Y 0.50))
            $shackle.StartFigure()
            $shackle.AddLine((X 0.26), (Y 0.50), (X 0.26), (Y 0.32))
            $sh = $shackle.Clone(); $sh.Widen((New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), ($w * 0.12)))
            $metal.AddPath($sh, $false)
            RoundRect $shape (X 0.12) (Y 0.42) ($w * 0.76) ($w * 0.54) ($w * 0.10)
            $white.AddEllipse((X 0.43), (Y 0.56), ($w * 0.14), ($w * 0.14))
            RoundRect $white (X 0.465) (Y 0.62) ($w * 0.07) ($w * 0.20) ($w * 0.03)
        }
        'cloud' {
            $shape.AddEllipse((X 0.04), (Y 0.42), ($w * 0.38), ($w * 0.38))
            $shape.AddEllipse((X 0.24), (Y 0.18), ($w * 0.50), ($w * 0.50))
            $shape.AddEllipse((X 0.54), (Y 0.38), ($w * 0.42), ($w * 0.42))
            RoundRect $shape (X 0.23) (Y 0.52) ($w * 0.56) ($w * 0.28) ($w * 0.02)
        }
        'media' {
            RoundRect $shape (X 0.06) (Y 0.10) ($w * 0.88) ($w * 0.80) ($w * 0.18)
            Polygon $white @((X 0.40), (Y 0.31), (X 0.40), (Y 0.69), (X 0.71), (Y 0.50))
        }
        'photo' {
            RoundRect $shape (X 0.04) (Y 0.12) ($w * 0.92) ($w * 0.76) ($w * 0.12)
            Polygon $white @((X 0.14), (Y 0.78), (X 0.38), (Y 0.46), (X 0.56), (Y 0.66), (X 0.68), (Y 0.54), (X 0.86), (Y 0.78))
            $white.AddEllipse((X 0.62), (Y 0.24), ($w * 0.14), ($w * 0.14))
        }
        'backup' {
            $a = $w * 0.68; $cx = X 0.5; $cy = Y 0.52; $start = 300.0; $sweep = 280.0
            $arc = New-Object System.Drawing.Drawing2D.GraphicsPath; $arc.AddArc($cx - $a / 2, $cy - $a / 2, $a, $a, $start, $sweep)
            $ring = $arc.Clone(); $ring.Widen((New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), ($w * 0.16)))
            $shape.AddPath($ring, $false)
            $th = ($start + $sweep) * [Math]::PI / 180
            $ex = $cx + [Math]::Cos($th) * $a / 2; $ey = $cy + [Math]::Sin($th) * $a / 2
            $tx = -[Math]::Sin($th); $ty = [Math]::Cos($th); $nx = [Math]::Cos($th); $ny = [Math]::Sin($th)
            $hl = $w * 0.25; $hw = $w * 0.21
            Polygon $shape @(($ex + $tx * $hl), ($ey + $ty * $hl), ($ex + $nx * $hw), ($ey + $ny * $hw), ($ex - $nx * $hw), ($ey - $ny * $hw))
            # clock hands
            RoundRect $shape ($cx - $w * 0.04) ($cy - $a * 0.26) ($w * 0.08) ($a * 0.30) ($w * 0.04)
            RoundRect $shape ($cx - $w * 0.04) ($cy - $w * 0.04) ($a * 0.26) ($w * 0.08) ($w * 0.04)
        }
    }
    $silhouette = NewPath; $silhouette.AddPath($shape, $false)
    if ($metal.PointCount -gt 0) { $silhouette.AddPath($metal, $false) }
    Knockout $g $silhouette ($s * 0.035)
    if ($metal.PointCount -gt 0) { $g.FillPath((Brush $ColMetal), $metal) }
    $g.FillPath((Brush $accent), $shape)
    if ($white.PointCount -gt 0) { $g.FillPath((Brush 'FFFFFF'), $white) }
}

# ---------- NAS tower ----------
function Draw-Nas($g, [int]$s, [string]$accent) {
    $body = NewPath; RoundRect $body ($s * 0.22) ($s * 0.06) ($s * 0.56) ($s * 0.88) ($s * 0.07)
    $g.FillPath((Brush $ColBody), $body)
    for ($i = 0; $i -lt 3; $i++) {
        $by = $s * (0.16 + $i * 0.25)
        $bay = NewPath; RoundRect $bay ($s * 0.30) $by ($s * 0.40) ($s * 0.18) ($s * 0.03)
        $g.FillPath((Brush $ColDetail), $bay)
        $g.FillEllipse((Brush $accent), ($s * 0.59), ($by + $s * 0.06), ($s * 0.06), ($s * 0.06))
    }
}

# ---------- ICO writer: PNG for 256, 32-bit DIB below (readable by every ICO loader) ----------
function New-Frame([int]$size, [scriptblock]$draw) {
    $script:Target = $size
    $big = [Math]::Min(1024, $size * 8)
    $hi = New-Object System.Drawing.Bitmap $big, $big, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $hg = [System.Drawing.Graphics]::FromImage($hi)
    $hg.SmoothingMode = 'AntiAlias'; $hg.PixelOffsetMode = 'HighQuality'
    $hg.Clear([System.Drawing.Color]::Transparent)
    & $draw $hg $big
    $hg.Dispose()
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingMode = 'SourceCopy'
    $attr = New-Object System.Drawing.Imaging.ImageAttributes; $attr.SetWrapMode('TileFlipXY')
    $g.DrawImage($hi, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), 0, 0, $big, $big, 'Pixel', $attr)
    $g.Dispose(); $hi.Dispose()

    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $bw.Write([uint32]($size * $size * 4)); $bw.Write([int32]0); $bw.Write([int32]0)
        $bw.Write([uint32]0); $bw.Write([uint32]0)
        $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $size, $size), 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
        $row = New-Object byte[] ($size * 4)
        for ($y = $size - 1; $y -ge 0; $y--) {
            [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
            $bw.Write($row)
        }
        $bmp.UnlockBits($data)
        $bw.Write((New-Object byte[] ([int]([Math]::Ceiling($size / 32.0) * 4) * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

function Write-Ico([string]$path, [scriptblock]$draw) {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
    $images = @(); foreach ($s in $sizes) { $images += , (New-Frame $s $draw) }
    $bw = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($path))
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = [byte]($(if ($s -ge 256) { 0 } else { $s }))
        $bw.Write($b); $bw.Write($b); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $bw.Write($img) }
    $bw.Close()
}

# ---------- app icon: blue tile, white drive pictogram, green "connected" check ----------
Write-Ico (Join-Path $assets 'app.ico') {
    param($g, $s)
    $tile = NewPath; RoundRect $tile 0 0 $s $s ($s * 0.22)
    $g.FillPath((Brush '2563EB'), $tile)
    $top = NewPath; Polygon $top @(($s * 0.27), ($s * 0.28), ($s * 0.73), ($s * 0.28), ($s * 0.82), ($s * 0.38), ($s * 0.18), ($s * 0.38))
    $g.FillPath((Brush 'BFD4FF'), $top)
    $front = NewPath; RoundRect $front ($s * 0.18) ($s * 0.38) ($s * 0.64) ($s * 0.22) ($s * 0.03)
    $front.AddRectangle((New-Object System.Drawing.RectangleF ($s * 0.18), ($s * 0.38), ($s * 0.64), ($s * 0.05)))
    $g.FillPath((Brush 'FFFFFF'), $front)
    $g.FillEllipse((Brush '2563EB'), ($s * 0.25), ($s * 0.465), ($s * 0.06), ($s * 0.06))
    $pipe = NewPath; RoundRect $pipe ($s * 0.24) ($s * 0.64) ($s * 0.52) ($s * 0.08) ($s * 0.04)
    $g.FillPath((Brush 'BFD4FF'), $pipe)
    $badge = NewPath; $badge.AddEllipse(($s * 0.60), ($s * 0.60), ($s * 0.34), ($s * 0.34))
    Knockout $g $badge ($s * 0.035)
    $g.FillPath((Brush '22C55E'), $badge)
    $pen = New-Object System.Drawing.Pen (C 'FFFFFF'), ($s * 0.05); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((PtF ($s * 0.69) ($s * 0.77)), (PtF ($s * 0.755) ($s * 0.835)), (PtF ($s * 0.86) ($s * 0.715))))
}

# ---------- drive icons ----------
$colors = [ordered]@{ green = '16A34A'; blue = '2563EB'; orange = 'EA580C'; purple = '9333EA'; red = 'DC2626'; teal = '0D9488' }
foreach ($name in $colors.Keys) {
    $a = $colors[$name]
    Write-Ico (Join-Path $driveDir "drive-$name.ico") ([scriptblock]::Create("param(`$g, `$s) Draw-Drive `$g `$s '$a'"))
}
$overlays = [ordered]@{ cloud = '2563EB'; media = '9333EA'; backup = '0D9488'; home = 'EA580C'; photo = '16A34A'; lock = 'DC2626' }
foreach ($name in $overlays.Keys) {
    $a = $overlays[$name]
    Write-Ico (Join-Path $driveDir "drive-$name.ico") ([scriptblock]::Create("param(`$g, `$s) Draw-Drive `$g `$s '$a'; Draw-Overlay `$g `$s '$a' '$name'"))
}
Write-Ico (Join-Path $driveDir 'nas-rack.ico') { param($g, $s) Draw-Nas $g $s '22C55E' }

Get-ChildItem $driveDir | Select-Object Name, Length
