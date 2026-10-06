namespace DormScoreViewer.Models;

/// <summary>
/// 一条扣分明细记录。
/// </summary>
public class DormRecord
{
    /// <summary>所属日期标签（公告标题）。</summary>
    public string DateLabel { get; set; } = "";

    /// <summary>班级名称。</summary>
    public string ClassName { get; set; } = "";

    /// <summary>寝室号。</summary>
    public string Room { get; set; } = "";

    /// <summary>扣分数值（一般为负数）。</summary>
    public double Score { get; set; }

    /// <summary>扣分原因。</summary>
    public string Reason { get; set; } = "";

    /// <summary>是否是用户关注的寝室（用于界面高亮，不参与序列化比较）。</summary>
    public bool IsTracked { get; set; }
}
