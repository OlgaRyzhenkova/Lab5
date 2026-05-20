using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Lab5_OOP;

public sealed class VoronoiPoint : INotifyPropertyChanged
{
    private double x;
    private double y;
    private int pixelCount;
    private string name;

    public VoronoiPoint(string name, Color color, double x, double y, double velocityX, double velocityY)
    {
        this.name = name;
        Color = color;
        Brush = new SolidColorBrush(color);
        Brush.Freeze();
        X = x;
        Y = y;
        VelocityX = velocityX;
        VelocityY = velocityY;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        private set
        {
            name = value;
            OnPropertyChanged();
        }
    }

    public Color Color { get; }

    public SolidColorBrush Brush { get; }

    public double X
    {
        get => x;
        private set
        {
            x = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(XText));
        }
    }

    public double Y
    {
        get => y;
        private set
        {
            y = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(YText));
        }
    }

    public double VelocityX { get; private set; }

    public double VelocityY { get; private set; }

    public string XText => X.ToString("F0");

    public string YText => Y.ToString("F0");

    public int PixelCount
    {
        get => pixelCount;
        private set
        {
            pixelCount = value;
            OnPropertyChanged();
        }
    }

    public void UpdatePixelCount(int value)
    {
        PixelCount = value;
    }

    public void Rename(string value)
    {
        Name = value;
    }

    public void Move(double width, double height)
    {
        X += VelocityX;
        Y += VelocityY;

        if (X < 8 || X > width - 8)
        {
            VelocityX *= -1;
            X = Math.Clamp(X, 8, width - 8);
        }

        if (Y < 8 || Y > height - 8)
        {
            VelocityY *= -1;
            Y = Math.Clamp(Y, 8, height - 8);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
