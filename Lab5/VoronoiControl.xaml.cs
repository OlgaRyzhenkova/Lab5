using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Lab5_OOP;

public partial class VoronoiControl : UserControl
{
    private const int RenderWidth = 760;
    private const int RenderHeight = 500;
    private const int TileSize = 80;

    private readonly ObservableCollection<VoronoiPoint> points = [];
    private readonly Random random = new();
    private long skippedParts;
    private string currentMetric = "Euclidean";
    private bool currentUsePruning = true;

    public VoronoiControl()
    {
        InitializeComponent();

        DataContext = new
        {
            Points = points
        };

        foreach (int count in new[] { 10, 25, 50, 100, 200, 400 })
        {
            PointCountComboBox.Items.Add(count);
        }

        foreach (int percent in new[] { 5, 10, 15, 20, 25, 30 })
        {
            RemovePercentComboBox.Items.Add(percent);
        }

        PointCountComboBox.SelectedItem = 50;
        RemovePercentComboBox.SelectedItem = 10;
        ThreadModeComboBox.SelectedIndex = 1;
        MetricComboBox.SelectedIndex = 0;

        GeneratePoints();
        RenderVoronoi();
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        GeneratePoints();
        RenderVoronoi();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        points.Clear();
        VoronoiImage.Source = null;
        VoronoiCanvas.Children.Clear();
        VoronoiStatusTextBlock.Text = "очищено";
        WallTimeTextBlock.Text = "Час: -";
        CpuTimeTextBlock.Text = "CPU: -";
        MemoryTextBlock.Text = "Пам'ять: -";
        SkippedTextBlock.Text = "Відкинуто: -";
    }

    private void SettingsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || points.Count == 0)
        {
            return;
        }

        RenderVoronoi();
    }

    private void SettingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || points.Count == 0)
        {
            return;
        }

        RenderVoronoi();
    }

    private void RemoveSmallestButton_Click(object sender, RoutedEventArgs e)
    {
        if (points.Count == 0)
        {
            return;
        }

        int percent = RemovePercentComboBox.SelectedItem is int selectedPercent ? selectedPercent : 10;
        int removeCount = Math.Max(1, (int)Math.Ceiling(points.Count * percent / 100.0));

        foreach (VoronoiPoint point in points.OrderBy(point => point.PixelCount).Take(removeCount).ToList())
        {
            points.Remove(point);
        }

        RenderVoronoi();
    }

    private void VoronoiCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point renderPoint = ToRenderPoint(e.GetPosition(VoronoiCanvas));
        points.Add(CreatePoint(renderPoint.X, renderPoint.Y, points.Count + 1));
        RenderVoronoi();
    }

    private void VoronoiCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (points.Count == 0)
        {
            return;
        }

        Point renderPoint = ToRenderPoint(e.GetPosition(VoronoiCanvas));
        VoronoiPoint nearest = points.MinBy(point => Distance(renderPoint.X, renderPoint.Y, point))!;
        points.Remove(nearest);
        RenamePoints();
        RenderVoronoi();
    }

    private void VoronoiCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawPointMarkers();
    }

    private void GeneratePoints()
    {
        int count = PointCountComboBox.SelectedItem is int selectedCount ? selectedCount : 50;
        points.Clear();

        for (int i = 0; i < count; i++)
        {
            double x = random.Next(12, RenderWidth - 12);
            double y = random.Next(12, RenderHeight - 12);
            points.Add(CreatePoint(x, y, i + 1));
        }
    }

    private VoronoiPoint CreatePoint(double x, double y, int index)
    {
        Color color = Color.FromRgb(
            (byte)random.Next(35, 235),
            (byte)random.Next(45, 235),
            (byte)random.Next(55, 235));

        double velocityX = random.NextDouble() * 2 + 1;
        double velocityY = random.NextDouble() * 2 + 1;
        return new VoronoiPoint($"Точка {index}", color, x, y, velocityX, velocityY);
    }

    private void RenamePoints()
    {
        int index = 1;
        foreach (VoronoiPoint point in points)
        {
            point.Rename($"Точка {index++}");
        }
    }

    private void RenderVoronoi()
    {
        if (points.Count == 0)
        {
            return;
        }

        VoronoiStatusTextBlock.Text = "обчислення...";
        VoronoiPoint[] snapshot = points.ToArray();
        int stride = RenderWidth * 4;
        byte[] pixels = new byte[stride * RenderHeight];
        int[] pixelCounts = new int[snapshot.Length];
        skippedParts = 0;
        currentMetric = GetMetric();
        currentUsePruning = UsePruningCheckBox.IsChecked == true;

        GC.Collect();
        long memoryBefore = GC.GetTotalMemory(true);
        TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (GetThreadMode() == "Single")
        {
            RenderSingleThread(snapshot, pixels, pixelCounts);
        }
        else
        {
            RenderParallel(snapshot, pixels, pixelCounts);
        }

        stopwatch.Stop();
        TimeSpan cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
        long memoryAfter = GC.GetTotalMemory(false);

        WriteableBitmap bitmap = new(RenderWidth, RenderHeight, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, RenderWidth, RenderHeight), pixels, stride, 0);
        bitmap.Freeze();
        VoronoiImage.Source = bitmap;

        for (int i = 0; i < snapshot.Length; i++)
        {
            snapshot[i].UpdatePixelCount(pixelCounts[i]);
        }

        WallTimeTextBlock.Text = $"Час: {stopwatch.ElapsedMilliseconds} мс";
        CpuTimeTextBlock.Text = $"CPU: {(cpuAfter - cpuBefore).TotalMilliseconds:F0} мс";
        MemoryTextBlock.Text = $"Пам'ять: {Math.Max(0, memoryAfter - memoryBefore) / 1024.0:F1} КБ";
        SkippedTextBlock.Text = $"Відкинуто частин порівнянь: {skippedParts:N0}";
        VoronoiStatusTextBlock.Text = $"{GetModeLabel()}, {GetMetricLabel()}";
        DrawPointMarkers();
    }

    private void RenderSingleThread(VoronoiPoint[] snapshot, byte[] pixels, int[] pixelCounts)
    {
        int stride = RenderWidth * 4;
        for (int y = 0; y < RenderHeight; y++)
        {
            for (int x = 0; x < RenderWidth; x++)
            {
                int pointIndex = FindNearestPointIndex(x, y, snapshot);
                WritePixel(pixels, stride, x, y, snapshot[pointIndex].Color);
                pixelCounts[pointIndex]++;
            }
        }
    }

    private void RenderParallel(VoronoiPoint[] snapshot, byte[] pixels, int[] pixelCounts)
    {
        int tileColumns = (int)Math.Ceiling(RenderWidth / (double)TileSize);
        int tileRows = (int)Math.Ceiling(RenderHeight / (double)TileSize);
        object countsLock = new();

        Parallel.For(0, tileColumns * tileRows, tileIndex =>
        {
            int tileX = tileIndex % tileColumns;
            int tileY = tileIndex / tileColumns;
            int startX = tileX * TileSize;
            int startY = tileY * TileSize;
            int endX = Math.Min(RenderWidth, startX + TileSize);
            int endY = Math.Min(RenderHeight, startY + TileSize);
            int[] localCounts = new int[snapshot.Length];
            int stride = RenderWidth * 4;

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    int pointIndex = FindNearestPointIndex(x, y, snapshot);
                    WritePixel(pixels, stride, x, y, snapshot[pointIndex].Color);
                    localCounts[pointIndex]++;
                }
            }

            lock (countsLock)
            {
                for (int i = 0; i < pixelCounts.Length; i++)
                {
                    pixelCounts[i] += localCounts[i];
                }
            }
        });
    }

    private int FindNearestPointIndex(int x, int y, VoronoiPoint[] snapshot)
    {
        int nearestIndex = 0;
        double bestDistance = Distance(x, y, snapshot[0]);

        for (int i = 1; i < snapshot.Length; i++)
        {
            VoronoiPoint point = snapshot[i];
            double distance;

            if (currentUsePruning && currentMetric == "Euclidean")
            {
                double dx = x - point.X;
                double dxSquared = dx * dx;
                if (dxSquared >= bestDistance)
                {
                    Interlocked.Increment(ref skippedParts);
                    continue;
                }

                double dy = y - point.Y;
                distance = dxSquared + dy * dy;
            }
            else if (currentUsePruning && currentMetric == "Manhattan")
            {
                double dx = Math.Abs(x - point.X);
                if (dx >= bestDistance)
                {
                    Interlocked.Increment(ref skippedParts);
                    continue;
                }

                distance = dx + Math.Abs(y - point.Y);
            }
            else
            {
                distance = Distance(x, y, point);
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    private double Distance(double x, double y, VoronoiPoint point)
    {
        double dx = Math.Abs(x - point.X);
        double dy = Math.Abs(y - point.Y);

        return currentMetric switch
        {
            "Manhattan" => dx + dy,
            "Chebyshev" => Math.Max(dx, dy),
            _ => dx * dx + dy * dy
        };
    }

    private static void WritePixel(byte[] pixels, int stride, int x, int y, Color color)
    {
        int offset = y * stride + x * 4;
        pixels[offset] = color.B;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.R;
        pixels[offset + 3] = 255;
    }

    private Point ToRenderPoint(Point canvasPoint)
    {
        if (VoronoiCanvas.ActualWidth <= 0 || VoronoiCanvas.ActualHeight <= 0)
        {
            return new Point(0, 0);
        }

        double x = Math.Clamp(canvasPoint.X / VoronoiCanvas.ActualWidth * RenderWidth, 0, RenderWidth - 1);
        double y = Math.Clamp(canvasPoint.Y / VoronoiCanvas.ActualHeight * RenderHeight, 0, RenderHeight - 1);
        return new Point(x, y);
    }

    private void DrawPointMarkers()
    {
        if (VoronoiCanvas.ActualWidth <= 0 || VoronoiCanvas.ActualHeight <= 0)
        {
            return;
        }

        VoronoiCanvas.Children.Clear();
        double scaleX = VoronoiCanvas.ActualWidth / RenderWidth;
        double scaleY = VoronoiCanvas.ActualHeight / RenderHeight;

        foreach (VoronoiPoint point in points)
        {
            double left = point.X * scaleX;
            double top = point.Y * scaleY;

            Ellipse outer = new()
            {
                Width = 18,
                Height = 18,
                Stroke = Brushes.White,
                StrokeThickness = 3,
                Fill = Brushes.Black
            };

            Ellipse inner = new()
            {
                Width = 7,
                Height = 7,
                Fill = Brushes.White
            };

            TextBlock label = new()
            {
                Text = point.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            };

            Border labelBack = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(185, 20, 27, 43)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 3, 7, 4),
                Child = label
            };

            VoronoiCanvas.Children.Add(outer);
            VoronoiCanvas.Children.Add(inner);
            VoronoiCanvas.Children.Add(labelBack);

            Canvas.SetLeft(outer, left - 9);
            Canvas.SetTop(outer, top - 9);
            Canvas.SetLeft(inner, left - 3.5);
            Canvas.SetTop(inner, top - 3.5);
            Canvas.SetLeft(labelBack, Math.Min(VoronoiCanvas.ActualWidth - 82, left + 12));
            Canvas.SetTop(labelBack, Math.Max(6, top - 15));
        }
    }

    private string GetThreadMode()
    {
        return (ThreadModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Parallel";
    }

    private string GetMetric()
    {
        return (MetricComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Euclidean";
    }

    private string GetModeLabel()
    {
        return GetThreadMode() == "Single" ? "один потік" : "паралельно";
    }

    private string GetMetricLabel()
    {
        return GetMetric() switch
        {
            "Manhattan" => "манхетенська",
            "Chebyshev" => "Чебишова",
            _ => "евклідова"
        };
    }
}
