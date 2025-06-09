namespace WpfBlazorSearchTool.Services
{
    public class SearchResult
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Keyword { get; set; } = string.Empty;
        public string LineText { get; set; } = string.Empty;
    }
}