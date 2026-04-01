using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

public class DatabaseHelper
{
    private string _connectionString;

    public DatabaseHelper(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void SetConnectionString(string connectionString)
    {
        _connectionString = connectionString;
    }

    private SqlConnection GetConnection()
    {
        return new SqlConnection(_connectionString);
    }

    public bool TestConnection()
    {
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public DataTable GetServerInfo()
    {
        string sql = @"
            SELECT
                @@SERVERNAME AS SUNUCU_ADI,
                CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS EDITION,
                CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS VERSIYON,
                CAST(SERVERPROPERTY('Collation') AS nvarchar(128)) AS KARSILASTIRMA,
                (SELECT COUNT(*) FROM sys.databases) AS TOPLAM_DB,
                (SELECT COUNT(*) FROM sys.databases WHERE state_desc = 'ONLINE') AS ONLINE_DB,
                (SELECT COUNT(*) FROM sys.databases WHERE state_desc != 'ONLINE') AS OFFLINE_DB,
                (SELECT CAST(SUM(size) * 8.0 / 1024 / 1024 AS DECIMAL(10,2)) FROM sys.master_files) AS TOPLAM_GB";
        return GetDataTable(sql);
    }

    public DataTable GetDatabaseDetails()
    {
        string sql = @"
            SELECT
                d.database_id AS DB_ID,
                d.name AS NAME,
                CASE d.state_desc
                    WHEN 'ONLINE'     THEN 'Çevrimiçi'
                    WHEN 'OFFLINE'    THEN 'Çevrimdışı'
                    WHEN 'RESTORING'  THEN 'Geri Yükleniyor'
                    WHEN 'RECOVERING' THEN 'Kurtarılıyor'
                    WHEN 'SUSPECT'    THEN 'Şüpheli'
                    ELSE d.state_desc
                END AS DURUM,
                d.state_desc AS DURUM_KOD,
                CASE d.recovery_model_desc
                    WHEN 'FULL'        THEN 'Tam'
                    WHEN 'SIMPLE'      THEN 'Basit'
                    WHEN 'BULK_LOGGED' THEN 'Toplu Günlük'
                    ELSE d.recovery_model_desc
                END AS RECOVERY_MODEL,
                d.create_date AS OLUSTURMA_TARIHI,
                CAST(SUM(mf.size) * 8.0 / 1024 AS DECIMAL(10,2)) AS BOYUT_MB,
                CAST(SUM(CASE WHEN mf.type = 0 THEN mf.size ELSE 0 END) * 8.0 / 1024 AS DECIMAL(10,2)) AS DATA_MB,
                CAST(SUM(CASE WHEN mf.type = 1 THEN mf.size ELSE 0 END) * 8.0 / 1024 AS DECIMAL(10,2)) AS LOG_MB,
                (SELECT MAX(b.backup_finish_date)
                 FROM msdb.dbo.backupset b
                 WHERE b.database_name = d.name AND b.type = 'D') AS SON_BACKUP,
                CASE d.is_read_only WHEN 1 THEN 'Evet' ELSE 'Hayır' END AS SALT_OKUNUR,
                d.page_verify_option_desc AS SAYFA_DOGRULAMA
            FROM sys.databases d
            LEFT JOIN sys.master_files mf ON d.database_id = mf.database_id
            GROUP BY d.database_id, d.name, d.state_desc, d.recovery_model_desc,
                     d.create_date, d.is_read_only, d.page_verify_option_desc
            ORDER BY d.name";
        return GetDataTable(sql);
    }

    public DataTable GetDatabaseTables(string dbName)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $@"
            SELECT
                t.name AS TABLO_ADI,
                s.name AS SEMA,
                p.rows AS SATIR_SAYISI,
                CAST((SUM(a.total_pages) * 8.0 / 1024) AS DECIMAL(10,2)) AS BOYUT_MB,
                t.create_date AS OLUSTURMA,
                t.modify_date AS SON_DEGISIKLIK
            FROM [{dbName}].sys.tables t
            INNER JOIN [{dbName}].sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN [{dbName}].sys.indexes i ON t.object_id = i.object_id AND i.index_id IN (0,1)
            INNER JOIN [{dbName}].sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
            INNER JOIN [{dbName}].sys.allocation_units a ON p.partition_id = a.container_id
            GROUP BY t.name, s.name, p.rows, t.create_date, t.modify_date
            ORDER BY p.rows DESC";
        return GetDataTable(sql);
    }

    public DataTable GetDatabaseProcedures(string dbName)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $@"
            SELECT
                p.name AS PROSEDUR_ADI,
                s.name AS SEMA,
                p.create_date AS OLUSTURMA,
                p.modify_date AS SON_DEGISIKLIK
            FROM [{dbName}].sys.procedures p
            INNER JOIN [{dbName}].sys.schemas s ON p.schema_id = s.schema_id
            ORDER BY p.name";
        return GetDataTable(sql);
    }

    public DataTable GetDatabaseFiles(string dbName)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $@"
            SELECT
                f.name AS DOSYA_ADI,
                CASE f.type WHEN 0 THEN 'Veri' WHEN 1 THEN 'Log' ELSE 'Diğer' END AS TIP,
                f.physical_name AS FIZIKSEL_YOL,
                CAST(f.size * 8.0 / 1024 AS DECIMAL(10,2)) AS BOYUT_MB,
                CASE f.max_size WHEN -1 THEN 'Sınırsız'
                    ELSE CAST(CAST(f.max_size * 8.0 / 1024 AS DECIMAL(10,2)) AS varchar) + ' MB'
                END AS MAX_BOYUT,
                CAST(f.growth AS varchar) + CASE f.is_percent_growth WHEN 1 THEN '%' ELSE ' KB' END AS ARTIS
            FROM [{dbName}].sys.database_files f
            ORDER BY f.type";
        return GetDataTable(sql);
    }

    public DataTable GetSqlJobs()
    {
        string sql = @"
            SELECT
                j.name AS JOB_ADI,
                CASE j.enabled WHEN 1 THEN 'Aktif' ELSE 'Pasif' END AS DURUM,
                CASE h.run_status
                    WHEN 0 THEN 'Başarısız'
                    WHEN 1 THEN 'Başarılı'
                    WHEN 2 THEN 'Yeniden Deneniyor'
                    WHEN 3 THEN 'İptal Edildi'
                    WHEN 4 THEN 'Devam Ediyor'
                    ELSE 'Henüz Çalışmadı'
                END AS SON_DURUM,
                CASE WHEN h.run_date IS NOT NULL THEN
                    CONVERT(datetime,
                        CONVERT(varchar(8), h.run_date) + ' ' +
                        STUFF(STUFF(RIGHT('000000' + CONVERT(varchar(6), h.run_time), 6), 3, 0, ':'), 6, 0, ':'))
                ELSE NULL END AS SON_CALISMA,
                c.name AS KATEGORI,
                j.description AS ACIKLAMA,
                j.enabled AS ENABLED_BIT
            FROM msdb.dbo.sysjobs j
            LEFT JOIN msdb.dbo.sysjobhistory h
                ON j.job_id = h.job_id
                AND h.instance_id = (
                    SELECT MAX(instance_id) FROM msdb.dbo.sysjobhistory
                    WHERE job_id = j.job_id AND step_id = 0)
            LEFT JOIN msdb.dbo.syscategories c ON j.category_id = c.category_id
            ORDER BY j.name";
        return GetDataTable(sql);
    }

    public bool BackupDatabase(string dbName, string backupPath)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $"BACKUP DATABASE [{dbName}] TO DISK = @BackupPath WITH FORMAT, INIT, NAME = '{dbName} Full Backup', STATS = 10";
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 600;
                    command.Parameters.AddWithValue("@BackupPath", backupPath);
                    command.ExecuteNonQuery();
                    DatabaseLog.WriteLog($"Backup alındı: {dbName} -> {backupPath}");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"Backup hatası [{dbName}]: {ex.Message}");
            throw;
        }
    }

    public void ShrinkDatabase(string dbName, int targetPercent = 10)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $"DBCC SHRINKDATABASE ([{dbName}], {targetPercent})";
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 3600;
                    command.ExecuteNonQuery();
                    DatabaseLog.WriteLog($"Shrink (DB): [{dbName}] hedef %{targetPercent}");
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"ShrinkDatabase hatası [{dbName}]: {ex.Message}");
            throw;
        }
    }

    public void ShrinkLog(string dbName)
    {
        dbName = SanitizeDbName(dbName);
        // Log dosyasının adını al
        string getLogSql = $"SELECT name FROM [{dbName}].sys.database_files WHERE type = 1";
        string logFileName = null;
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var cmd = new SqlCommand(getLogSql, connection))
                    logFileName = cmd.ExecuteScalar()?.ToString();

                if (string.IsNullOrEmpty(logFileName))
                    throw new Exception("Log dosyası bulunamadı.");

                string shrinkSql = $"USE [{dbName}]; DBCC SHRINKFILE ([{logFileName}], 1)";
                using (var cmd = new SqlCommand(shrinkSql, connection))
                {
                    cmd.CommandTimeout = 3600;
                    cmd.ExecuteNonQuery();
                    DatabaseLog.WriteLog($"Shrink (Log): [{dbName}] log dosyası: {logFileName}");
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"ShrinkLog hatası [{dbName}]: {ex.Message}");
            throw;
        }
    }

    public DataTable GetFragmentation(string dbName)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $@"
            SELECT
                OBJECT_NAME(i.object_id, DB_ID(N'{dbName}')) AS TABLO_ADI,
                ISNULL(i.name, '(Heap)')                      AS INDEX_ADI,
                i.type_desc                                   AS INDEX_TIPI,
                CAST(s.avg_fragmentation_in_percent AS DECIMAL(5,1)) AS FRAGMENTASYON,
                s.page_count                                  AS SAYFA_SAYISI,
                s.record_count                                AS KAYIT_SAYISI,
                CASE
                    WHEN s.avg_fragmentation_in_percent >= 30 THEN 'Kritik'
                    WHEN s.avg_fragmentation_in_percent >= 10 THEN 'Orta'
                    ELSE 'İyi'
                END AS DURUM
            FROM sys.dm_db_index_physical_stats(DB_ID(N'{dbName}'), NULL, NULL, NULL, 'LIMITED') s
            INNER JOIN [{dbName}].sys.indexes i
                ON s.object_id = i.object_id AND s.index_id = i.index_id
            WHERE s.index_id > 0
              AND s.page_count >= 1
            ORDER BY s.avg_fragmentation_in_percent DESC";
        return GetDataTable(sql);
    }

    public void RebuildIndex(string dbName, string tableName, string indexName)
    {
        dbName    = SanitizeDbName(dbName);
        tableName = SanitizeDbName(tableName);
        indexName = SanitizeDbName(indexName);
        string sql = $"USE [{dbName}]; ALTER INDEX [{indexName}] ON [{tableName}] REBUILD WITH (ONLINE = OFF)";
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 3600;
                    command.ExecuteNonQuery();
                    DatabaseLog.WriteLog($"Index rebuild: [{dbName}].[{tableName}].[{indexName}]");
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"Rebuild hatası [{dbName}].[{tableName}].[{indexName}]: {ex.Message}");
            throw;
        }
    }

    public void ReorganizeIndex(string dbName, string tableName, string indexName)
    {
        dbName    = SanitizeDbName(dbName);
        tableName = SanitizeDbName(tableName);
        indexName = SanitizeDbName(indexName);
        string sql = $"USE [{dbName}]; ALTER INDEX [{indexName}] ON [{tableName}] REORGANIZE";
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 3600;
                    command.ExecuteNonQuery();
                    DatabaseLog.WriteLog($"Index reorganize: [{dbName}].[{tableName}].[{indexName}]");
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"Reorganize hatası [{dbName}].[{tableName}].[{indexName}]: {ex.Message}");
            throw;
        }
    }

    public DataTable GetActiveConnections()
    {
        string sql = @"
            SELECT s.session_id AS SESSION_ID, s.login_name AS KULLANICI,
                s.host_name AS MAKINE, s.program_name AS UYGULAMA,
                DB_NAME(s.database_id) AS VERITABANI, s.status AS DURUM,
                s.cpu_time AS CPU_MS, s.memory_usage*8 AS BELLEK_KB,
                s.logical_reads AS MANTIKSAL_OKUMA,
                DATEDIFF(SECOND,s.last_request_start_time,GETDATE()) AS SURE_SN,
                ISNULL(CAST(r.blocking_session_id AS VARCHAR),'—') AS BLOKLAYAN,
                ISNULL(r.wait_type,'—') AS BEKLEME_TIPI
            FROM sys.dm_exec_sessions s
            LEFT JOIN sys.dm_exec_requests r ON s.session_id=r.session_id
            WHERE s.is_user_process=1 ORDER BY s.session_id";
        return GetDataTable(sql);
    }

    public DataTable GetLongRunningQueries()
    {
        string sql = @"
            SELECT r.session_id AS SESSION_ID, s.login_name AS KULLANICI,
                DB_NAME(r.database_id) AS VERITABANI,
                DATEDIFF(SECOND,r.start_time,GETDATE()) AS SURE_SN,
                r.status AS DURUM, r.cpu_time AS CPU_MS,
                r.logical_reads AS MANTIKSAL_OKUMA,
                ISNULL(r.wait_type,'—') AS BEKLEME_TIPI,
                ISNULL(CAST(r.blocking_session_id AS VARCHAR),'—') AS BLOKLAYAN,
                SUBSTRING(t.text,1,500) AS SORGU_METNI
            FROM sys.dm_exec_requests r
            INNER JOIN sys.dm_exec_sessions s ON r.session_id=s.session_id
            CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
            WHERE s.is_user_process=1 AND r.status<>'background'
            ORDER BY SURE_SN DESC";
        return GetDataTable(sql);
    }

    public DataTable GetLockInfo()
    {
        string sql = @"
            SELECT l.request_session_id AS SESSION_ID, s.login_name AS KULLANICI,
                DB_NAME(l.resource_database_id) AS VERITABANI,
                l.resource_type AS KAYNAK_TIPI,
                l.resource_description AS KAYNAK,
                l.request_mode AS KILIT_MODU, l.request_status AS DURUM,
                ISNULL(CAST(r.blocking_session_id AS VARCHAR),'—') AS BLOKLAYAN,
                ISNULL(r.wait_time,0) AS BEKLEME_MS,
                ISNULL(r.wait_type,'—') AS BEKLEME_TIPI
            FROM sys.dm_tran_locks l
            INNER JOIN sys.dm_exec_sessions s ON l.request_session_id=s.session_id
            LEFT  JOIN sys.dm_exec_requests r ON l.request_session_id=r.session_id
            WHERE s.is_user_process=1 ORDER BY l.request_session_id";
        return GetDataTable(sql);
    }

    public DataTable GetTempDbUsage()
    {
        string sql = @"
            SELECT s.session_id AS SESSION_ID, s.login_name AS KULLANICI,
                s.host_name AS MAKINE, DB_NAME(s.database_id) AS VERITABANI,
                u.user_objects_alloc_page_count*8 AS KULLANICI_OBJ_KB,
                u.internal_objects_alloc_page_count*8 AS DAHILI_OBJ_KB,
                (u.user_objects_alloc_page_count+u.internal_objects_alloc_page_count)*8 AS TOPLAM_KB
            FROM sys.dm_db_session_space_usage u
            INNER JOIN sys.dm_exec_sessions s ON u.session_id=s.session_id
            WHERE s.is_user_process=1
              AND (u.user_objects_alloc_page_count+u.internal_objects_alloc_page_count)>0
            ORDER BY TOPLAM_KB DESC";
        return GetDataTable(sql);
    }

    public DataTable GetDatabaseUsers(string dbName)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $@"
            USE [{dbName}];
            SELECT
                u.name AS KULLANICI_ADI,
                ISNULL(l.name, '—') AS LOGIN_ADI,
                CASE u.type
                    WHEN 'S' THEN 'SQL'
                    WHEN 'U' THEN 'Windows'
                    WHEN 'G' THEN 'Win Grup'
                    ELSE u.type_desc
                END AS AUTH_TIP,
                ISNULL(STUFF((
                    SELECT ', ' + r2.name
                    FROM sys.database_role_members rm2
                    INNER JOIN sys.database_principals r2
                        ON r2.principal_id = rm2.role_principal_id
                    WHERE rm2.member_principal_id = u.principal_id
                    FOR XML PATH(''), TYPE).value('.','nvarchar(max)'), 1, 2, ''), '—') AS ROLLER,
                u.create_date AS OLUSTURMA,
                u.modify_date AS DEGISTIRME
            FROM sys.database_principals u
            LEFT JOIN sys.server_principals l ON u.sid = l.sid
            WHERE u.type IN ('S','U','G','C','K')
            ORDER BY u.name";
        return GetDataTable(sql);
    }

    public string GenerateDatabaseScript(string dbName)
    {
        dbName = SanitizeDbName(dbName);
        string sql = $@"
            SELECT d.name, d.recovery_model_desc, d.collation_name,
                d.compatibility_level, mf.type_desc, mf.physical_name,
                mf.size*8/1024 AS size_mb, mf.name AS logical_name
            FROM sys.databases d
            INNER JOIN sys.master_files mf ON d.database_id=mf.database_id
            WHERE d.name=N'{dbName}'";
        var dt = GetDataTable(sql);
        if (dt == null || dt.Rows.Count == 0) return $"-- '{dbName}' bulunamadı";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- [{dbName}] Database Script");
        sb.AppendLine($"-- Oluşturuldu: {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine("-- !! Dosya yollarını hedef sunucuya göre düzenleyin !!");
        sb.AppendLine();
        sb.AppendLine($"CREATE DATABASE [{dbName}]");
        sb.AppendLine("COLLATE " + dt.Rows[0]["collation_name"]);
        sb.AppendLine("GO");
        sb.AppendLine($"ALTER DATABASE [{dbName}] SET RECOVERY {dt.Rows[0]["recovery_model_desc"]} GO");
        sb.AppendLine($"ALTER DATABASE [{dbName}] SET COMPATIBILITY_LEVEL = {dt.Rows[0]["compatibility_level"]} GO");
        sb.AppendLine();
        sb.AppendLine("-- Dosya bilgisi:");
        foreach (DataRow r in dt.Rows)
            sb.AppendLine($"--  [{r["logical_name"]}]  {r["type_desc"]}  {r["physical_name"]}  ({r["size_mb"]} MB)");
        return sb.ToString();
    }

    private static string SanitizeDbName(string dbName)
    {
        if (string.IsNullOrWhiteSpace(dbName))
            throw new ArgumentException("Veritabanı adı boş olamaz.");
        if (!Regex.IsMatch(dbName, @"^[\w\s\-\.]+$"))
            throw new ArgumentException("Geçersiz veritabanı adı.");
        return dbName;
    }

    public bool CreateTable(string createTableSql)
    {
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(createTableSql, connection))
                {
                    command.ExecuteNonQuery();
                    DatabaseLog.WriteLog("Table created successfully.");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"CreateTable Error: {ex.Message}");
            return false;
        }
    }

    public DataTable GetDataTable(string selectSql, params SqlParameter[] parameters)
    {
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(selectSql, connection))
                {
                    if (parameters != null)
                        command.Parameters.AddRange(parameters);
                    using (var adapter = new SqlDataAdapter(command))
                    {
                        var dataTable = new DataTable();
                        adapter.Fill(dataTable);
                        return dataTable;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"GetDataTable Error: {ex.Message}");
            return null;
        }
    }

    public List<T> GetEntities<T>(string selectSql, params SqlParameter[] parameters) where T : new()
    {
        DataTable dataTable = GetDataTable(selectSql, parameters);
        if (dataTable != null)
            return dataTable.ToList<T>();
        return new List<T>();
    }

    public T GetEntity<T>(string selectSql, params SqlParameter[] parameters) where T : new()
    {
        DataTable dataTable = GetDataTable(selectSql, parameters);
        if (dataTable != null)
            return dataTable.ToSingle<T>();
        return default;
    }

    public int? InsertData(string insertSql, params SqlParameter[] parameters)
    {
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(insertSql + ";SELECT SCOPE_IDENTITY();", connection))
                {
                    if (parameters != null)
                        command.Parameters.AddRange(parameters);
                    object result = command.ExecuteScalar();
                    if (result != null && int.TryParse(result.ToString(), out int newId))
                    {
                        DatabaseLog.WriteLog("Data inserted successfully with ID: " + newId);
                        return newId;
                    }
                    else
                    {
                        DatabaseLog.WriteLog("Data was inserted but ID could not be retrieved.");
                        return null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"InsertData Error: {ex.Message}");
            return null;
        }
    }

    public bool UpdateData(string updateSql, params SqlParameter[] parameters)
    {
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(updateSql, connection))
                {
                    if (parameters != null)
                        command.Parameters.AddRange(parameters);
                    int rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected > 0)
                    {
                        DatabaseLog.WriteLog("Data updated successfully.");
                        return true;
                    }
                    else
                    {
                        DatabaseLog.WriteLog("No data was updated.");
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"UpdateData Error: {ex.Message}");
            return false;
        }
    }

    public bool DeleteData(string deleteSql, params SqlParameter[] parameters)
    {
        try
        {
            using (var connection = GetConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(deleteSql, connection))
                {
                    if (parameters != null)
                        command.Parameters.AddRange(parameters);
                    int rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected > 0)
                    {
                        DatabaseLog.WriteLog("Data deleted successfully.");
                        return true;
                    }
                    else
                    {
                        DatabaseLog.WriteLog("No data was deleted.");
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DatabaseLog.WriteLog($"DeleteData Error: {ex.Message}");
            return false;
        }
    }
}

public static class DataTableExtensions
{
    public static List<T> ToList<T>(this DataTable dataTable) where T : new()
    {
        var dataList = new List<T>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (DataRow row in dataTable.Rows)
        {
            var item = new T();
            foreach (var property in properties)
            {
                if (dataTable.Columns.Contains(property.Name) && row[property.Name] != DBNull.Value)
                    property.SetValue(item, Convert.ChangeType(row[property.Name], property.PropertyType), null);
            }
            dataList.Add(item);
        }
        return dataList;
    }

    public static T ToSingle<T>(this DataTable dataTable) where T : new()
    {
        if (dataTable.Rows.Count == 0)
            return default;
        var item = new T();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var row = dataTable.Rows[0];
        foreach (var property in properties)
        {
            if (dataTable.Columns.Contains(property.Name) && row[property.Name] != DBNull.Value)
                property.SetValue(item, Convert.ChangeType(row[property.Name], property.PropertyType), null);
        }
        return item;
    }
}

public class DatabaseLog
{
    private static readonly string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "\\Log\\", "DatabaseLog");

    public static void WriteLog(string message)
    {
        try
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string fullPath = Path.Combine(logDirectory, $"{date}_DatabaseLog.txt");
            EnsureDirectoryExists(logDirectory);
            using (StreamWriter sw = new StreamWriter(fullPath, true))
                sw.WriteLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - {message}");
        }
        catch (Exception ex)
        {
            WriteErrorLog($"Event Log Error: {ex.Message}");
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static void WriteErrorLog(string message)
    {
        try
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string fullPath = Path.Combine(logDirectory, $"{date}_DatabaseLog.txt");
            EnsureDirectoryExists(logDirectory);
            using (StreamWriter sw = new StreamWriter(fullPath, true))
                sw.WriteLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - {message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing error log: {ex.Message}");
        }
    }
}
