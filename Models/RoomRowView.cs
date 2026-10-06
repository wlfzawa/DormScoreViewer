namespace DormScoreViewer.Models;

/// <summary>
/// 主界面组件中一行扣分记录的可绑定视图模型。
/// </summary>
public class RoomRowView
{
    /// <summary>寝室号（关注的寝室会带 ★ 标记）。</summary>
    public string DisplayRoom { get; set; } = "";

    /// <summary>班级（关闭显示班级时为空）。</summary>
    public string ClassName { get; set; } = "";

    /// <summary>扣分原因（关闭显示原因时为空）。</summary>
    public string Reason { get; set; } = "";

    /// <summary>格式化后的扣分，如 "-1.5"。</summary>
    public string ScoreText { get; set; } = "";

    /// <summary>是否为关注的寝室。</summary>
    public bool IsTracked { get; set; }

    /// <summary>行透明度（关注寝室更醒目）。</summary>
    public double RowOpacity { get; set; } = 1.0;
}
