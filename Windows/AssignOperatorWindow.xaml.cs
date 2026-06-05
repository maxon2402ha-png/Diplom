using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Windows;
using КР_Ханников.Core;
using КР_Ханников.Data;

namespace КР_Ханников.Windows
{
    public partial class AssignOperatorWindow : Window
    {
        private readonly AppDbContext _context;
        private readonly Ticket _ticket;

        // Выбранный оператор — назначение выполняет вызывающий код через TicketService.AssignAsync
        // (там же проверяется лимит нагрузки и пишется история).
        public int? SelectedEmployeeId { get; private set; }

        public AssignOperatorWindow(AppDbContext context, Ticket ticket)
        {
            InitializeComponent();

            _context = context ?? throw new ArgumentNullException(nameof(context));
            _ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));

            LoadEmployees();
            PreselectCurrentAssignee();
        }

        private void LoadEmployees()
        {
            try
            {
                var employees = _context.Employees
                    .Include(e => e.User)
                    .AsNoTracking()
                    .Where(e => e.User != null && e.User.Role == "Support")
                    .OrderBy(e => e.User.Username)
                    .ToList();

                EmployeeBox.ItemsSource = employees;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сотрудников: {ex.Message}");
            }
        }

        private void PreselectCurrentAssignee()
        {
            if (!_ticket.AssigneeEmployeeId.HasValue || EmployeeBox.Items.Count == 0) return;
            var current = EmployeeBox.Items.OfType<Employee>().FirstOrDefault(e => e.Id == _ticket.AssigneeEmployeeId.Value);
            if (current != null) EmployeeBox.SelectedItem = current;
        }

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            if (EmployeeBox.SelectedItem is not Employee selected)
            {
                MessageBox.Show("Выберите сотрудника.");
                return;
            }

            SelectedEmployeeId = selected.Id;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}