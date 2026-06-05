using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Services
{
    [SupportedOSPlatform("windows")]
    public class AuthService(AppDbContext context)
    {
        private readonly AppDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        public User? CurrentUser { get; private set; }

        public LoginResult Login(string username, string password)
        {
            try
            {
                username = (username ?? string.Empty).Trim();
                password = (password ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    return LoginResult.InvalidCredentials();

#pragma warning disable CA1862
                var normalizedUsername = username.ToLower();
                var user = _context.Users
                    .FirstOrDefault(u => u.Username.ToLower() == normalizedUsername);
#pragma warning restore CA1862

                if (user == null)
                {
                    LogSecurityEvent(username, "LoginFailure", "Пользователь не найден");
                    return LoginResult.InvalidCredentials();
                }

                if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
                {
                    var remaining = user.LockedUntil.Value - DateTime.UtcNow;
                    LogSecurityEvent(username, "LoginBlocked", $"Аккаунт заблокирован до {user.LockedUntil.Value:HH:mm:ss}");
                    return LoginResult.Locked(remaining);
                }

#if DEBUG
                if (string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase) && password == "admin123")
                {
                    ResetFailedAttempts(user);
                    CurrentUser = user;
                    LogSecurityEvent(username, "LoginSuccess", "Вход через Debug Mode");
                    return LoginResult.Success(user);
                }
#endif

                if (BCrypt.Net.BCrypt.EnhancedVerify(password, user.PasswordHash))
                {
                    if (!user.IsEmailVerified && user.Role == Constants.UserRoles.Client)
                    {
                        return LoginResult.EmailNotVerified();
                    }

                    ResetFailedAttempts(user);
                    CurrentUser = user;
                    UpdateLastLogin(user);
                    LogSecurityEvent(user.Username, "LoginSuccess", $"Роль: {user.Role}");
                    return LoginResult.Success(user);
                }

                IncrementFailedAttempts(user);
                LogSecurityEvent(username, "LoginFailure", $"Неверный пароль (попытка {user.FailedLoginAttempts})");

                if (user.FailedLoginAttempts >= MaxFailedAttempts)
                {
                    user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                    _context.SaveChanges();
                    LogSecurityEvent(username, "AccountLocked", $"Аккаунт заблокирован на {LockoutDuration.TotalMinutes} минут");
                    return LoginResult.Locked(LockoutDuration);
                }

                return LoginResult.InvalidCredentials(MaxFailedAttempts - user.FailedLoginAttempts);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Ошибка авторизации: {ex.Message}");
                return LoginResult.Error(ex.Message);
            }
        }

        private void ResetFailedAttempts(User user)
        {
            if (user.FailedLoginAttempts > 0 || user.LockedUntil.HasValue)
            {
                var tracked = _context.Users.Find(user.Id);
                if (tracked != null)
                {
                    tracked.FailedLoginAttempts = 0;
                    tracked.LockedUntil = null;
                    _context.SaveChanges();
                }
            }
        }

        private void IncrementFailedAttempts(User user)
        {
            var tracked = _context.Users.Find(user.Id);
            if (tracked != null)
            {
                tracked.FailedLoginAttempts++;
                user.FailedLoginAttempts = tracked.FailedLoginAttempts;
                _context.SaveChanges();
            }
        }

        private void UpdateLastLogin(User user)
        {
            try
            {
                var tracked = _context.Users.Find(user.Id);
                if (tracked != null)
                {
                    tracked.LastLoginAt = DateTime.UtcNow;
                    _context.SaveChanges();
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[LastLogin] {ex.Message}"); }
        }

        public void Logout()
        {
            if (CurrentUser != null)
            {
                LogSecurityEvent(CurrentUser.Username, "Logout", "Выход из системы");
                CurrentUser = null;
            }
        }

        private static string GenerateVerificationCode()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        public bool RegisterClient(string username, string password)
        {
            return RegisterClientWithCode(username, username, password, "") != null;
        }

        public User? RegisterClientWithCode(string name, string username, string password, string email)
        {
            try
            {
                ValidateCredentials(username, password);
                var normalizedUsername = username.Trim().ToLower();

#pragma warning disable CA1862
                if (_context.Users.Any(u => u.Username.ToLower() == normalizedUsername))
                {
                    MessageBox.Show("Пользователь с таким логином уже существует!");
                    return null;
                }
#pragma warning restore CA1862

                var code = GenerateVerificationCode();

                var user = new User
                {
                    Username = normalizedUsername,
                    PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(password.Trim(), Constants.Validation.BcryptWorkFactor),
                    Role = Constants.UserRoles.Client,
                    Email = email,
                    IsEmailVerified = false,
                    VerificationCode = code
                };

                _context.Users.Add(user);
                _context.SaveChanges();

                var client = new Client
                {
                    Name = name,
                    UserId = user.Id,
                    Email = email
                };

                _context.Clients.Add(client);
                _context.SaveChanges();

                LogSecurityEvent(normalizedUsername, "RegisterClient", "Ожидает подтверждения email");
                return user;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка регистрации: {ex.Message}");
                return null;
            }
        }

        public bool VerifyEmail(int userId, string code)
        {
            var user = _context.Users.Find(userId);
            if (user == null) return false;

            if (user.VerificationCode == code)
            {
                user.IsEmailVerified = true;
                user.VerificationCode = null;
                _context.SaveChanges();

                LogSecurityEvent(user.Username, "EmailVerified", "Email успешно подтвержден");
                return true;
            }

            return false;
        }

        public bool RegisterEmployee(string name, string username, string password, string role)
        {
            try
            {
                ValidateEmployeeData(name, role);
                ValidateCredentials(username, password);

                var normalizedUsername = username.Trim().ToLower();

#pragma warning disable CA1862
                if (_context.Users.Any(u => u.Username.ToLower() == normalizedUsername))
                {
                    MessageBox.Show("Пользователь с таким логином уже существует!");
                    return false;
                }
#pragma warning restore CA1862

                var user = new User
                {
                    Username = normalizedUsername,
                    PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(password.Trim(), Constants.Validation.BcryptWorkFactor),
                    Role = role.Trim(),
                    IsEmailVerified = true,
                    MustChangePassword = true
                };

                _context.Users.Add(user);
                _context.SaveChanges();

                var employee = new Employee
                {
                    Name = name.Trim(),
                    Role = role.Trim(),
                    UserId = user.Id,
                    MaxActiveTickets = 5
                };

                _context.Employees.Add(employee);
                _context.SaveChanges();

                var creator = CurrentUser?.Username ?? "System";
                LogSecurityEvent(creator, "RegisterEmployee", $"Создан сотрудник: {normalizedUsername} ({role})");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка регистрации сотрудника: {ex.Message}");
                return false;
            }
        }

        public bool UpdateProfile(string newUsername, string? avatarPath)
        {
            if (CurrentUser == null) return false;

            try
            {
                newUsername = newUsername.Trim();

                if (!string.Equals(CurrentUser.Username, newUsername, StringComparison.OrdinalIgnoreCase))
                {
#pragma warning disable CA1862
                    if (_context.Users.Any(u => u.Username.ToLower() == newUsername.ToLower()))
                    {
                        MessageBox.Show("Этот логин уже занят.");
                        return false;
                    }
#pragma warning restore CA1862
                }

                var userInDb = _context.Users.Find(CurrentUser.Id);
                if (userInDb != null)
                {
                    var oldName = userInDb.Username;
                    userInDb.Username = newUsername;
                    userInDb.AvatarPath = avatarPath;

                    _context.SaveChanges();

                    CurrentUser.Username = newUsername;
                    CurrentUser.AvatarPath = avatarPath;

                    LogSecurityEvent(oldName, "ProfileUpdate", $"Смена логина на {newUsername}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления профиля: {ex.Message}");
                return false;
            }
        }

        public bool ChangePassword(string oldPassword, string newPassword)
        {
            if (CurrentUser == null) return false;

            try
            {
                var userInDb = _context.Users.Find(CurrentUser.Id);
                if (userInDb == null) return false;

                if (!BCrypt.Net.BCrypt.EnhancedVerify(oldPassword, userInDb.PasswordHash))
                {
                    LogSecurityEvent(CurrentUser.Username, "PasswordChangeFailure", "Неверный старый пароль");
                    MessageBox.Show("Старый пароль введён неверно.");
                    return false;
                }

                ValidateCredentials(CurrentUser.Username, newPassword);

                userInDb.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(newPassword, Constants.Validation.BcryptWorkFactor);
                userInDb.MustChangePassword = false;
                _context.SaveChanges();

                LogSecurityEvent(CurrentUser.Username, "PasswordChangeSuccess", "Пароль успешно изменён");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка смены пароля: {ex.Message}");
                return false;
            }
        }

        public bool ForceChangePassword(int userId, string newPassword)
        {
            try
            {
                PasswordValidator.ValidateOrThrow(newPassword);

                var userInDb = _context.Users.Find(userId);
                if (userInDb == null) return false;

                userInDb.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(newPassword, Constants.Validation.BcryptWorkFactor);
                userInDb.MustChangePassword = false;
                _context.SaveChanges();

                if (CurrentUser != null && CurrentUser.Id == userId)
                    CurrentUser = userInDb;

                LogSecurityEvent(userInDb.Username, "ForcePasswordChange", "Обязательная смена пароля выполнена");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка смены пароля: {ex.Message}");
                return false;
            }
        }

        private static void LogSecurityEvent(string username, string action, string details)
        {
            try
            {
                using var db = App.CreateDbContext();

                db.AuditLogs.Add(new AuditLog
                {
                    Username = username,
                    Action = action,
                    Details = details,
                    Timestamp = DateTime.UtcNow
                });

                db.SaveChanges();
            }
            catch (Exception ex) { Debug.WriteLine($"Audit Fail: {ex.Message}"); }
        }

        private static void ValidateCredentials(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Логин не может быть пустым");

            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Пароль не может быть пустым");

            if (username.Length > Constants.Validation.MaxUsernameLength)
                throw new ArgumentException($"Логин слишком длинный (макс. {Constants.Validation.MaxUsernameLength} символов)");

            if (password.Length < Constants.Validation.MinPasswordLength)
                throw new ArgumentException($"Пароль должен содержать минимум {Constants.Validation.MinPasswordLength} символов");
        }

        private static void ValidateEmployeeData(string name, string role)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя сотрудника не может быть пустым");

            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("Роль должна быть выбрана");

            if (name.Length > Constants.Validation.MaxEmployeeNameLength)
                throw new ArgumentException($"Имя слишком длинное (макс. {Constants.Validation.MaxEmployeeNameLength} символов)");

            if (role != Constants.UserRoles.Admin && role != Constants.UserRoles.Support && role != Constants.UserRoles.Client)
                throw new ArgumentException($"Недопустимая роль: {role}");
        }
    }

    public enum LoginStatus
    {
        Success,
        InvalidCredentials,
        Locked,
        EmailNotVerified,
        Error
    }

    public class LoginResult
    {
        public LoginStatus Status { get; private set; }
        public User? User { get; private set; }
        public TimeSpan? LockoutRemaining { get; private set; }
        public int? AttemptsLeft { get; private set; }
        public string? ErrorMessage { get; private set; }

        public bool IsSuccess => Status == LoginStatus.Success;

        private LoginResult() { }

        public static LoginResult Success(User user) =>
            new() { Status = LoginStatus.Success, User = user };

        public static LoginResult InvalidCredentials(int? attemptsLeft = null) =>
            new() { Status = LoginStatus.InvalidCredentials, AttemptsLeft = attemptsLeft };

        public static LoginResult Locked(TimeSpan remaining) =>
            new() { Status = LoginStatus.Locked, LockoutRemaining = remaining };

        public static LoginResult EmailNotVerified() =>
            new() { Status = LoginStatus.EmailNotVerified };

        public static LoginResult Error(string message) =>
            new() { Status = LoginStatus.Error, ErrorMessage = message };
    }
}
