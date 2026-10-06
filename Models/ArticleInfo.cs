namespace DormScoreViewer.Models;

/// <summary>
/// 寝室内务栏目中的一篇公告。
/// </summary>
public class ArticleInfo
{
    /// <summary>公告文章 ID。</summary>
    public int Id { get; set; }

    /// <summary>公告标题（通常即为日期）。</summary>
    public string Title { get; set; } = "";

    public override string ToString() => Title;
}
