<#
.SYNOPSIS
  Puts the launcher's art in place: each known picture of a source folder (PNG, WebP or JPEG) becomes an 8-bit PNG in
  src/Darklands.Launcher/Resources/art, at most at its size in the list below; the next build embeds it and it replaces the
  launcher's drawn stand-in. A picture missing from the source folder leaves the art already there (or the stand-in) as it is.
  A sliced picture (nine slices: a card; three: a plate) comes with <name>.slice.txt, its borders "left top right bottom" in
  its pixels (tools/launcher-art/SheetCutter writes both from the artist's sheets); it is copied along, scaled with the picture.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\launcher-art\Convert-LauncherArt.ps1 -Source D:\art\launcher

  Uses Windows' own image decoders (WIC: WebP needs Windows 10 1809 or later). The names, sizes and how each is drawn are in
  tools/launcher-art/ASSETS.md.
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\src\Darklands.Launcher\Resources\art')
)

Add-Type -AssemblyName PresentationCore, WindowsBase

# name -> largest width, largest height, whether it keeps transparency (else stored without alpha, smaller)
$known = [ordered]@{
    'background'          = @(2560, 1440, $false)
    'card-left'           = @(1300, 1300, $true)
    'card-settings'       = @(1300, 1300, $true)
    'corner'              = @(200, 200, $true)
    'fleuron'             = @(200, 240, $true)
    'rule-left'           = @(600, 40, $true)
    'rule-right'          = @(600, 40, $true)
    'dropcap'             = @(200, 200, $true)
    'plate-stone'         = @(2000, 96, $true)
    'plate-red'           = @(2000, 96, $true)
    'plate-small'         = @(2000, 56, $true)
    'plate-small-hover'   = @(2000, 56, $true)
    'plate-small-pressed' = @(2000, 56, $true)
    'hint-bar'            = @(2000, 120, $true)
    'icon-game'           = @(128, 128, $true)
    'icon-display'        = @(128, 128, $true)
    'icon-sound'          = @(128, 128, $true)
    'icon-startup'        = @(128, 128, $true)
    'icon-link'           = @(128, 128, $true)
    'icon-problem'        = @(64, 64, $true)
    'icon-found'          = @(64, 64, $true)
}

New-Item -ItemType Directory -Force $Destination | Out-Null
$done = 0
foreach ($name in $known.Keys) {
    $file = Get-ChildItem -LiteralPath $Source -File | Where-Object { $_.BaseName -ieq $name -and $_.Extension -in '.png', '.webp', '.jpg', '.jpeg' } | Select-Object -First 1
    if ($null -eq $file) { continue }
    $maxWidth, $maxHeight, $alpha = $known[$name]
    $stream = [System.IO.File]::OpenRead($file.FullName)
    try {
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create($stream, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = $decoder.Frames[0]
    } finally { $stream.Dispose() }
    $scale = [Math]::Min(1.0, [Math]::Min($maxWidth / $frame.PixelWidth, $maxHeight / $frame.PixelHeight))
    $bitmap = [System.Windows.Media.Imaging.BitmapSource]$frame
    if ($scale -lt 1.0) {
        $bitmap = New-Object System.Windows.Media.Imaging.TransformedBitmap($frame, (New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    }
    $format = if ($alpha) { [System.Windows.Media.PixelFormats]::Bgra32 } else { [System.Windows.Media.PixelFormats]::Bgr24 }
    $converted = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($bitmap, $format, $null, 0)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Interlace = [System.Windows.Media.Imaging.PngInterlaceOption]::Off
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($converted))
    $out = Join-Path $Destination ($name + '.png')
    $target = [System.IO.File]::Create($out)
    try { $encoder.Save($target) } finally { $target.Dispose() }
    # a sliced picture's borders (left top right bottom, in its pixels) go along, scaled with it
    $slice = Join-Path $Source ($name + '.slice.txt')
    $sliceOut = Join-Path $Destination ($name + '.slice.txt')
    if (Test-Path -LiteralPath $slice) {
        $borders = (Get-Content -LiteralPath $slice -Raw).Trim() -split '\s+' | ForEach-Object { [int][Math]::Round([int]$_ * $scale) }
        [System.IO.File]::WriteAllText($sliceOut, ($borders -join ' ') + "`n")
    } elseif (Test-Path -LiteralPath $sliceOut) {
        Remove-Item -LiteralPath $sliceOut
    }
    "{0,-20} {1} x {2} -> {3} x {4}  {5}{6}" -f $name, $frame.PixelWidth, $frame.PixelHeight, $converted.PixelWidth, $converted.PixelHeight, $out, $(if (Test-Path -LiteralPath $slice) { '  slice ' + ($borders -join ' ') } else { '' })
    $done++
}
$unknown = Get-ChildItem -LiteralPath $Source -File | Where-Object { -not $known.Contains($_.BaseName.ToLowerInvariant()) -and $_.Extension -in '.png', '.webp', '.jpg', '.jpeg' -and $_.BaseName -notlike '*-keyed' }
foreach ($u in $unknown) { "not a launcher picture (see ASSETS.md), left out: " + $u.Name }
"$done picture(s) converted. Rebuild the program to embed them."
