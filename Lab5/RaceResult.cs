namespace Lab5_OOP;

public sealed class RaceResult
{
    public RaceResult(int place, string horseName, TimeSpan finishTime)
    {
        Place = place;
        HorseName = horseName;
        FinishTime = finishTime;
    }

    public int Place { get; private set; }

    public string HorseName { get; private set; }

    public TimeSpan FinishTime { get; private set; }

    public string FinishTimeText => FinishTime.ToString(@"mm\:ss\.ff");
}
