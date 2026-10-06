using System.Collections.Generic;

namespace DormScoreViewer.Models;

/// <summary>
/// 某一天（一条公告）的扣分数据。
/// </summary>
public class DayRecords
{
    /// <summary>公告文章 ID。</summary>
    public int ArticleId { get; set; }

    /// <summary>日期标签（公告标题）。</summary>
    public string Label { get; set; } = "";

    /// <summary>这一天的扣分明细。</summary>
    public List<DormRecord> Records { get; set; } = new();

    /// <summary>本次数据是否来自本地缓存（true 表示联网抓取失败，使用了缓存）。</summary>
    public bool FromCache { get; set; }

    /// <summary>本次抓取该天的失败原因；null 或空表示成功。</summary>
    public string? Error { get; set; }
}
