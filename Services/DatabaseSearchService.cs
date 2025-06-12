using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfBlazorSearchTool.Services
{
    public class DatabaseSearchService
    {
        private readonly IFileSaveService _fileSaveService;
        private readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(7);

        public DatabaseSearchService(IFileSaveService fileSaveService)
        {
            _fileSaveService = fileSaveService;
        }

        public async Task ExecuteSearchAsync(DatabaseSearchParameters parameters, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var keywordResults = new List<KeywordDataResult>();
            var columnResults = new List<ColumnNameResult>();

            try
            {
                if (parameters.PerformKeywordDataSearch)
                {
                    await SearchDataAsync(parameters, progress, cancellationToken, keywordResults);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (parameters.PerformColumnSearch)
                {
                    await SearchColumnNamesAsync(parameters, progress, cancellationToken, columnResults);
                }
            }
            catch (OperationCanceledException)
            {
                progress.Report("[!] Search was cancelled by the user.");
                return;
            }
            catch (Exception ex)
            {
                progress.Report($"[!] An unexpected application error occurred: {ex.Message}");
                return;
            }
            
            cancellationToken.ThrowIfCancellationRequested();

            if (parameters.PerformKeywordDataSearch)
            {
                progress.Report($"\n--- Results: {keywordResults.Count} items found in Keyword Data Search. ---");
                foreach (var result in keywordResults)
                {
                    // VVVV --- ADDED 4 SPACES TO TRIGGER GREEN COLOR --- VVVV
                    progress.Report($"    ✅ FOUND keyword '{result.Keyword}' in DB: {result.DatabaseName}, Table: {result.SchemaName}.{result.TableName}");
                }
                if (keywordResults.Any())
                {
                    SaveKeywordResultsToCsv(keywordResults, parameters.ServerIp, progress);
                }
            }
            
            if (parameters.PerformColumnSearch)
            {
                progress.Report($"\n--- Results: {columnResults.Count} items found in Column Name Search. ---");
                foreach (var result in columnResults)
                {
                    // VVVV --- ADDED 4 SPACES TO TRIGGER GREEN COLOR --- VVVV
                    progress.Report($"    ✅ FOUND column '{result.FoundColumnName}' in DB: {result.DatabaseName}, Table: {result.SchemaName}.{result.TableName}");
                }
                if (columnResults.Any())
                {
                    SaveColumnResultsToCsv(columnResults, parameters.ServerIp, progress);
                }
            }
        }

        #region Core Search Logic (Unchanged)
        private async Task SearchDataAsync(DatabaseSearchParameters parameters, IProgress<string> progress, CancellationToken cancellationToken, List<KeywordDataResult> results)
        {
            progress.Report("\n--- Starting Keyword Data Search ---");
            var connectionString = new SqlConnectionStringBuilder { DataSource = parameters.ServerIp, UserID = parameters.UserId, Password = parameters.Password, TrustServerCertificate = true }.ConnectionString;
            var keywordsLower = parameters.KeywordsToSearchData.Select(k => k.ToLowerInvariant()).ToList();
            
            List<string> databaseNames = await GetDatabaseNamesAsync(connectionString, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (string dbName in databaseNames)
            {
                progress.Report($"-- Searching Database for Data: {dbName} --");
                var dbConnectionString = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = dbName }.ConnectionString;
                
                try
                {
                    List<TableInfo> tables = await GetTablesAndColumnsAsync(dbConnectionString, dbName, true);
                    foreach (TableInfo table in tables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress.Report($"  -> Searching Table: {table.SchemaName}.{table.TableName}");
                        if (!table.AllColumns.Any()) continue;

                        foreach (string keyword in keywordsLower)
                        {
                            await SearchTableForKeywordAsync(dbConnectionString, dbName, table, keyword, parameters.KeywordSearchMode, results, progress, cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    progress.Report($"[!] Error processing database {dbName} for data search: {ex.Message}");
                }
            }
        }

        private async Task SearchColumnNamesAsync(DatabaseSearchParameters parameters, IProgress<string> progress, CancellationToken cancellationToken, List<ColumnNameResult> results)
        {
            progress.Report("\n--- Starting Column Name Search ---");
            var connectionString = new SqlConnectionStringBuilder { DataSource = parameters.ServerIp, UserID = parameters.UserId, Password = parameters.Password, TrustServerCertificate = true }.ConnectionString;
            var columnNamesLower = parameters.ColumnNamesToSearch.Select(c => c.ToLowerInvariant()).ToList();
            
            List<string> databaseNames = await GetDatabaseNamesAsync(connectionString, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (string dbName in databaseNames)
            {
                progress.Report($"-- Searching Database for Columns: {dbName} --");
                var dbConnectionString = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = dbName }.ConnectionString;
                try
                {
                    List<TableInfo> tables = await GetTablesAndColumnsAsync(dbConnectionString, dbName, false);
                    foreach (TableInfo table in tables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (ColumnDetail colDetail in table.AllColumns)
                        {
                            string lowerActualColName = colDetail.Name.ToLowerInvariant();
                            foreach (string searchedColNameLower in columnNamesLower)
                            {
                                if (lowerActualColName == searchedColNameLower)
                                {
                                    results.Add(new ColumnNameResult
                                    {
                                        SearchedColumnName = searchedColNameLower,
                                        FoundColumnName = colDetail.Name,
                                        DatabaseName = dbName,
                                        SchemaName = table.SchemaName,
                                        TableName = table.TableName,
                                        ColumnDataType = colDetail.DataType
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    progress.Report($"[!] Error processing database {dbName} for column search: {ex.Message}");
                }
            }
        }
        #endregion

        #region Helper Methods (Unchanged, except Save methods)

        private async Task SearchTableForKeywordAsync(string dbConnectionString, string dbName, TableInfo table, string lowercasedKeyword, KeywordSearchMode mode, List<KeywordDataResult> results, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var whereClauses = new List<string>();
            string sqlParamValue = (mode == KeywordSearchMode.Contains) ? $"%{lowercasedKeyword}%" : lowercasedKeyword;

            foreach (var col in table.AllColumns)
            {
                string expression = $"LOWER(CAST([{col.Name}] AS NVARCHAR(MAX)))";
                whereClauses.Add(mode == KeywordSearchMode.Contains ? $"{expression} LIKE @keyword_param" : $"{expression} = @keyword_param");
            }

            if (!whereClauses.Any()) return;

            var columnsToSelect = new List<string>();
            if (table.PrimaryKeyColumns.Any())
                columnsToSelect.AddRange(table.PrimaryKeyColumns.Select(pk => $"CAST([{pk}] AS NVARCHAR(MAX)) AS [{pk}_PK]"));
            columnsToSelect.AddRange(table.AllColumns.Select(c => $"CAST([{c.Name}] AS NVARCHAR(255)) AS [{c.Name}_Preview]"));

            string query = $"SELECT {string.Join(", ", columnsToSelect)} FROM [{table.SchemaName}].[{table.TableName}] WITH (NOLOCK) WHERE {string.Join(" OR ", whereClauses)}";

            using var connection = new SqlConnection(dbConnectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@keyword_param", sqlParamValue);
            command.CommandTimeout = 300;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string rowId = "N/A";
                if (table.PrimaryKeyColumns.Any())
                    rowId = string.Join(", ", table.PrimaryKeyColumns.Select(pk => $"{pk}='{reader[$"{pk}_PK"]}'"));
                
                var matchedPreviews = new List<string>();
                foreach (var col in table.AllColumns)
                {
                    object colValue = reader[$"{col.Name}_Preview"];
                    if (colValue != DBNull.Value && colValue.ToString()!.ToLowerInvariant().Contains(lowercasedKeyword))
                    {
                        matchedPreviews.Add($"{col.Name}: \"{SanitizeForCsv(colValue.ToString()!)}\"");
                    }
                }
                
                results.Add(new KeywordDataResult {
                    Keyword = lowercasedKeyword,
                    DatabaseName = dbName,
                    SchemaName = table.SchemaName,
                    TableName = table.TableName,
                    RowIdentifier = rowId,
                    MatchedColumnsPreview = string.Join(" | ", matchedPreviews)
                });
            }
        }

        private async Task<(bool Success, int? ErrorCode)> TryConnectWithTimeoutAsync(string connectionString, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                var connectTask = connection.OpenAsync(cancellationToken);
                var timeoutTask = Task.Delay(_connectionTimeout, cancellationToken);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask) { return (false, null); }
                await connectTask;
                return (true, null);
            }
            catch (SqlException ex) { return (false, ex.Number); }
            catch { return (false, -1); }
        }
        
        private async Task<List<string>> GetDatabaseNamesAsync(string baseConnectionString, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var dbNames = new List<string>();
            var masterBuilder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master", ConnectTimeout = 5 };
            
            progress.Report("[*] Getting list of online databases...");
            var (success, errorCode) = await TryConnectWithTimeoutAsync(masterBuilder.ConnectionString, cancellationToken);
            if (!success)
            {
                progress.Report($"[!] Could not connect to SQL Server to list databases. Error: {errorCode}. Check server IP and credentials.");
                return dbNames;
            }

            using var connection = new SqlConnection(masterBuilder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            string query = "SELECT name FROM sys.databases WHERE database_id > 4 AND state_desc = 'ONLINE' AND is_read_only = 0;";
            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                dbNames.Add(reader.GetString(0));
            }
            progress.Report($"[*] Found {dbNames.Count} user databases to search.");
            return dbNames;
        }

        private async Task<List<TableInfo>> GetTablesAndColumnsAsync(string dbConnectionString, string dbName, bool forKeywordSearch)
        {
            var tables = new List<TableInfo>();
            using var connection = new SqlConnection(dbConnectionString);
            await connection.OpenAsync();

            string columnFilterClause = forKeywordSearch
                ? "AND c.DATA_TYPE IN ('char', 'varchar', 'nchar', 'nvarchar', 'text', 'ntext', 'xml', 'uniqueidentifier', 'date', 'datetime', 'datetime2', 'smalldatetime', 'time', 'datetimeoffset')"
                : "";

            string query = $@"
                SELECT t.TABLE_SCHEMA, t.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE
                FROM INFORMATION_SCHEMA.TABLES t
                INNER JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
                WHERE t.TABLE_TYPE = 'BASE TABLE' {columnFilterClause}
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION;";

            var tableData = new Dictionary<string, TableInfo>();
            using (var command = new SqlCommand(query, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string schema = reader.GetString(0), tableName = reader.GetString(1), colName = reader.GetString(2), dataType = reader.GetString(3);
                    string fullTableName = $"{schema}.{tableName}";
                    
                    if (!tableData.TryGetValue(fullTableName, out var tableInfo))
                    {
                        tableInfo = new TableInfo { DatabaseName = dbName, SchemaName = schema, TableName = tableName };
                        if (forKeywordSearch)
                        {
                            tableInfo.PrimaryKeyColumns.AddRange(await GetPrimaryKeyColumnsAsync(dbConnectionString, schema, tableName));
                        }
                        tableData[fullTableName] = tableInfo;
                    }
                    tableInfo.AllColumns.Add(new ColumnDetail { Name = colName, DataType = dataType });
                }
            }
            return tableData.Values.ToList();
        }

        private async Task<List<string>> GetPrimaryKeyColumnsAsync(string dbConnectionString, string schema, string tableName)
        {
            var pkColumns = new List<string>();
            string pkQuery = @"
                SELECT ku.COLUMN_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS ku ON tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                WHERE ku.TABLE_CATALOG = DB_NAME() AND ku.TABLE_SCHEMA = @schema AND ku.TABLE_NAME = @tableName
                ORDER BY ku.ORDINAL_POSITION;";

            using var connection = new SqlConnection(dbConnectionString);
            if(connection.State != ConnectionState.Open) await connection.OpenAsync();
            using var command = new SqlCommand(pkQuery, connection);
            command.Parameters.AddWithValue("@schema", schema);
            command.Parameters.AddWithValue("@tableName", tableName);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pkColumns.Add(reader.GetString(0));
            }
            return pkColumns;
        }

        private string SanitizeForCsv(string value) => string.IsNullOrEmpty(value) ? "" : value.Replace("\"", "\"\"");

        private void SaveKeywordResultsToCsv(List<KeywordDataResult> results, string serverIp, IProgress<string> progress)
        {
            progress.Report($"[*] Prompting to save Keyword Data Search results...");
            string defaultFileName = $"KeywordDataSearchResults_{serverIp.Replace(".", "_")}_{DateTime.Now:yyyyMMddHHmmss}.csv";
            string? savePath = _fileSaveService.GetSaveAsFilePath(defaultFileName);

            if (string.IsNullOrEmpty(savePath))
            {
                progress.Report("[i] File save cancelled by user.");
                return;
            }
            try
            {
                var csvLines = new List<string> { "\"Keyword\",\"Database\",\"Schema\",\"Table\",\"RowIdentifier\",\"MatchedColumnsWithValuePreview\"" };
                csvLines.AddRange(results.Select(r => $"\"{SanitizeForCsv(r.Keyword)}\",\"{SanitizeForCsv(r.DatabaseName)}\",\"{SanitizeForCsv(r.SchemaName)}\",\"{SanitizeForCsv(r.TableName)}\",\"{SanitizeForCsv(r.RowIdentifier)}\",\"{SanitizeForCsv(r.MatchedColumnsPreview)}\""));
                File.WriteAllLines(savePath, csvLines, Encoding.UTF8);
                // VVVV --- USE FLOPPY DISK ICON --- VVVV
                progress.Report($"💾 Results saved to: {savePath}");
            }
            catch (Exception ex)
            {
                progress.Report($"[!] Failed to write CSV file: {ex.Message}");
            }
        }

        private void SaveColumnResultsToCsv(List<ColumnNameResult> results, string serverIp, IProgress<string> progress)
        {
            progress.Report($"[*] Prompting to save Column Name Search results...");
            string defaultFileName = $"ColumnNameSearchResults_{serverIp.Replace(".", "_")}_{DateTime.Now:yyyyMMddHHmmss}.csv";
            string? savePath = _fileSaveService.GetSaveAsFilePath(defaultFileName);

            if (string.IsNullOrEmpty(savePath))
            {
                progress.Report("[i] File save cancelled by user.");
                return;
            }
            try
            {
                var csvLines = new List<string> { "\"SearchedColumnName\",\"FoundColumnName\",\"DatabaseName\",\"SchemaName\",\"TableName\",\"ColumnDataType\"" };
                csvLines.AddRange(results.Select(r => $"\"{SanitizeForCsv(r.SearchedColumnName)}\",\"{SanitizeForCsv(r.FoundColumnName)}\",\"{SanitizeForCsv(r.DatabaseName)}\",\"{SanitizeForCsv(r.SchemaName)}\",\"{SanitizeForCsv(r.TableName)}\",\"{SanitizeForCsv(r.ColumnDataType)}\""));
                File.WriteAllLines(savePath, csvLines, Encoding.UTF8);
                // VVVV --- USE FLOPPY DISK ICON --- VVVV
                progress.Report($"💾 Results saved to: {savePath}");
            }
            catch (Exception ex)
            {
                progress.Report($"[!] Failed to write CSV file: {ex.Message}");
            }
        }
        #endregion
    }
}