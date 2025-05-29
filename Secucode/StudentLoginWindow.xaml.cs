using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using MySql.Data.MySqlClient;

namespace Secucode
{
    public partial class StudentLoginWindow : Window
    {
        private StudentLoginViewModel viewModel;

        public StudentLoginWindow()
        {
            InitializeComponent();
            viewModel = new StudentLoginViewModel();
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
            string username = txtUsername.Text;
            string password = txtPassword.Password;
            string batchName = txtBatchName.Text;

            if (AuthenticateUser(username, password, batchName, out User loggedInUser))
            {
                // Pass the authenticated user object to the StudentDashboard
                StudentDashboard studentDashboard = new StudentDashboard(loggedInUser);
                studentDashboard.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Invalid credentials, please try again.");
            }
        }


        private bool AuthenticateUser(string username, string password, string batchName, out User authenticatedUser)
        {
            authenticatedUser = null;
            string connectionString = "Server=localhost;Database=secucode;User Id=root;Password=root;";

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    string query = @"
            SELECT u.username, u.password, b.branch_name AS branch, c.class_name AS class, bt.batch_name AS batch
            FROM users u
            JOIN branches b ON u.branch_id = b.id
            JOIN classes c ON u.class_id = c.id
            JOIN batches bt ON u.batch_id = bt.id
            WHERE u.username = @username AND bt.batch_name = @batchName AND u.role = 'Student';";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@batchName", batchName);

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string storedHashedPassword = reader.GetString("password");

                                // Verify entered password with stored hash
                                if (BCrypt.Net.BCrypt.Verify(password, storedHashedPassword))
                                {
                                    // Populate authenticatedUser with the data from the database
                                    authenticatedUser = new User
                                    {
                                        Username = reader.GetString("username"),
                                        Branch = reader.GetString("branch"),
                                        Class = reader.GetString("class"),
                                        Batch = reader.GetString("batch")
                                    };
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error connecting to database: " + ex.Message);
            }

            return false; // Return false if user not found or incorrect password
        }



        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }
    }
}