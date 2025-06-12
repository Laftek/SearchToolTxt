using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WpfBlazorSearchTool.Services
{
    public class DatabaseSearchParameters
    {
        [Required(ErrorMessage = "Server IP is required.")]
        public string ServerIp { get; set; } = "172.16.2.16";

        public string UserId { get; set; } = "sa";

        public string Password { get; set; } = "S!ql74123698";

        public bool PerformKeywordDataSearch { get; set; } = true;
        public bool PerformColumnSearch { get; set; } = true;

        [ConditionalListNotEmpty(nameof(PerformKeywordDataSearch), true, ErrorMessage = "At least one keyword must be provided for a data search.")]
        public List<string> KeywordsToSearchData { get; set; } = new List<string> {
           "RCP-MMD-LR_ConRecipeNameMA1"
        };
        public KeywordSearchMode KeywordSearchMode { get; set; } = KeywordSearchMode.Contains;

        [ConditionalListNotEmpty(nameof(PerformColumnSearch), true, ErrorMessage = "At least one column name must be provided for a column search.")]
        public List<string> ColumnNamesToSearch { get; set; } = new List<string> {
            "Aktiv"
        };
    }
}