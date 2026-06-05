using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using КР_Ханников.Core;
using КР_Ханников.Data;
using КР_Ханников.Services;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class ManageEmployeesControl : UserControl
    {
        private readonly AuthService _authService;
        private List<Employee> _allEmployees = new();
        private List<User> _allUsers = new();
        private string _searchText = string.Empty;

        public ManageEmployeesControl(AuthService authService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));

            Loaded += async (s, e) => await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                using var db = App.CreateDbContext();

                _allEmployees = await db.Employees
                    .Include(e => e.User)
                    .AsNoTracking()
                    .OrderBy(e => e.Name)
                    .ToListAsync();

                _allUsers = await db.Users
                    .AsNoTracking()
                    .OrderBy(u => u.Username)
                    .ToListAsync();

                ApplyFilter();
                LoadUsers();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            var filtered = _allEmployees.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.ToLower();
                filtered = filtered.Where(e =>
                    e.Name.ToLower().Contains(search) ||
                    (e.User != null && e.User.Username.ToLower().Contains(search)));
            }

            var list = filtered.ToList();
            if (EmployeesGrid != null)
            {
                EmployeesGrid.ItemsSource = list;
            }
        }

        private void LoadUsers()
        {
            var filtered = _allUsers.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.ToLower();
                filtered = filtered.Where(u =>
                    (u.Username != null && u.Username.ToLower().Contains(search)) ||
                    (u.Role != null && u.Role.ToLower().Contains(search)) ||
                    (u.Email != null && u.Email.ToLower().Contains(search)));
            }

            if (UsersGrid != null)
            {
                UsersGrid.ItemsSource = filtered.ToList();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _searchText = textBox.Text;
                ApplyFilter();
                LoadUsers();
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.Text = string.Empty;
            await LoadDataAsync();
        }

        private async void AddEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                                var addWindow = new AddEmployeeWindow();
                addWindow.Owner = Window.GetWindow(this);

                if (addWindow.ShowDialog() == true)
                {
                    await LoadDataAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия окна: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenStats_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBox.Show("Расширенная статистика по сотрудникам находится в разработке.\nИспользуйте вкладку 'Дашборд' для общей сводки.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть статистику: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EmployeesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void EmployeesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (EmployeesGrid.SelectedItem is Employee emp && emp.User != null)
            {
                EditUser(emp.User);
            }
        }

        private void UsersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UsersGrid.SelectedItem is User user)
            {
                EditUser(user);
            }
        }

        private async void DeleteEmployee_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Employee emp)
            {
                await DeleteUserLogicAsync(emp.UserId, emp.Name);
            }
        }

        private async void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is User user)
            {
                await DeleteUserLogicAsync(user.Id, user.Username);
            }
        }

        private async Task DeleteUserLogicAsync(int userIdToDelete, string displayName)
        {
            if (userIdToDelete == _authService.CurrentUser?.Id)
            {
                MessageBox.Show("Вы не можете удалить собственную учетную запись!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var db = App.CreateDbContext();
                var userToDelete = await db.Users.FindAsync(userIdToDelete);

                if (userToDelete != null && userToDelete.Role == "Admin" && _authService.CurrentUser?.Role != "Admin")
                {
                    MessageBox.Show("Удаление администраторов разрешено только другим администраторам.", "Ограничение", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            catch (Exception) { /* Игнорируем ошибку при проверке прав */ }

            var result = MessageBox.Show(
                $"Вы уверены, что хотите удалить пользователя '{displayName}'?\nЭто действие закроет ему доступ к системе, а его незакрытые тикеты будут переведены в общую очередь.",
                "Удаление пользователя",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using var db = App.CreateDbContext();

                    var user = await db.Users.FindAsync(userIdToDelete);
                    if (user == null) return;

                    var employee = await db.Employees.FirstOrDefaultAsync(emp => emp.UserId == userIdToDelete);
                    if (employee != null)
                    {
                        var activeTickets = await db.Tickets.Where(t => t.AssigneeEmployeeId == employee.Id && t.Status != Constants.TicketStatus.Closed).ToListAsync();
                        foreach (var t in activeTickets)
                        {
                            t.AssigneeEmployeeId = null;
                        }
                        db.Employees.Remove(employee);
                    }

                    var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == userIdToDelete);
                    if (client != null)
                    {
                        var clientTickets = await db.Tickets.Where(t => t.ClientId == client.Id && t.Status != Constants.TicketStatus.Closed).ToListAsync();
                        foreach (var t in clientTickets)
                        {
                            t.Status = Constants.TicketStatus.Closed;
                            t.ClosedAt = DateTime.UtcNow;
                        }
                        db.Clients.Remove(client);
                    }

                    db.Users.Remove(user);

                    db.AuditLogs.Add(new AuditLog
                    {
                        Username = _authService.CurrentUser?.Username ?? "Система",
                        Action = "Удаление пользователя",
                        Details = $"Удален аккаунт ID: {userIdToDelete} ({displayName})",
                        Timestamp = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при удалении: {ex.Message}\n\nВозможно, у пользователя есть связанные данные (например, комментарии), которые блокируют удаление.", "Ошибка SQL", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            int userId;
            string displayName;
            if (btn.DataContext is Employee emp)
            {
                userId = emp.UserId;
                displayName = emp.Name;
            }
            else if (btn.DataContext is User user)
            {
                userId = user.Id;
                displayName = user.Username;
            }
            else
            {
                return;
            }

            // Админ задаёт новый временный пароль (по умолчанию — сгенерированный надёжный).
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                $"Введите новый временный пароль для пользователя «{displayName}».\n" +
                "При следующем входе система потребует у пользователя сменить его.",
                "Сброс пароля",
                GenerateTempPassword());

            // Пустая строка — пользователь нажал «Отмена».
            if (string.IsNullOrWhiteSpace(input)) return;

            var newPassword = input.Trim();
            var (isValid, errors) = PasswordValidator.Validate(newPassword);
            if (!isValid)
            {
                MessageBox.Show(
                    "Пароль не соответствует требованиям:\n• " + string.Join("\n• ", errors),
                    "Некорректный пароль", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Защита от двойного клика на время операции.
            btn.IsEnabled = false;
            try
            {
                using var db = App.CreateDbContext();
                var user = await db.Users.FindAsync(userId);
                if (user == null)
                {
                    MessageBox.Show("Пользователь не найден.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                user.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(
                    newPassword, Constants.Validation.BcryptWorkFactor);
                user.MustChangePassword = true;     // принудительная смена при следующем входе
                user.FailedLoginAttempts = 0;       // снимаем возможную блокировку
                user.LockedUntil = null;

                db.AuditLogs.Add(new AuditLog
                {
                    Username = _authService.CurrentUser?.Username ?? "Система",
                    Action = "Сброс пароля",
                    Details = $"Сброшен пароль пользователя ID: {userId} ({displayName})",
                    Timestamp = DateTime.UtcNow
                });

                await db.SaveChangesAsync();

                MessageBox.Show(
                    $"Пароль пользователя «{displayName}» сброшен.\n\n" +
                    $"Временный пароль: {newPassword}\n\n" +
                    "Передайте его пользователю — при следующем входе потребуется задать новый пароль.",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сбросить пароль: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        // Генерирует надёжный временный пароль (заглавная, строчная, цифра, спецсимвол, длина 10).
        private static string GenerateTempPassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnpqrstuvwxyz";
            const string digits = "23456789";
            const string special = "!@#$%*";
            const string all = upper + lower + digits + special;

            var rnd = new Random();
            var chars = new System.Collections.Generic.List<char>
            {
                upper[rnd.Next(upper.Length)],
                lower[rnd.Next(lower.Length)],
                digits[rnd.Next(digits.Length)],
                special[rnd.Next(special.Length)]
            };
            while (chars.Count < 10)
                chars.Add(all[rnd.Next(all.Length)]);

            return new string(chars.OrderBy(_ => rnd.Next()).ToArray());
        }

        private void EditUser(User user)
        {
            try
            {
                var roles = new[] { "Admin", "Support", "Client" };
                var currentIndex = Array.IndexOf(roles, user.Role);

                var dialog = new Window
                {
                    Title = "Изменение роли",
                    Width = 300,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"))
                };

                var panel = new StackPanel { Margin = new Thickness(20) };

                panel.Children.Add(new TextBlock
                {
                    Text = $"Пользователь: {user.Username}",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = Brushes.Black
                });

                panel.Children.Add(new TextBlock
                {
                    Text = "Выберите роль:",
                    Margin = new Thickness(0, 0, 0, 5),
                    Foreground = Brushes.Gray
                });

                var comboBox = new ComboBox
                {
                    ItemsSource = roles,
                    SelectedIndex = currentIndex >= 0 ? currentIndex : 2,
                    Height = 32,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                panel.Children.Add(comboBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var cancelButton = new Button
                {
                    Content = "Отмена",
                    Width = 80,
                    Height = 32,
                    Margin = new Thickness(0, 0, 10, 0),
                    Background = Brushes.White,
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1)
                };
                cancelButton.Click += (s, ev) => dialog.DialogResult = false;

                var saveButton = new Button
                {
                    Content = "Сохранить",
                    Width = 80,
                    Height = 32,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontWeight = FontWeights.SemiBold
                };

                saveButton.Click += async (s, ev) =>
                {
                    try
                    {
                        using var db = App.CreateDbContext();
                        var dbUser = await db.Users.FindAsync(user.Id);
                        var newRole = comboBox.SelectedItem?.ToString() ?? "Client";

                        if (dbUser != null && dbUser.Role != newRole)
                        {
                            dbUser.Role = newRole;

                                                        if (newRole == "Admin" || newRole == "Support")
                            {
                                var existingEmp = await db.Employees.FirstOrDefaultAsync(e => e.UserId == dbUser.Id);
                                if (existingEmp == null)
                                {
                                    db.Employees.Add(new Employee
                                    {
                                        UserId = dbUser.Id,
                                        Name = dbUser.Username,
                                        Role = newRole,
                                        MaxActiveTickets = 5
                                    });
                                }
                                else
                                {
                                    existingEmp.Role = newRole;
                                }
                            }

                            await db.SaveChangesAsync();
                        }
                        dialog.DialogResult = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                buttonPanel.Children.Add(cancelButton);
                buttonPanel.Children.Add(saveButton);
                panel.Children.Add(buttonPanel);

                dialog.Content = panel;

                if (dialog.ShowDialog() == true)
                {
                    _ = LoadDataAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка редактирования: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}