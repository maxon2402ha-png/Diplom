using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Services
{
    [SupportedOSPlatform("windows")]
    public static class AttachmentService
    {
        public const long MaxFileSize = 25L * 1024 * 1024;

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".pdf", ".log", ".txt", ".docx", ".xlsx", ".zip"
        };

        public static (bool IsValid, string Error) ValidateFile(string filePath)
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return (false, "Файл не найден.");

            if (info.Length > MaxFileSize)
                return (false, "Файл слишком большой. Максимум 25 МБ.");

            var ext = Path.GetExtension(filePath);
            if (!AllowedExtensions.Contains(ext))
                return (false, "Тип файла не поддерживается. Разрешены: PNG, JPG, JPEG, PDF, LOG, TXT, DOCX, XLSX, ZIP.");

            if (!VerifyMagicNumber(filePath, ext.ToLowerInvariant()))
                return (false, "Содержимое файла не соответствует его расширению.");

            return (true, string.Empty);
        }

        private static bool VerifyMagicNumber(string filePath, string ext)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var h = new byte[8];
                var read = stream.Read(h, 0, h.Length);

                return ext switch
                {
                    ".png" => read >= 4 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47,
                    ".jpg" or ".jpeg" => read >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF,
                    ".pdf" => read >= 4 && h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46,
                    ".zip" or ".docx" or ".xlsx" => read >= 2 && h[0] == 0x50 && h[1] == 0x4B,
                    ".txt" or ".log" => true,
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        public static async Task<TicketAttachment> SaveAsync(
            string sourcePath, int ticketId, int uploadedByUserId, AppDbContext db)
        {
            var fileName = Path.GetFileName(sourcePath);
            var ext = Path.GetExtension(fileName);
            var storedName = $"{Guid.NewGuid()}_{fileName}";

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ServiceDesk", "Attachments", ticketId.ToString());

            Directory.CreateDirectory(dir);

            var destPath = Path.Combine(dir, storedName);

            await using (var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write))
            {
                await src.CopyToAsync(dst);
            }

            var info = new FileInfo(destPath);
            var attachment = new TicketAttachment
            {
                TicketId = ticketId,
                FileName = fileName,
                StoredFilePath = destPath,
                ContentType = GetContentType(ext),
                FileSize = info.Length,
                UploadedAt = DateTime.UtcNow,
                UploadedByUserId = uploadedByUserId
            };

            db.TicketAttachments.Add(attachment);
            await db.SaveChangesAsync();
            return attachment;
        }

        public static async Task DeleteAsync(TicketAttachment attachment, AppDbContext db)
        {
            if (File.Exists(attachment.StoredFilePath))
            {
                try { File.Delete(attachment.StoredFilePath); } catch { }
            }
            db.TicketAttachments.Remove(attachment);
            await db.SaveChangesAsync();
        }

        public static bool IsImage(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} КБ";
            return $"{bytes / (1024 * 1024)} МБ";
        }

        private static string GetContentType(string ext) => ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            ".txt" or ".log" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}
