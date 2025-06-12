namespace WpfBlazorSearchTool.Services
{
    // ENUM for keyword search mode
    public enum KeywordSearchMode
    {
        Contains,
        ExactMatch
    }

    public class ColumnDetail
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
    }

    public class TableInfo
    {
        public string DatabaseName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public List<ColumnDetail> AllColumns { get; set; } = new List<ColumnDetail>();
        public List<string> PrimaryKeyColumns { get; set; } = new List<string>();
    }

    // --- VVVV  NEW RESULT MODELS  VVVV ---
    public class KeywordDataResult
    {
        public string Keyword { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string RowIdentifier { get; set; } = string.Empty;
        public string MatchedColumnsPreview { get; set; } = string.Empty;
    }

    public class ColumnNameResult
    {
        public string SearchedColumnName { get; set; } = string.Empty;
        public string FoundColumnName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string ColumnDataType { get; set; } = string.Empty;
    }
    // --- ^^^^ END OF NEW RESULT MODELS ^^^^ ---
}