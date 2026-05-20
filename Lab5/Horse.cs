using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Lab5_OOP;

public sealed class Horse : INotifyPropertyChanged
{
    private readonly Random random;
    private double x;
    private double acceleration = 1;
    private TimeSpan runTime;
    private double money;
    private double coefficient;

    public Horse(string name, Color color, int lane, double coefficient, Random random)
    {
        Name = name;
        Color = color;
        Lane = lane;
        this.coefficient = coefficient;
        Brush = new SolidColorBrush(color);
        Brush.Freeze();
        this.random = random;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; private set; }

    public Color Color { get; private set; }

    public SolidColorBrush Brush { get; private set; }

    public int Lane { get; private set; }

    public double Coefficient
    {
        get => coefficient;
        private set
        {
            coefficient = value;
            OnPropertyChanged();
        }
    }

    public double Money
    {
        get => money;
        private set
        {
            money = value;
            OnPropertyChanged();
        }
    }

    public double X
    {
        get => x;
        private set
        {
            if (Math.Abs(x - value) < 0.01)
            {
                return;
            }

            x = value;
            OnPropertyChanged();
        }
    }

    public double Acceleration
    {
        get => acceleration;
        private set
        {
            if (Math.Abs(acceleration - value) < 0.001)
            {
                return;
            }

            acceleration = value;
            OnPropertyChanged();
        }
    }

    public TimeSpan RunTime
    {
        get => runTime;
        private set
        {
            runTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RunTimeText));
        }
    }

    public bool IsFinished { get; private set; }

    public string RunTimeText => RunTime == TimeSpan.Zero ? "-" : RunTime.ToString(@"mm\:ss\.ff");

    public void Reset()
    {
        X = 0;
        Acceleration = 1;
        RunTime = TimeSpan.Zero;
        IsFinished = false;
        OnPropertyChanged(nameof(IsFinished));
    }

    public void AddMoney(double value)
    {
        Money += value;
    }

    public void UpdateCoefficient(double value)
    {
        Coefficient = Math.Clamp(value, 1.1, 4.0);
    }

    public void Render(double baseSpeed, double finishX, TimeSpan elapsed)
    {
        if (IsFinished)
        {
            return;
        }

        X = Math.Min(finishX, X + baseSpeed * Acceleration);

        if (X >= finishX)
        {
            IsFinished = true;
            RunTime = elapsed;
            OnPropertyChanged(nameof(IsFinished));
        }
    }

    public void ChangeAcceleration()
    {
        Acceleration = 0.7 + random.NextDouble() * 0.3;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
