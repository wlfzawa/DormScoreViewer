namespace DormScoreViewer.Models;

/// <summary>
/// 主界面组件的设置（每个摆放的组件实例各自独立，由 ClassIsland 自动保存与加载）。
/// </summary>
public class ComponentSettings
{
    private int _recentDays = 1;
    private int _rotateSeconds = 5;
    private double _fontSize = 0; // 0 = 自动

    /// <summary>汇总最近 N 天的扣分数据（1-60）。</summary>
    public int RecentDays
    {
        get => _recentDays < 1 ? 1 : (_recentDays > 60 ? 60 : _recentDays);
        set => _recentDays = value < 1 ? 1 : (value > 60 ? 60 : value);
    }

    /// <summary>只显示关注班级的寝室；关闭则显示全部。</summary>
    public bool OnlyTrackedClasses { get; set; } = true;

    /// <summary>
    /// 轮播间隔（秒）。逐条轮播时，每隔这么多秒切换到下一条。
    /// 设为 0 表示关闭轮播，一次列出全部。
    /// </summary>
    public int RotateSeconds
    {
        get => _rotateSeconds < 0 ? 0 : _rotateSeconds;
        set => _rotateSeconds = value < 0 ? 0 : value;
    }

    /// <summary>
    /// 字号（像素）。0 表示自动（随组件尺寸自适应）。
    /// 常用取值：14（小）、18（中）、24（大）、32（特大）。
    /// </summary>
    public double FontSize
    {
        get => _fontSize < 0 ? 0 : _fontSize;
        set => _fontSize = value < 0 ? 0 : value;
    }

    // ------------------------------------------------------------ 顶部元素开关
    //
    // 组件顶部有两处「小字」：标题行（标题 + 更新时间）与范围行（近 N 天 · 轮播进度）。
    // 它们会占用组件的固定高度，把下方真正的扣分内容挤出可视区（表现为内容被小字遮挡/裁掉）。
    // 下面三个开关可分别隐藏它们，把有限高度让给扣分内容；全部关闭时内容独占整个组件。
    //
    // 默认值只保留标题「寝室扣分」：它是组件的身份标识，缺失后容易分不清是什么组件；
    // 而更新时间与范围行属于辅助信息，默认隐藏以把高度优先让给扣分内容（可按需在设置中打开）。

    /// <summary>显示标题「寝室扣分」。默认开启。</summary>
    public bool ShowTitle { get; set; } = true;

    /// <summary>显示更新时间（取扣分文章的发布时间）。默认关闭以节省高度。</summary>
    public bool ShowUpdateTime { get; set; }

    /// <summary>显示范围行（近 N 天 · 我的班级 · 第 n/N 条）。默认关闭以节省高度。</summary>
    public bool ShowScope { get; set; }

    /// <summary>顶部标题行是否整体可见（标题或更新时间任一开启即显示该行）。</summary>
    public bool ShowHeaderLine => ShowTitle || ShowUpdateTime;
}
