using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Serilog;
using КР_Ханников.Core;
using КР_Ханников.Services;
using КР_Ханников.Data;
using КР_Ханников.Services;
using КР_Ханников.Windows;

namespace КР_Ханников
{
    [SupportedOSPlatform("windows")]
    public partial class App : Application
    {
        private static DeadlineMonitorService? _deadlineMonitor;
        private static BackupService? _backupService;

        public App()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            ConfigureLogging();
        }

        private static void ConfigureLogging()
        {
            var logsPath = Constants.Database.GetLogsPath();
            Directory.CreateDirectory(logsPath);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Warning()
                .WriteTo.File(
                    path: Path.Combine(logsPath, "errors-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var culture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            base.OnStartup(e);
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            try
            {
                using (var context = CreateDbContext())
                {
                    context.Database.EnsureCreated();
                    EnsureAdminExists(context);
                    // Run async seeding on a thread-pool thread to avoid an async-over-sync
                    // deadlock on the WPF UI thread when seeding a fresh/empty database.
                    Task.Run(() => DbSeeder.SeedAsync(context)).GetAwaiter().GetResult();
                }

                Task.Run(() =>
                {
                    try
                    {
                        using var mlContext = CreateDbContext();
                        var classifier = new MlTicketClassifier();
                        classifier.TrainModels(mlContext);
                        Debug.WriteLine("[ML] Модель успешно обучена на исторических данных!");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[ML] Ошибка обучения модели");
                        Debug.WriteLine($"[ML Error] Ошибка обучения: {ex.Message}");
                    }
                });

                _deadlineMonitor = new DeadlineMonitorService();
                _deadlineMonitor.Start();

                _backupService = new BackupService();
                _backupService.Start();

                var loginContext = CreateDbContext();
                var authService = new AuthService(loginContext);
                var loginWindow = new LoginWindow(loginContext, authService);
                loginWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Критическая ошибка при запуске приложения");
                ShowErrorDialog("Не удалось запустить приложение", ex);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _deadlineMonitor?.Dispose();
            _backupService?.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        private static void EnsureAdminExists(AppDbContext context)
        {
            try
            {
                string adminUsername = "admin";
                string defaultPassword = "admin123";

                var adminUser = context.Users.FirstOrDefault(u => u.Username == adminUsername);

                if (adminUser != null)
                {
                    if (!BCrypt.Net.BCrypt.EnhancedVerify(defaultPassword, adminUser.PasswordHash))
                    {
                        adminUser.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(defaultPassword, 13);
                    }

                    adminUser.Role = Constants.UserRoles.Admin;

                    var emp = context.Employees.FirstOrDefault(e => e.UserId == adminUser.Id);
                    if (emp == null)
                    {
                        context.Employees.Add(new Employee
                        {
                            Name = "Главный Администратор",
                            UserId = adminUser.Id,
                            Role = Constants.UserRoles.Admin,
                            MaxActiveTickets = 999
                        });
                    }
                    else
                    {
                        emp.Role = Constants.UserRoles.Admin;
                    }
                }
                else
                {
                    var newAdmin = new User
                    {
                        Username = adminUsername,
                        PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(defaultPassword, 13),
                        Role = Constants.UserRoles.Admin,
                        IsEmailVerified = true,
                        MustChangePassword = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Users.Add(newAdmin);
                    context.SaveChanges();

                    context.Employees.Add(new Employee
                    {
                        Name = "Главный Администратор",
                        UserId = newAdmin.Id,
                        Role = Constants.UserRoles.Admin,
                        MaxActiveTickets = 999
                    });
                }

                context.SaveChanges();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Ошибка при проверке/создании администратора");
                Debug.WriteLine($"[Admin Check Error] {ex.Message}");
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Необработанное исключение в UI-потоке");

            try
            {
                ShowErrorDialog("В приложении произошла непредвиденная ошибка", e.Exception);
            }
            catch
            {
                MessageBox.Show(
                    $"Критическая ошибка:\n{e.Exception.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            e.Handled = true;
        }

        private static void ShowErrorDialog(string summary, Exception ex)
        {
            var details = BuildErrorDetails(ex);
            var logPath = Constants.Database.GetLogsPath();

            var dialog = new ErrorDetailsWindow(summary, details, logPath);
            dialog.ShowDialog();
        }

        private static string BuildErrorDetails(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Тип: {ex.GetType().FullName}");
            sb.AppendLine($"Сообщение: {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("Stack trace:");
            sb.AppendLine(ex.StackTrace);

            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("Внутреннее исключение:");
                sb.AppendLine($"  Тип: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"  Сообщение: {ex.InnerException.Message}");
                sb.AppendLine(ex.InnerException.StackTrace);
            }

            return sb.ToString();
        }

        public static AppDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(Constants.Database.GetConnectionString());
            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
