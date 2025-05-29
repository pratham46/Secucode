using System;
using System.Windows;
using System.Windows.Controls;
using MySql.Data.MySqlClient;
using BCrypt.Net;

namespace Secucode
{
    public partial class TeacherLoginWindow : Window
    {
        private TeacherLoginViewModel viewModel;

        public TeacherLoginWindow()
        {
            InitializeComponent();
            viewModel = new TeacherLoginViewModel();
            DataContext = viewModel;
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                viewModel.IsPasswordEmpty = string.IsNullOrEmpty(passwordBox.Password);
            }
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both username and password.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (AuthenticateUser(username, password))
            {
                TeacherMainWindow teacherMainWindow = new TeacherMainWindow(username);
                teacherMainWindow.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Invalid credentials. Please try again.", "Authentication Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool AuthenticateUser(string username, string password)
        {
            string connectionString = "Server=localhost;Database=secucode;User Id=root;Password=root;";

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT password 
                        FROM users 
                        WHERE username = @username AND role = 'Teacher'";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        object result = cmd.ExecuteScalar();

                        if (result != null)
                        {
                            string storedHashedPassword = result.ToString();
                            return BCrypt.Net.BCrypt.Verify(password, storedHashedPassword);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return false;
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }
    }
}
