namespace DormScoreViewer.Models;

/// <summary>
/// 主界面组件的设置（每个摆放的组件实例各自独立，由 ClassIsland 自动保存与加载）。
/// </summary>
public class ComponentSettings
{
    private int _recentDays = 1;
    private int _maxRows = 5;

    /// <summary>汇总最近 N 天的扣分数据（1-60）。</summary>
    public int RecentDays
    {
        get => _recentDays < 1 ? 1 : (_recentDays > 60 ? 60 : _recentDays);
        set => _recentDays = value < 1 ? 1 : (value > 60 ? 60 : value);
    }

    /// <summary>只显示关注的寝室；关闭则显示全校排名前列的寝室。</summary>
    public bool OnlyTrackedRooms { get; set; } = true;

    /// <summary>最多显示的行数（1-50）。</summary>
    public int MaxRows
    {
        get => _maxRows < 1 ? 1 : (_maxRows > 50 ? 50 : _maxRows);
        set => _maxRows = value < 1 ? 1 : (value > 50 ? 50 : value);
    }

    /// <summary>是否显示班级列。</summary>
    public bool ShowClassName { get; set; } = true;

    /// <summary>是否显示扣分原因。</summary>
    public bool ShowReason { get; set; } = true;
}
