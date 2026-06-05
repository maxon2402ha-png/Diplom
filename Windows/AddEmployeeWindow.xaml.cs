using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class AddEmployeeWindow : Window
    {
        public AddEmployeeWindow()
        {
            InitializeComponent();
            NameBox.Focus();
        }

        private void UsernameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
                        if (UsernameBox.Text.Contains(" "))
            {
                int caretIndex = UsernameBox.CaretIndex;
                UsernameBox.Text = UsernameBox.Text.Replace(" ", "");
                UsernameBox.CaretIndex = caretIndex > 0 ? caretIndex - 1 : 0;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;
                        var role = (RoleBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? Constants.UserRoles.Support;

            NameError.Visibility = Visibility.Collapsed;
            UsernameError.Visibility = Visibility.Collapsed;
            PasswordError.Visibility = Visibility.Collapsed;

            bool invalid = false;
            if (string.IsNullOrWhiteSpace(name))
            {
                NameError.Text = "Укажите ФИО сотрудника";
                NameError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (string.IsNullOrWhiteSpace(username))
            {
                UsernameError.Text = "Укажите логин";
                UsernameError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                PasswordError.Text = "Укажите пароль";
                PasswordError.Visibility = Visibility.Visible;
                invalid = true;
            }
            else if (password.Length < 6)
            {
                PasswordError.Text = "Минимум 6 символов";
                PasswordError.Visibility = Visibility.Visible;
                invalid = true;
            }
            if (invalid) return;

            try
            {
                using var db = App.CreateDbContext();

                if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
                {
                    UsernameError.Text = "Логин уже занят";
                    UsernameError.Visibility = Visibility.Visible;
                    UsernameBox.Focus();
                    return;
                }

                                var user = new User
                {
                    Username = username,
                    PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(password, 13),
                    Role = role,
                    IsEmailVerified = true,                     CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(user);
                await db.SaveChangesAsync(); 
                                var employee = new Employee
                {
                    UserId = user.Id,
                    Name = name,
                    Role = role,
                    MaxActiveTickets = 5                 };

                db.Employees.Add(employee);

                                db.AuditLogs.Add(new AuditLog
                {
                    Username = "System",
                    Action = "Создание сотрудника",
                    Details = $"Создан новый оператор '{name}' с логином '{username}' ({role})",
                    Timestamp = DateTime.UtcNow
                });

                await db.SaveChangesAsync();

                MessageBox.Show($"Сотрудник {name} успешно добавлен в систему и может приступить к работе!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении в базу данных:\n{ex.Message}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}