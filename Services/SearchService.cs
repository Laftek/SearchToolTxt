using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfBlazorSearchTool.Services
{
    public class SearchService
    {
        private readonly IFileSaveService _fileSaveService;
        // Set a reasonable timeout for network operations.
        private readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(7);

        public SearchService(IFileSaveService fileSaveService)
        {
            _fileSaveService = fileSaveService;
        }

        private enum ConnectionAttemptResult
        {
            Success,
            Failed,
            TimedOut
        }

        private async Task<(ConnectionAttemptResult Result, int? ErrorCode)> TryConnectWithTimeoutAsync(string share, string? username, string? password, CancellationToken cancellationToken)
        {
            try
            {
                var connectTask = Task.Run(() => NativeMethods.ConnectToRemote(share, username, password), cancellationToken);
                var timeoutTask = Task.Delay(_connectionTimeout, cancellationToken);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // Timeout occurred
                    return (ConnectionAttemptResult.TimedOut, null);
                }

                // If we get here, connectTask finished. We need to await it to see if it threw an exception.
                await connectTask; 
                return (ConnectionAttemptResult.Success, null);
            }
            catch (Win32Exception ex)
            {
                // Connection failed with a specific Windows error
                return (ConnectionAttemptResult.Failed, ex.NativeErrorCode);
            }
            catch (OperationCanceledException)
            {
                // This will be caught by the outer handler, but good to be specific
                throw; 
            }
            catch
            {
                // Any other unexpected exception during the connection attempt
                return (ConnectionAttemptResult.Failed, null);
            }
        }

        public async Task ExecuteSearchAsync(SearchParameters parameters, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var foundResults = new List<SearchResult>();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (parameters.IsLocalSearch)
                {
                    // Local search logic is unchanged
                    progress.Report("[*] Performing local search...");
                    foreach (var folderPath in parameters.LocalFolders)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress.Report($"[*] Searching in local folder: {folderPath}");
                        if (!Directory.Exists(folderPath))
                        {
                            progress.Report($"[!] Folder not found or inaccessible: {folderPath}");
                            continue;
                        }
                        await Task.Run(() => RecursiveFileSearch(folderPath, parameters, foundResults, progress, cancellationToken), cancellationToken);
                    }
                }
                else // --- VVVV NEW REMOTE SEARCH LOGIC WITH TIMEOUTS VVVV ---
                {
                    var foldersByDrive = parameters.RemoteFolders
                        .Where(p => !string.IsNullOrWhiteSpace(Path.GetPathRoot(p)))
                        .GroupBy(p => Path.GetPathRoot(p)!, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (!foldersByDrive.Any())
                    {
                        progress.Report("[!] No valid remote paths with drive letters provided.");
                        return;
                    }

                    foreach (var driveGroup in foldersByDrive)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string rootPath = driveGroup.Key;
                        char driveLetter = rootPath[0];
                        string rootShareForConnection = $@"\\{parameters.IpAddress}\{driveLetter}$";
                        bool connectionEstablishedForThisDrive = false;

                        try
                        {
                            progress.Report($"\n--- Processing Drive {driveLetter}: ---");

                            // Step 1: Implicit Connection with Timeout
                            progress.Report($"[*] Step 1: Attempting implicit connection to {rootShareForConnection}...");
                            var implicitResult = await TryConnectWithTimeoutAsync(rootShareForConnection, null, null, cancellationToken);
                            
                            if (implicitResult.Result == ConnectionAttemptResult.Success)
                            {
                                progress.Report($"[+] Connected implicitly to {driveLetter}$.");
                                connectionEstablishedForThisDrive = true;
                            }
                            else if(implicitResult.Result == ConnectionAttemptResult.TimedOut)
                            {
                                progress.Report($"[!] Timed out connecting to {rootShareForConnection}. The server is likely offline or unreachable.");
                            }
                            else
                            {
                                progress.Report($"[i] Implicit connection to {driveLetter}$ failed (Error: {implicitResult.ErrorCode}).");
                            }

                            cancellationToken.ThrowIfCancellationRequested();

                            // Step 2: Default Credentials with Timeout
                            if (!connectionEstablishedForThisDrive)
                            {
                                progress.Report($"[*] Step 2: Attempting connection to {driveLetter}$ with provided credentials...");
                                var explicitResult = await TryConnectWithTimeoutAsync(rootShareForConnection, parameters.Username, parameters.Password, cancellationToken);

                                if (explicitResult.Result == ConnectionAttemptResult.Success)
                                {
                                    progress.Report($"[+] Connected to {driveLetter}$ using provided credentials.");
                                    connectionEstablishedForThisDrive = true;
                                }
                                else if (explicitResult.Result == ConnectionAttemptResult.TimedOut)
                                {
                                    progress.Report($"[!] Timed out connecting to {rootShareForConnection}. The server is likely offline or unreachable.");
                                }
                                else
                                {
                                    progress.Report($"[!] Provided credentials for {driveLetter}$ failed (Error: {explicitResult.ErrorCode}).");
                                }
                            }
                            cancellationToken.ThrowIfCancellationRequested();

                            if (connectionEstablishedForThisDrive)
                            {
                                // Search logic for this drive
                                foreach (var userPath in driveGroup)
                                {
                                     cancellationToken.ThrowIfCancellationRequested();
                                     var relativePath = userPath.Substring(rootPath.Length);
                                     var fullUncPath = Path.Combine(rootShareForConnection, relativePath);
                                     progress.Report($"[*] Searching in: {fullUncPath}");
                                     if (!Directory.Exists(fullUncPath))
                                     {
                                         progress.Report($"[!] Folder not found or inaccessible: {fullUncPath}");
                                         continue;
                                     }
                                     await Task.Run(() => RecursiveFileSearch(fullUncPath, parameters, foundResults, progress, cancellationToken), cancellationToken);
                                }
                            }
                            else
                            {
                                progress.Report($"[!] Could not connect to share {rootShareForConnection}. Skipping all paths for drive {driveLetter}:.");
                            }
                        }
                        finally
                        {
                            if (connectionEstablishedForThisDrive)
                            {
                                progress.Report($"[*] Disconnecting from {rootShareForConnection}...");
                                await Task.Run(() => NativeMethods.DisconnectFromRemote(rootShareForConnection));
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                progress.Report("[!] Search was cancelled by the user.");
            }
            catch (Exception ex)
            {
                progress.Report($"[!] An unexpected application error occurred: {ex.Message}");
            }

            // Save results logic remains the same
            if (!cancellationToken.IsCancellationRequested && foundResults.Count > 0)
            {
                progress.Report($"\n[*] Found {foundResults.Count} results. Prompting to save file...");
                SaveResults(foundResults, parameters, progress);
            }
            else if (!cancellationToken.IsCancellationRequested && foundResults.Count == 0)
            {
                progress.Report("\n[*] Search complete. No keywords found.");
            }
        }
        
        // RecursiveFileSearch and SaveResults methods are unchanged...
        private void RecursiveFileSearch(string currentDirectory, SearchParameters parameters, List<SearchResult> foundResults, IProgress<string> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(currentDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (System.Linq.Enumerable.Any(parameters.Extensions, ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            var lines = File.ReadLines(file);
                            int lineNumber = 0;
                            foreach (string line in lines)
                            {
                                lineNumber++;
                                foreach (var keyword in parameters.Keywords)
                                {
                                    if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                                    {
                                        foundResults.Add(new SearchResult
                                        {
                                            FilePath = file,
                                            LineNumber = lineNumber,
                                            Keyword = keyword,
                                            LineText = line.Trim()
                                        });
                                        progress.Report($"    FOUND: Keyword '{keyword}' in {file} (Line: {lineNumber})");
                                    }
                                }
                            }
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                        {
                            progress.Report($"[!] Could not read file {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                progress.Report($"[!] Access denied to list files in directory: {currentDirectory}");
            }
            catch (Exception ex)
            {
                progress.Report($"[!] Error enumerating files in {currentDirectory}: {ex.Message}");
            }

            if (parameters.SearchSubdirectories)
            {
                string[] subdirectories = Array.Empty<string>();
                try
                {
                    subdirectories = Directory.GetDirectories(currentDirectory);
                }
                catch (UnauthorizedAccessException)
                {
                    progress.Report($"[!] Access denied to list subdirectories in: {currentDirectory}");
                    return; 
                }
                catch (Exception ex)
                {
                    progress.Report($"[!] Error getting subdirectories from {currentDirectory}: {ex.Message}");
                    return;
                }

                foreach (var subDir in subdirectories)
                {
                    try
                    {
                        RecursiveFileSearch(subDir, parameters, foundResults, progress, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        progress.Report($"[!] An error occurred while processing subdirectory {subDir}: {ex.Message}");
                    }
                }
            }
        }
        private void SaveResults(List<SearchResult> results, SearchParameters parameters, IProgress<string> progress)
        {
            string defaultFileName = parameters.IsLocalSearch
                ? $"LocalSearchResults_{DateTime.Now:yyyyMMddHHmmss}.csv"
                : $"RemoteSearchResults_{parameters.IpAddress.Replace(".", "_")}_{DateTime.Now:yyyyMMddHHmmss}.csv";

            string? savePath = _fileSaveService.GetSaveAsFilePath(defaultFileName);

            if (string.IsNullOrEmpty(savePath))
            {
                progress.Report("[i] File save cancelled by user.");
                return;
            }

            try
            {
                var csvLines = new List<string> { "\"File\";\"Line\";\"Keyword\";\"Text\"" };
                foreach (var res in results)
                {
                    string escapedLineText = res.LineText.Replace("\"", "\"\"");
                    csvLines.Add($"\"{res.FilePath}\";{res.LineNumber};\"{res.Keyword}\";\"{escapedLineText}\"");
                }
                File.WriteAllLines(savePath, csvLines, Encoding.UTF8);
                progress.Report($"✅ Results saved to: {savePath}");
            }
            catch (Exception ex)
            {
                progress.Report($"[!] Failed to write CSV file: {ex.Message}");
            }
        }
        
        #region NativeMethods (P/Invoke) - Unchanged
        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
            public class NETRESOURCEA
            {
                public int dwScope = 0;
                public int dwType = 0;
                public int dwDisplayType = 0;
                public int dwUsage = 0;
                public string lpLocalName = null;
                public string lpRemoteName = null;
                public string lpComment = null;
                public string lpProvider = null;
            }

            [DllImport("mpr.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            public static extern int WNetAddConnection2A(NETRESOURCEA lpNetResource, string lpPassword, string lpUsername, int dwFlags);

            [DllImport("mpr.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            public static extern int WNetCancelConnection2A(string lpName, int dwFlags, bool fForce);

            public const int NO_ERROR = 0;

            public static void ConnectToRemote(string remoteUNCShare, string? username, string? password)
            {
                var nr = new NETRESOURCEA { dwType = 1, lpRemoteName = remoteUNCShare };
                int result = WNetAddConnection2A(nr, password, username, 0);
                if (result != NO_ERROR)
                {
                    throw new Win32Exception(result);
                }
            }

            public static void DisconnectFromRemote(string remoteUNCShare)
            {
                WNetCancelConnection2A(remoteUNCShare, 0, true);
            }
        }
        #endregion
    }
}