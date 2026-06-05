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
            RegisterGlobalExceptionHandlers();
        }

        private void RegisterGlobalExceptionHandlers()
        {
            // Исключения UI-потока — приложение продолжает работу (e.Handled = true).
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Фатальные исключения фоновых потоков — логируем перед завершением процесса.
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var ex = args.ExceptionObject as Exception
                         ?? new Exception($"Неизвестная ошибка: {args.ExceptionObject}");
                LogUnhandled("Фоновый поток", ex);
            };

            // Несоблюдённые (unobserved) исключения задач — гасим, чтобы не уронить процесс.
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogUnhandled("Фоновая задача", args.Exception);
                args.SetObserved();
            };
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

            try
            {
                using (var context = CreateDbContext())
                {
                    InitializeDatabase(context);
                    EnsureAdminExists(context);
                    // Сидирование выполняем на пуле потоков, чтобы избежать
                    // async-over-sync дедлока на UI-потоке при пустой БД.
                    Task.Run(() => DbSeeder.SeedAsync(context)).GetAwaiter().GetResult();
                }

                _deadlineMonitor = new DeadlineMonitorService();
                _deadlineMonitor.Start();

                _backupService = new BackupService();
                _backupService.Start();

                var loginContext = CreateDbContext();
                var authService = new AuthService(loginContext);
                var loginWindow = new LoginWindow(loginContext, authService);
                loginWindow.Show();

                // Фоновое обучение модели запускается ПОСЛЕ показа окна
                // и ни при каких условиях не роняет приложение.
                StartBackgroundModelTraining();
            }
            catch (Exception ex) when (IsDatabaseUnavailable(ex))
            {
                LogUnhandled("Запуск: БД недоступна", ex);
                MessageBox.Show(
                    "Не удалось подключиться к базе данных.\n\n" +
                    "Проверьте, запущен ли сервер PostgreSQL, и параметры подключения, " +
                    "затем запустите приложение снова.",
                    "База данных недоступна",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
            }
            catch (Exception ex)
            {
                LogUnhandled("Запуск приложения", ex);
                ShowErrorDialog("Не удалось запустить приложение", ex);
                Shutdown();
            }
        }

        private static void InitializeDatabase(AppDbContext context)
        {
            try
            {
                // Применяем миграции: для новой БД создаётся актуальная схема,
                // для управляемой миграциями — применяются недостающие (включая RemoveEmailFeatures).
                context.Database.Migrate();
            }
            catch (Exception ex) when (!IsDatabaseUnavailable(ex))
            {
                // БД существует, но не велась миграциями (создана ранее через EnsureCreated()).
                // Её схема совместима с моделью — продолжаем работу, зафиксировав предупреждение.
                Log.Warning(ex, "Database.Migrate() пропущен: используется существующая схема БД");
            }
        }

        // Отличает «сервер БД недоступен» (PostgreSQL выключен/недостижим)
        // от ошибок уровня самой БД (на которые сервер ответил).
        private static bool IsDatabaseUnavailable(Exception? ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is Npgsql.PostgresException) return false;   // сервер ответил — он доступен
                if (e is Npgsql.NpgsqlException) return true;       // не удалось соединиться
                if (e is System.Net.Sockets.SocketException) return true;
                if (e is TimeoutException) return true;
            }
            return false;
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
            LogUnhandled("UI-поток", e.Exception);

            try
            {
                ShowErrorDialog("Произошла ошибка, приложение продолжит работу", e.Exception);
            }
            catch
            {
                try
                {
                    MessageBox.Show(
                        $"Произошла ошибка, приложение продолжит работу.\n\n{e.Exception.Message}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
            }

            // Не даём UI-исключению уронить приложение.
            e.Handled = true;
        }

        // Единая точка логирования необработанных исключений: Serilog + файл logs/error-{date}.log.
        private static void LogUnhandled(string source, Exception ex)
        {
            try { Log.Error(ex, "Необработанное исключение ({Source})", source); } catch { }
            WriteErrorLog(source, ex);
        }

        private static void WriteErrorLog(string source, Exception ex)
        {
            try
            {
                var logsPath = Constants.Database.GetLogsPath();
                Directory.CreateDirectory(logsPath);
                var file = Path.Combine(logsPath, $"error-{DateTime.Now:yyyy-MM-dd}.log");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("======================================================");
                sb.AppendLine($"Дата:      {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Источник:  {source}");
                sb.AppendLine($"Тип:       {ex.GetType().FullName}");
                sb.AppendLine($"Сообщение: {ex.Message}");
                sb.AppendLine("Stack trace:");
                sb.AppendLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"Внутреннее: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                    sb.AppendLine(ex.InnerException.StackTrace);
                }
                File.AppendAllText(file, sb.ToString());
            }
            catch { /* логирование не должно само бросать исключения */ }
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

        private static void StartBackgroundModelTraining()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using var trainingContext = CreateDbContext();
                    var classifier = new MlTicketClassifier();
                    classifier.TrainModels(trainingContext);
                }
                catch (Exception ex)
                {
                    // Любая ошибка фонового обучения только логируется и не всплывает наружу.
                    try { Log.Warning(ex, "Фоновое обучение модели завершилось ошибкой"); }
                    catch { }
                }
            });
        }

        public static AppDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(Constants.Database.GetConnectionString());
            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
