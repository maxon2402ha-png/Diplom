using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using КР_Ханников.Core;

namespace КР_Ханников.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class BackupService : IDisposable
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
        private const int MaxBackupsToKeep = 14;

        private Timer? _timer;
        private DateTime _lastScheduledRun = DateTime.MinValue;

        public static string BackupsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ServiceDesk", "Backups");

        public void Start()
        {
            Directory.CreateDirectory(BackupsDirectory);
            _timer = new Timer(_ => RunIfScheduled(), null, TimeSpan.FromMinutes(1), CheckInterval);
            Serilog.Log.Information("[Backup] Schedule started, target time 03:00 daily");
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private void RunIfScheduled()
        {
            var now = DateTime.Now;
            // запускать раз в сутки около 03:00
            if (now.Hour != 3) return;
            if (_lastScheduledRun.Date == now.Date) return;

            _lastScheduledRun = now;
            _ = Task.Run(async () =>
            {
                try
                {
                    var path = await BackupAsync();
                    Serilog.Log.Information("[Backup] Scheduled backup created: {Path}", path);
                    CleanupOld();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[Backup] Scheduled backup failed");
                }
            });
        }

        public async Task<string> BackupAsync(string? outputPath = null)
        {
            Directory.CreateDirectory(BackupsDirectory);

            var conn = ParseConnectionString(Constants.Database.GetConnectionString());
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dumpFile = Path.Combine(BackupsDirectory, $"backup_{timestamp}.sql");

            outputPath ??= Path.Combine(BackupsDirectory, $"backup_{timestamp}.zip");

            var pgDump = ResolveTool("pg_dump.exe");
            if (pgDump == null)
                throw new InvalidOperationException(
                    "Не найден pg_dump. Убедитесь, что PostgreSQL установлен и добавлен в PATH " +
                    "или находится в стандартной папке Program Files.");

            var psi = new ProcessStartInfo
            {
                FileName = pgDump,
                Arguments = $"-h {conn.Host} -p {conn.Port} -U {conn.Username} -d {conn.Database} -F p -f \"{dumpFile}\" --no-owner --no-privileges",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.EnvironmentVariables["PGPASSWORD"] = conn.Password;

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить pg_dump");

            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(dumpFile))
                throw new InvalidOperationException($"pg_dump завершился с кодом {process.ExitCode}: {stderr}");

            // Архивируем дамп в zip и удаляем .sql
            using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(dumpFile, Path.GetFileName(dumpFile), CompressionLevel.Optimal);
            }

            try { File.Delete(dumpFile); } catch { }

            return outputPath;
        }

        public async Task RestoreAsync(string backupPath)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("Файл резервной копии не найден", backupPath);

            var conn = ParseConnectionString(Constants.Database.GetConnectionString());
            var tempDir = Path.Combine(Path.GetTempPath(), $"servicedesk_restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            string sqlFile;
            try
            {
                if (backupPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(backupPath, tempDir);
                    sqlFile = Directory.EnumerateFiles(tempDir, "*.sql").FirstOrDefault()
                        ?? throw new InvalidOperationException("В архиве не найден .sql файл");
                }
                else
                {
                    sqlFile = backupPath;
                }

                var psql = ResolveTool("psql.exe")
                    ?? throw new InvalidOperationException(
                        "Не найден psql. Убедитесь, что PostgreSQL установлен.");

                var psi = new ProcessStartInfo
                {
                    FileName = psql,
                    Arguments = $"-h {conn.Host} -p {conn.Port} -U {conn.Username} -d {conn.Database} -f \"{sqlFile}\" --single-transaction --quiet",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                psi.EnvironmentVariables["PGPASSWORD"] = conn.Password;

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Не удалось запустить psql");

                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"psql завершился с кодом {process.ExitCode}: {stderr}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public List<BackupInfo> ListBackups()
        {
            if (!Directory.Exists(BackupsDirectory))
                return new List<BackupInfo>();

            return Directory.EnumerateFiles(BackupsDirectory, "backup_*.zip")
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new BackupInfo
                {
                    FullPath = f.FullName,
                    FileName = f.Name,
                    CreatedAt = f.LastWriteTime,
                    SizeBytes = f.Length
                })
                .ToList();
        }

        public void CleanupOld()
        {
            var all = ListBackups();
            if (all.Count <= MaxBackupsToKeep) return;

            foreach (var old in all.Skip(MaxBackupsToKeep))
            {
                try { File.Delete(old.FullPath); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[Backup] Cannot delete {File}", old.FullPath); }
            }
        }

        public static void DeleteBackup(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // ---------- helpers ----------

        private static string? ResolveTool(string toolFileName)
        {
            // PATH
            var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in envPath.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir.Trim(), toolFileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            // Стандартные пути установки PostgreSQL под Windows
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (Directory.Exists(pf))
            {
                try
                {
                    foreach (var pgRoot in Directory.EnumerateDirectories(pf, "PostgreSQL"))
                    {
                        foreach (var version in Directory.EnumerateDirectories(pgRoot))
                        {
                            var candidate = Path.Combine(version, "bin", toolFileName);
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private static (string Host, string Port, string Database, string Username, string Password) ParseConnectionString(string cs)
        {
            string host = "localhost", port = "5432", db = "", user = "", pwd = "";
            foreach (var part in cs.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim().ToLowerInvariant();
                var val = kv[1].Trim();
                switch (key)
                {
                    case "host": case "server": host = val; break;
                    case "port": port = val; break;
                    case "database": case "db": db = val; break;
                    case "username": case "user id": case "user": user = val; break;
                    case "password": pwd = val; break;
                }
            }
            return (host, port, db, user, pwd);
        }
    }

    public class BackupInfo
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
        public string SizeLabel
        {
            get
            {
                if (SizeBytes < 1024) return $"{SizeBytes} Б";
                if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024} КБ";
                return $"{SizeBytes / (1024.0 * 1024.0):F1} МБ";
            }
        }
    }
}
