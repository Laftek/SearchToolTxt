using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WpfBlazorSearchTool.Services
{
    public class SearchParameters
    {
        [Required(ErrorMessage = "IP Address is required.")]
        public string IpAddress { get; set; } = "172.16.2.16";

        [ListMustContainElements(nameof(IsLocalSearch), false, ErrorMessage = "At least one remote folder must be provided for a remote search.")]
        public List<string> RemoteFolders { get; set; } = new List<string> { @"C:\HMI" };

        [ListMustContainElements(nameof(IsLocalSearch), true, ErrorMessage = "At least one local folder must be provided for a local search.")]
        public List<string> LocalFolders { get; set; } = new List<string> { @"C:\Temp", @"C:\Temp1" };

        [ListMustContainElements(ErrorMessage = "At least one file extension must be provided.")]
        public List<string> Extensions { get; set; } = new List<string> { ".txt", ".ini", ".csv", ".log", ".config" };

        [ListMustContainElements(ErrorMessage = "At least one keyword must be provided.")]
        public List<string> Keywords { get; set; } = new List<string> { "error", "timeout", "connection", "failed" };

        public string Username { get; set; } = @".\hmi-plc-user";

        public string Password { get; set; } = "H!mi74123698";

        public bool SearchSubdirectories { get; set; } = true;

        public bool IsLocalSearch => IpAddress.Equals("127.0.0.1", System.StringComparison.OrdinalIgnoreCase) ||
                                     IpAddress.Equals("localhost", System.StringComparison.OrdinalIgnoreCase);
    }
}