# Renders docs/icons-preview.png: every built-in icon at 256 px plus 48/32/24/16 px.
param([string]$Background = 'F9F9F9', [string]$Out = (Join-Path $PSScriptRoot '..\docs\icons-preview.png'))
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot '..\src\SMBLinker\Assets'
$names = 'DriveIcons\drive-green', 'DriveIcons\drive-blue', 'DriveIcons\drive-orange', 'DriveIcons\drive-purple', 'DriveIcons\drive-red',
         'DriveIcons\drive-teal', 'DriveIcons\nas-rack', 'DriveIcons\drive-cloud', 'DriveIcons\drive-media', 'DriveIcons\drive-backup',
         'DriveIcons\drive-home', 'DriveIcons\drive-photo', 'DriveIcons\drive-lock', 'app'
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$h = [Convert]::ToInt32($Background, 16)
$bmp = New-Object System.Drawing.Bitmap 1500, 560
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, ($h -shr 16) -band 255, ($h -shr 8) -band 255, $h -band 255))
$font = New-Object System.Drawing.Font 'Segoe UI', 10
$i = 0
foreach ($n in $names) {
    $p = Join-Path $dir "$n.ico"
    $x = 12 + ($i % 7) * 212; $y = 8 + [math]::Floor($i / 7) * 280
    $g.DrawIcon((New-Object System.Drawing.Icon $p, 256, 256), (New-Object System.Drawing.Rectangle $x, $y, 192, 192))
    $k = 0
    foreach ($sz in 48, 32, 24, 16) {
        $g.DrawIcon((New-Object System.Drawing.Icon $p, $sz, $sz), (New-Object System.Drawing.Rectangle ($x + $k), ($y + 196 + (48 - $sz)), $sz, $sz))
        $k += $sz + 12
    }
    $g.DrawString((Split-Path $n -Leaf), $font, [System.Drawing.Brushes]::Gray, $x, ($y + 250))
    $i++
}
$bmp.Save($Out)
$Out
