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

            if (AuthenticateUser(username, password, out User? teacher))
            {
                TeacherMainWindow teacherMainWindow = new TeacherMainWindow(teacher!);
                teacherMainWindow.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Invalid credentials. Please try again.", "Authentication Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool AuthenticateUser(string username, string password, out User? teacher)
        {
            teacher = null;
            try
            {
                using (MySqlConnection conn = Db.Open())
                {
                    string query = @"
                        SELECT id, password
                        FROM users
                        WHERE username = @username AND role = 'Teacher'";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read() && BCrypt.Net.BCrypt.Verify(password, reader.GetString("password")))
                            {
                                teacher = new User { Id = reader.GetInt32("id"), Username = username, Role = "Teacher" };
                                return true;
                            }
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
