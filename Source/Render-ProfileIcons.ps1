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
    $sizes = @(16,24,32,48,64,128)
    $frames = foreach ($size in $sizes) { ,(Render-Mark $artwork $width $size) }
    $writer = [IO.BinaryWriter]::new([IO.File]::Create($path))
    try {
        $writer.Write([byte[]](0,0,1,0,$sizes.Count,0))
        $offset = 6 + $sizes.Count * 16
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $size = $sizes[$i]
            $length = $frames[$i].Length
            $writer.Write([byte[]]($size,$size,0,0,1,0,32,0))
            $writer.Write([int]$length)
            $writer.Write([int]$offset)
            $offset += $length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose() }
}

function Place-Mark($geometry, [double]$offset) {
    $placed = $geometry.Clone()
    $transform = [Windows.Media.TransformGroup]::new()
    $transform.Children.Add($geometry.Transform)
    $transform.Children.Add([Windows.Media.ScaleTransform]::new(0.68, 0.68))
    $transform.Children.Add([Windows.Media.TranslateTransform]::new($offset, $offset))
    $placed.Transform = $transform
    return $placed
}

function Stack-Marks($geometry, $green, [double]$width) {
    $rear = Place-Mark $geometry ($width * 0.03)
    $front = Place-Mark $geometry ($width * 0.29)
    $bounds = $front.Bounds
    $center = [Windows.Point]::new($bounds.X + $bounds.Width / 2, $bounds.Y + $bounds.Height / 2)
    $radius = $bounds.Width / 2 + $width * 0.033
    $cut = [Windows.Media.EllipseGeometry]::new($center, $radius, $radius)
    $rear = [Windows.Media.Geometry]::Combine($rear, $cut, [Windows.Media.GeometryCombineMode]::Exclude, $null)
    $artwork = [Windows.Media.DrawingGroup]::new()
    $artwork.Children.Add([Windows.Media.GeometryDrawing]::new($green, $null, $rear))
    $artwork.Children.Add([Windows.Media.GeometryDrawing]::new([Windows.Media.Brushes]::Gainsboro, $null, $front))
    return $artwork
}

$marks = @(
    @{ Name='spotify'; Source='spotify' },
    @{ Name='dual-spotify'; Source='spotify'; Icon='dual-spotify-green.ico'; Stacked=$true },
    @{ Name='second-spotify'; Source='second-spotify'; Ink='#dcdcdc'; Icon='second-spotify-circle.ico' }
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
    $ink = if ($mark.Ink) { $mark.Ink } else { [regex]::Match($path.GetAttribute('style'), '(?:^|;)fill:(#[0-9a-fA-F]{6})').Groups[1].Value }
    $brush = [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($ink)
    $artwork = if ($mark.Stacked) { Stack-Marks $geometry $brush $width }
        else { [Windows.Media.GeometryDrawing]::new($brush, $null, $geometry) }
    [IO.File]::WriteAllBytes((Join-Path $assets "$($mark.Name).png"), (Render-Mark $artwork $width 128))
    if ($mark.Icon) { Save-Icon (Join-Path $assets $mark.Icon) $artwork $width }
}
