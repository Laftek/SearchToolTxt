namespace WpfBlazorSearchTool.Services
{
    public interface IFileSaveService
    {
        string? GetSaveAsFilePath(string defaultFileName);
    }
}