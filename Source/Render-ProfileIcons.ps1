param([switch]$HighResolution)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
$culture = [Globalization.CultureInfo]::InvariantCulture

function Render-Mark($artwork, [double]$width, [int]$size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $scale = $size / $width
        $drawing.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
        $drawing.DrawDrawing($artwork)
        $drawing.Pop()
    } finally { $drawing.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $data = [IO.MemoryStream]::new()
    try { $encoder.Save($data); return ,$data.ToArray() } finally { $data.Dispose() }
}

function Save-Icon($path, $artwork, $width) {
    $sizes = @(16,20,24,32,40,48,64,96,128,256)
    $frames = foreach ($size in $sizes) { ,(Render-Mark $artwork $width $size) }
    $writer = [IO.BinaryWriter]::new([IO.File]::Create($path))
    try {
        $writer.Write([byte[]](0,0,1,0,$sizes.Count,0))
        $offset = 6 + $sizes.Count * 16
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $size = $sizes[$i]
            $length = $frames[$i].Length
            $dimension = $size % 256
            $writer.Write([byte[]]($dimension,$dimension,0,0,1,0,32,0))
            $writer.Write([int]$length)
            $writer.Write([int]$offset)
            $offset += $length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose() }
}

function Place-Mark($geometry, [double]$offset, [double]$scale) {
    $placed = $geometry.Clone()
    $transform = [Windows.Media.TransformGroup]::new()
    $transform.Children.Add($geometry.Transform)
    $transform.Children.Add([Windows.Media.ScaleTransform]::new($scale, $scale))
    $transform.Children.Add([Windows.Media.TranslateTransform]::new($offset, $offset))
    $placed.Transform = $transform
    return $placed
}

function Draw-DarkMark($geometry) {
    $bounds = $geometry.Bounds
    $disk = [Windows.Media.EllipseGeometry]::new(
        [Windows.Point]::new($bounds.X + $bounds.Width / 2, $bounds.Y + $bounds.Height / 2),
        $bounds.Width / 2, $bounds.Height / 2)
    $waves = [Windows.Media.Geometry]::Combine($disk, $geometry, [Windows.Media.GeometryCombineMode]::Exclude, $null, .012, [Windows.Media.ToleranceType]::Absolute)
    $inner = $disk.Clone()
    $inner.RadiusX *= 0.91
    $inner.RadiusY *= 0.91
    $waves = [Windows.Media.Geometry]::Combine($waves, $inner, [Windows.Media.GeometryCombineMode]::Intersect, $null, .012, [Windows.Media.ToleranceType]::Absolute)
    $brushes = [Windows.Media.BrushConverter]::new()
    $dark = $brushes.ConvertFromInvariantString('#111417')
    $border = [Windows.Media.Pen]::new($brushes.ConvertFromInvariantString('#34383E'), $bounds.Width * .035)
    $group = [Windows.Media.DrawingGroup]::new()
    $group.Children.Add([Windows.Media.GeometryDrawing]::new($dark, $border, $disk))
    $group.Children.Add([Windows.Media.GeometryDrawing]::new([Windows.Media.Brushes]::White, $null, $waves))
    return $group
}

function Stack-Marks($geometry, $green, [double]$width) {
    $rear = Place-Mark $geometry ($width * 0.005) 0.72
    $front = Place-Mark $geometry ($width * 0.265) 0.72
    $bounds = $front.Bounds
    $center = [Windows.Point]::new($bounds.X + $bounds.Width / 2, $bounds.Y + $bounds.Height / 2)
    $radius = $bounds.Width / 2 + $width * 0.036
    $cut = [Windows.Media.EllipseGeometry]::new($center, $radius, $radius)
    $rear = [Windows.Media.Geometry]::Combine($rear, $cut, [Windows.Media.GeometryCombineMode]::Exclude, $null, .012, [Windows.Media.ToleranceType]::Absolute)
    $artwork = [Windows.Media.DrawingGroup]::new()
    $artwork.Children.Add([Windows.Media.GeometryDrawing]::new($green, $null, $rear))
    $artwork.Children.Add((Draw-DarkMark $front))
    return $artwork
}

$marks = @(
    @{ Name='spotify'; Source='spotify' },
    @{ Name='dual-spotify'; Source='spotify'; Icon='dual-spotify-green.ico'; Stacked=$true },
    @{ Name='second-spotify'; Source='second-spotify'; Ink='#ffffff'; Dark=$true; Icon='second-spotify-circle.ico' }
)
foreach ($mark in $marks) {
    [xml]$svg = Get-Content -LiteralPath (Join-Path $assets "$($mark.Source).svg") -Raw
    $path = $svg.DocumentElement.ChildNodes | Where-Object LocalName -EQ 'path' | Select-Object -First 1
    $viewBox = $svg.DocumentElement.GetAttribute('viewBox').Split(' ')
    $matrixText = $path.GetAttribute('transform') -replace '^matrix\(|\)$',''
    $matrix = $matrixText.Split(' ') | ForEach-Object { [double]::Parse($_, $culture) }
    $geometry = [Windows.Media.Geometry]::Parse('F1 ' + $path.GetAttribute('d')).Clone()
    $geometry.Transform = [Windows.Media.MatrixTransform]::new($matrix[0],$matrix[1],$matrix[2],$matrix[3],$matrix[4],$matrix[5])
    $width = [double]::Parse($viewBox[2], $culture)
    $bounds = $geometry.Bounds
    $factor = $width * 0.96 / [Math]::Max($bounds.Width, $bounds.Height)
    $fit = [Windows.Media.TransformGroup]::new()
    $fit.Children.Add($geometry.Transform)
    $fit.Children.Add([Windows.Media.ScaleTransform]::new($factor, $factor))
    $fit.Children.Add([Windows.Media.TranslateTransform]::new(
        ($width - $bounds.Width * $factor) / 2 - $bounds.X * $factor,
        ($width - $bounds.Height * $factor) / 2 - $bounds.Y * $factor))
    $geometry.Transform = $fit
    $ink = if ($mark.Ink) { $mark.Ink } else { [regex]::Match($path.GetAttribute('style'), '(?:^|;)fill:(#[0-9a-fA-F]{6})').Groups[1].Value }
    $brush = [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($ink)
    $artwork = if ($mark.Stacked) { Stack-Marks $geometry $brush $width }
        elseif ($mark.Dark) { Draw-DarkMark $geometry }
        else { [Windows.Media.GeometryDrawing]::new($brush, $null, $geometry) }
    [IO.File]::WriteAllBytes((Join-Path $assets "$($mark.Name).png"), (Render-Mark $artwork $width 256))
    if ($mark.Icon) { Save-Icon (Join-Path $assets $mark.Icon) $artwork $width }
    if ($HighResolution) {
        $masters = Join-Path $assets 'HiRes'
        $null = [IO.Directory]::CreateDirectory($masters)
        [IO.File]::WriteAllBytes((Join-Path $masters "$($mark.Name)-4096.png"), (Render-Mark $artwork $width 4096))
    }
}
