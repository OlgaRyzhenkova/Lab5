using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace Lab5_OOP;

public partial class MainWindow : Window
{
    private const int MinHorseCount = 5;
    private const int MaxHorseCount = 10;
    private const double HorseWidth = 96;
    private const double HorseHeight = 64;
    private const double LeftPadding = 54;
    private const double RightPadding = 82;
    private const double TrackTopPadding = 78;
    private const double TrackBottomPadding = 26;
    private readonly ObservableCollection<Horse> horses = [];
    private readonly ObservableCollection<RaceResult> results = [];
    private readonly ICollectionView horsesView;
    private readonly Dictionary<Horse, Image> horseImages = [];
    private readonly Dictionary<Horse, Border> horseBadges = [];
    private readonly Dictionary<Horse, List<BitmapSource>> horseFrames = [];
    private readonly List<Line> laneLines = [];
    private readonly DispatcherTimer renderTimer;
    private readonly Stopwatch stopwatch = new();
    private readonly Random random = new();
    private readonly object randomLock = new();
    private readonly Dictionary<int, double> savedCoefficients = [];
    private readonly List<BitmapSource> frames = [];
    private readonly List<BitmapSource> masks = [];
    private bool isRenderingFrame;
    private double balance = 250;
    private int selectedBetAmount = 10;
    private int activeBetAmount;
    private Horse? activeBetHorse;
    private int frameIndex;
    private int cameraHorseIndex = -1;
    private bool isLeaderCameraEnabled;
    private double raceFinishX;

    public MainWindow()
    {
        InitializeComponent();

        horsesView = CollectionViewSource.GetDefaultView(horses);
        horsesView.SortDescriptions.Add(new SortDescription(nameof(Horse.X), ListSortDirection.Descending));

        if (horsesView is ICollectionViewLiveShaping liveView && liveView.CanChangeLiveSorting)
        {
            liveView.LiveSortingProperties.Add(nameof(Horse.X));
            liveView.IsLiveSorting = true;
        }

        DataContext = new
        {
            Horses = horses,
            HorsesView = horsesView,
            Results = results
        };

        for (int count = MinHorseCount; count <= MaxHorseCount; count++)
        {
            HorseCountComboBox.Items.Add(count);
        }

        HorseCountComboBox.SelectedItem = 6;

        renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        renderTimer.Tick += RenderTimer_Tick;

        LoadImages();
        CreateRace();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (renderTimer.IsEnabled)
        {
            return;
        }

        int betAmount = selectedBetAmount;
        if (betAmount > balance)
        {
            MessageBox.Show("Недостатньо коштів для цієї ставки.", "Ставка", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CreateRace();
        raceFinishX = CalculateFinishX();
        PositionFixedTrackElements();
        activeBetAmount = betAmount;
        activeBetHorse = horses.FirstOrDefault(horse => horse.Name == (BetHorseComboBox.SelectedItem as string));
        balance -= activeBetAmount;
        UpdateBalance();

        StartButton.IsEnabled = false;
        HorseCountComboBox.IsEnabled = false;
        BetPanel.IsEnabled = false;
        StatusTextBlock.Text = "перегони тривають";

        stopwatch.Restart();
        renderTimer.Start();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        StopRace("готово до старту");
        CreateRace();
    }

    private void NextCameraButton_Click(object sender, RoutedEventArgs e)
    {
        isLeaderCameraEnabled = !isLeaderCameraEnabled;

        if (isLeaderCameraEnabled)
        {
            UpdateLeaderCamera();
            NextCameraButton.Content = "Вимкнути камеру";
        }
        else
        {
            cameraHorseIndex = -1;
            CameraFrame.Visibility = Visibility.Collapsed;
            NextCameraButton.Content = "Камера лідера";
        }

        RenderHorses();
    }

    private void BetAmountButton_Click(object sender, RoutedEventArgs e)
    {
        if (renderTimer.IsEnabled)
        {
            return;
        }

        if (sender is not Button button || button.Tag is null || !int.TryParse(button.Tag.ToString(), out int amount))
        {
            return;
        }

        selectedBetAmount = amount;
        SelectedBetTextBlock.Text = $"Обрано: {selectedBetAmount}$";
    }

    private void HorseCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || renderTimer is null || renderTimer.IsEnabled)
        {
            return;
        }

        savedCoefficients.Clear();
        CreateRace();
    }

    private void RaceCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!renderTimer.IsEnabled)
        {
            raceFinishX = CalculateFinishX();
        }

        PositionFixedTrackElements();
        RenderHorses();
    }

    private async void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (isRenderingFrame || horses.Count == 0)
        {
            return;
        }

        isRenderingFrame = true;

        try
        {
            double finishX = GetFinishX();
            TimeSpan elapsed = stopwatch.Elapsed;

            Task<RaceMove>[] tasks = horses
                .Select(horse => Task.Run(() => horse.CalculateMove(finishX, elapsed)))
                .ToArray();

            RaceMove[] moves = await Task.WhenAll(tasks);

            foreach (RaceMove move in moves)
            {
                bool wasFinished = move.Horse.IsFinished;
                move.Horse.ApplyMove(move);

                if (!wasFinished && move.Horse.IsFinished)
                {
                    results.Add(new RaceResult(results.Count + 1, move.Horse.Name, move.Horse.RunTime));
                }
            }

            horsesView.Refresh();
            UpdateLeaderCamera();
            frameIndex = (frameIndex + 1) % frames.Count;
            PositionFixedTrackElements();
            RenderHorses();

            if (horses.All(horse => horse.IsFinished))
            {
                StopRace("фініш");
            }
        }
        finally
        {
            isRenderingFrame = false;
        }
    }

    private void CreateRace()
    {
        int horseCount = HorseCountComboBox.SelectedItem is int selectedCount ? selectedCount : 6;

        horses.Clear();
        results.Clear();
        horseImages.Clear();
        horseBadges.Clear();
        horseFrames.Clear();
        laneLines.Clear();
        RaceCanvas.Children.Clear();

        RaceCanvas.Children.Add(StartLine);
        RaceCanvas.Children.Add(FinishLine);
        RaceCanvas.Children.Add(StartLabel);
        RaceCanvas.Children.Add(FinishLabel);
        RaceCanvas.Children.Add(CameraFrame);
        raceFinishX = CalculateFinishX();
        cameraHorseIndex = -1;
        isLeaderCameraEnabled = false;
        CameraFrame.Visibility = Visibility.Collapsed;
        NextCameraButton.Content = "Камера лідера";

        Color[] colors =
        [
            Color.FromRgb(210, 56, 73),
            Color.FromRgb(41, 128, 185),
            Color.FromRgb(46, 139, 87),
            Color.FromRgb(146, 85, 184),
            Color.FromRgb(231, 126, 35),
            Color.FromRgb(22, 160, 133),
            Color.FromRgb(125, 95, 70),
            Color.FromRgb(52, 73, 94),
            Color.FromRgb(190, 80, 130),
            Color.FromRgb(88, 103, 221)
        ];

        for (int i = 0; i < horseCount; i++)
        {
            double coefficient = savedCoefficients.TryGetValue(i, out double savedCoefficient)
                ? savedCoefficient
                : 1.15 + random.NextDouble() * 1.35;
            double baseSpeed = 2.6 + random.NextDouble() * 1.25;
            Horse horse = new($"Кінь {i + 1}", colors[i], i, baseSpeed, coefficient, random);
            horse.Reset();
            horses.Add(horse);

            Line laneLine = new()
            {
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Opacity = 0.58,
                StrokeDashArray = [8, 8]
            };

            laneLines.Add(laneLine);
            RaceCanvas.Children.Add(laneLine);

            List<BitmapSource> coloredFrames = CreateColoredFrames(horse.Color);
            horseFrames.Add(horse, coloredFrames);

            Image image = new()
            {
                Width = HorseWidth,
                Height = HorseHeight,
                Stretch = Stretch.Uniform,
                Source = coloredFrames[0],
                Effect = new DropShadowEffect
                {
                    BlurRadius = 8,
                    Direction = 270,
                    ShadowDepth = 3,
                    Opacity = 0.32
                }
            };

            Border badge = CreateHorseBadge(horse);
            horseImages.Add(horse, image);
            horseBadges.Add(horse, badge);
            RaceCanvas.Children.Add(image);
            RaceCanvas.Children.Add(badge);
        }

        UpdateBetHorseList();
        PositionFixedTrackElements();
        RenderHorses();
    }

    private void UpdateBetHorseList()
    {
        string? selected = BetHorseComboBox.SelectedItem as string;
        BetHorseComboBox.Items.Clear();

        foreach (Horse horse in horses)
        {
            BetHorseComboBox.Items.Add(horse.Name);
        }

        BetHorseComboBox.SelectedItem = horses.Any(horse => horse.Name == selected)
            ? selected
            : horses.FirstOrDefault()?.Name;
    }

    private void LoadImages()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string trackPath = IOPath.Combine(baseDirectory, "Images", "Background", "Track.png");

        if (!File.Exists(trackPath))
        {
            trackPath = IOPath.Combine(Environment.CurrentDirectory, "Images", "Background", "Track.png");
        }

        TrackImage.Source = LoadBitmap(trackPath);

        string framesDirectory = IOPath.Combine(baseDirectory, "Images", "Horses");
        if (!Directory.Exists(framesDirectory))
        {
            framesDirectory = IOPath.Combine(Environment.CurrentDirectory, "Images", "Horses");
        }

        foreach (string framePath in Directory.EnumerateFiles(framesDirectory, "WithOutBorder_*.png").OrderBy(path => path))
        {
            frames.Add(LoadBitmap(framePath));
        }

        if (frames.Count == 0)
        {
            throw new InvalidOperationException("Не знайдено зображення коней у папці Images\\Horses.");
        }

        string masksDirectory = IOPath.Combine(baseDirectory, "Images", "HorsesMask");
        if (!Directory.Exists(masksDirectory))
        {
            masksDirectory = IOPath.Combine(Environment.CurrentDirectory, "Images", "HorsesMask");
        }

        foreach (string maskPath in Directory.EnumerateFiles(masksDirectory, "mask_*.png").OrderBy(path => path))
        {
            masks.Add(LoadBitmap(maskPath));
        }

        if (masks.Count != frames.Count)
        {
            throw new InvalidOperationException("Кількість кадрів коня і масок вершника не збігається.");
        }
    }

    private static BitmapImage LoadBitmap(string path)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private List<BitmapSource> CreateColoredFrames(Color riderColor)
    {
        List<BitmapSource> coloredFrames = [];

        for (int i = 0; i < frames.Count; i++)
        {
            coloredFrames.Add(ColorizeRider(frames[i], masks[i], riderColor));
        }

        return coloredFrames;
    }

    private static BitmapSource ColorizeRider(BitmapSource frame, BitmapSource mask, Color riderColor)
    {
        BitmapSource normalizedFrame = EnsureBgra32(frame);
        BitmapSource normalizedMask = EnsureBgra32(mask);
        int width = normalizedFrame.PixelWidth;
        int height = normalizedFrame.PixelHeight;
        int stride = width * 4;
        byte[] framePixels = new byte[stride * height];
        byte[] maskPixels = new byte[stride * height];

        normalizedFrame.CopyPixels(framePixels, stride, 0);
        normalizedMask.CopyPixels(maskPixels, stride, 0);

        for (int pixel = 0; pixel < framePixels.Length; pixel += 4)
        {
            byte maskAlpha = maskPixels[pixel + 3];
            if (maskAlpha < 16)
            {
                continue;
            }

            byte blue = framePixels[pixel];
            byte green = framePixels[pixel + 1];
            byte red = framePixels[pixel + 2];
            double shade = Math.Clamp((red * 0.299 + green * 0.587 + blue * 0.114) / 255.0, 0.18, 1.0);
            double strength = maskAlpha / 255.0;

            byte targetBlue = (byte)Math.Clamp(riderColor.B * (0.42 + shade * 0.72), 0, 255);
            byte targetGreen = (byte)Math.Clamp(riderColor.G * (0.42 + shade * 0.72), 0, 255);
            byte targetRed = (byte)Math.Clamp(riderColor.R * (0.42 + shade * 0.72), 0, 255);

            framePixels[pixel] = Blend(blue, targetBlue, strength);
            framePixels[pixel + 1] = Blend(green, targetGreen, strength);
            framePixels[pixel + 2] = Blend(red, targetRed, strength);
        }

        BitmapSource result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, framePixels, stride);
        result.Freeze();
        return result;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
        {
            return source;
        }

        FormatConvertedBitmap converted = new(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte Blend(byte original, byte target, double strength)
    {
        return (byte)Math.Clamp(original * (1 - strength) + target * strength, 0, 255);
    }

    private static Border CreateHorseBadge(Horse horse)
    {
        TextBlock nameText = new()
        {
            Text = horse.Name,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        };

        StackPanel content = new() { Orientation = Orientation.Horizontal };
        content.Children.Add(nameText);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 17, 30, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 4, 9, 5),
            Child = content
        };
    }

    private void RenderHorses()
    {
        if (RaceCanvas.ActualWidth <= 0 || RaceCanvas.ActualHeight <= 0)
        {
            return;
        }

        double raceTop = TrackTopPadding;
        double raceHeight = Math.Max(120, RaceCanvas.ActualHeight - TrackTopPadding - TrackBottomPadding);
        double laneHeight = raceHeight / Math.Max(horses.Count, 1);

        for (int i = 0; i < laneLines.Count; i++)
        {
            double y = raceTop + laneHeight * (i + 1);
            laneLines[i].X1 = LeftPadding;
            laneLines[i].X2 = RaceCanvas.ActualWidth - RightPadding + 42;
            laneLines[i].Y1 = y;
            laneLines[i].Y2 = y;
        }

        foreach (Horse horse in horses)
        {
            if (!horseImages.TryGetValue(horse, out Image? image))
            {
                continue;
            }

            double horseLeft = LeftPadding + horse.X;
            double horseTop = raceTop + laneHeight * horse.Lane + Math.Max(3, (laneHeight - HorseHeight) / 2);

            image.Source = horseFrames[horse][frameIndex];
            Canvas.SetLeft(image, horseLeft);
            Canvas.SetTop(image, horseTop);

            if (horseBadges.TryGetValue(horse, out Border? badge))
            {
                Canvas.SetLeft(badge, Math.Max(12, horseLeft - 54));
                Canvas.SetTop(badge, horseTop + (HorseHeight - 30) / 2);
            }
        }

        PositionCameraFrame(raceTop, laneHeight);
    }

    private void PositionFixedTrackElements()
    {
        if (RaceCanvas.ActualWidth <= 0 || RaceCanvas.ActualHeight <= 0)
        {
            return;
        }

        StartLine.Height = RaceCanvas.ActualHeight;
        FinishLine.Height = RaceCanvas.ActualHeight;

        Canvas.SetLeft(StartLine, LeftPadding - 14);
        Canvas.SetTop(StartLine, 0);
        Canvas.SetLeft(FinishLine, LeftPadding + GetFinishX() + HorseWidth + 4);
        Canvas.SetTop(FinishLine, 0);

        Canvas.SetLeft(StartLabel, LeftPadding - 34);
        Canvas.SetTop(StartLabel, 12);
        Canvas.SetLeft(FinishLabel, LeftPadding + GetFinishX() + HorseWidth - 14);
        Canvas.SetTop(FinishLabel, 12);
    }

    private void UpdateLeaderCamera()
    {
        if (!isLeaderCameraEnabled || horses.Count == 0)
        {
            return;
        }

        Horse leader = horses
            .OrderByDescending(horse => horse.X)
            .ThenBy(horse => horse.RunTime == TimeSpan.Zero ? TimeSpan.MaxValue : horse.RunTime)
            .First();
        cameraHorseIndex = horses.IndexOf(leader);
    }

    private void PositionCameraFrame(double raceTop, double laneHeight)
    {
        if (!isLeaderCameraEnabled || cameraHorseIndex < 0 || cameraHorseIndex >= horses.Count)
        {
            CameraFrame.Visibility = Visibility.Collapsed;
            return;
        }

        Horse focusedHorse = horses[cameraHorseIndex];
        double horseLeft = LeftPadding + focusedHorse.X;
        double horseTop = raceTop + laneHeight * focusedHorse.Lane + Math.Max(3, (laneHeight - HorseHeight) / 2);

        CameraFrame.Width = HorseWidth + 18;
        CameraFrame.Height = HorseHeight + 16;
        Canvas.SetLeft(CameraFrame, horseLeft - 9);
        Canvas.SetTop(CameraFrame, horseTop - 8);
        Canvas.SetZIndex(CameraFrame, 50);
        CameraFrame.Visibility = Visibility.Visible;
    }

    private double GetFinishX()
    {
        return raceFinishX > 0 ? raceFinishX : CalculateFinishX();
    }

    private double CalculateFinishX()
    {
        return Math.Max(120, RaceCanvas.ActualWidth - HorseWidth - LeftPadding - RightPadding);
    }

    private void StopRace(string status)
    {
        renderTimer.Stop();

        if (status == "фініш")
        {
            status = PayBet();
            UpdateCoefficientsByResults();
        }

        stopwatch.Stop();
        StartButton.IsEnabled = true;
        HorseCountComboBox.IsEnabled = true;
        BetPanel.IsEnabled = true;
        StatusTextBlock.Text = status;
    }

    private string PayBet()
    {
        RaceResult? winner = results.FirstOrDefault();
        if (winner is null || activeBetHorse is null || activeBetAmount <= 0)
        {
            return "фініш";
        }

        if (winner.HorseName == activeBetHorse.Name)
        {
            double prize = Math.Round(activeBetAmount * activeBetHorse.Coefficient);
            balance += prize;
            activeBetHorse.AddMoney(prize);
            activeBetAmount = 0;
            activeBetHorse = null;
            UpdateBalance();
            return $"виграш {prize:F0}$";
        }

        activeBetAmount = 0;
        activeBetHorse = null;
        UpdateBalance();
        return "ставка не зіграла";
    }

    private void UpdateCoefficientsByResults()
    {
        if (results.Count == 0)
        {
            return;
        }

        Dictionary<string, int> places = results.ToDictionary(result => result.HorseName, result => result.Place);
        int horseCount = horses.Count;

        foreach (Horse horse in horses)
        {
            int place = places.TryGetValue(horse.Name, out int resultPlace) ? resultPlace : horseCount;
            double multiplier = place switch
            {
                1 => 0.88,
                2 => 0.95,
                3 => 1.02,
                _ => 1.0 + place * 0.045
            };

            double updatedCoefficient = Math.Round(horse.Coefficient * multiplier, 2);
            horse.UpdateCoefficient(updatedCoefficient);
            savedCoefficients[horse.Lane] = horse.Coefficient;
        }
    }

    private void UpdateBalance()
    {
        BalanceTextBlock.Text = $"Баланс: {balance:F0}$";
    }
}
