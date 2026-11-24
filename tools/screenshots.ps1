# Captures README screenshots from the app's --demo mode (sample data, no real servers).
#   powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1 [-Exe path\to\SMBLinker.exe]
# Output: docs/screenshots/<lang>-<shot>.png
param([string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class Cap {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
  public struct R { public int L, T, Ri, B; }
  public static IntPtr Find(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { R r; GetWindowRect(h, out r); if (r.Ri - r.L > 300) { found = h; return false; } }
      return true; }, IntPtr.Zero);
    return found;
  }
}
"@
$root = Split-Path $PSScriptRoot
if (-not $Exe) { $Exe = Get-ChildItem (Join-Path $root 'src\SMBLinker\bin') -Recurse -Filter SMBLinker.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName }
$out = Join-Path $root 'docs\screenshots'
New-Item -ItemType Directory -Force $out | Out-Null

function Shoot([string]$name, [string[]]$demoArgs, [int]$wait = 9) {
    $p = Start-Process $Exe -ArgumentList (@('--demo') + $demoArgs) -PassThru
    try {
        $h = [IntPtr]::Zero
        for ($i = 0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 500; $h = [Cap]::Find([uint32]$p.Id) }
        if ($h -eq [IntPtr]::Zero) { throw "window not found for $name" }
        Start-Sleep $wait
        $wr = New-Object Cap+R; [void][Cap]::GetWindowRect($h, [ref]$wr)
        $fr = New-Object Cap+R; [void][Cap]::DwmGetWindowAttribute($h, 9, [ref]$fr, 16)   # visible frame, no shadow
        $bmp = New-Object System.Drawing.Bitmap ($wr.Ri - $wr.L), ($wr.B - $wr.T)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc(); [void][Cap]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
        $crop = New-Object System.Drawing.Rectangle ($fr.L - $wr.L), ($fr.T - $wr.T), ($fr.Ri - $fr.L), ($fr.B - $fr.T)
        $final = $bmp.Clone($crop, $bmp.PixelFormat)
        $file = Join-Path $out "$name.png"
        $final.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $final.Dispose(); $bmp.Dispose()
        $file
    } finally {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
}

foreach ($lang in 'en', 'ja') {
    Shoot "$lang-main-light" @("--demo-lang=$lang", '--demo-theme=light')
    Shoot "$lang-main-dark" @("--demo-lang=$lang", '--demo-theme=dark')
    Shoot "$lang-edit" @("--demo-lang=$lang", '--demo-theme=light', '--demo-open=edit')
    Shoot "$lang-diag" @("--demo-lang=$lang", '--demo-theme=light', '--demo-open=diag')
}
