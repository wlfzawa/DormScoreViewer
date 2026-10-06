namespace DormScoreViewer.Models;

/// <summary>
/// 按寝室汇总的结果。
/// </summary>
public class RoomAggregate
{
    public string ClassName { get; set; } = "";
    public string Room { get; set; } = "";
    public double TotalScore { get; set; }
    public int Count { get; set; }
    public int DayCount { get; set; }
    public bool IsTracked { get; set; }
}

/// <summary>
/// 按班级汇总的结果。
/// </summary>
public class ClassAggregate
{
    public string ClassName { get; set; } = "";
    public double TotalScore { get; set; }
    public int Count { get; set; }
}
