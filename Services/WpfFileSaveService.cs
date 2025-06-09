using Microsoft.Win32;

namespace WpfBlazorSearchTool.Services
{
    public class WpfFileSaveService : IFileSaveService
    {
        public string? GetSaveAsFilePath(string defaultFileName)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = "CSV (Comma-separated values)|*.csv|All files|*.*",
                Title = "Save Search Results"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                return saveFileDialog.FileName;
            }

            return null;
        }
    }
}