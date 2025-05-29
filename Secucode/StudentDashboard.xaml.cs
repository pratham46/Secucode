using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using MySql.Data.MySqlClient;

namespace Secucode
{
    public partial class StudentDashboard : Window
    {
        private readonly string connectionString = "Server=127.0.0.1;Database=secucode;User Id=root;Password=root;";
        private readonly string studentBranch;
        private readonly string studentClass;
        private readonly string studentBatch;
        private readonly int studentUserId; // To keep track of the student user ID

        public StudentDashboard(User loggedInUser)
        {
            InitializeComponent();

            // Assign branch, class, and batch details from the logged-in user
            studentBranch = loggedInUser.Branch;
            studentClass = loggedInUser.Class;
            studentBatch = loggedInUser.Batch;
            studentUserId = GetUserIdByUsername(loggedInUser.Username); // Get the student ID based on username

            LoadUpcomingExams();
        }

        // Fetch and display upcoming exams based on the student's branch, class, and batch
        private void LoadUpcomingExams()
        {
            List<Exam> upcomingExams = new List<Exam>();

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = @"
                SELECT e.id, e.name, e.date, e.time_limit
                FROM examinations e
                WHERE e.branch_id = (SELECT id FROM branches WHERE branch_name = @branchName)
                AND e.class_id = (SELECT id FROM classes WHERE class_name = @className)
                AND e.batch_id = (SELECT id FROM batches WHERE batch_name = @batchName)
                AND e.date >= CURDATE()
                AND e.is_active = 1"; // Only include active tests

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@branchName", studentBranch); // Using student's branch name
                        cmd.Parameters.AddWithValue("@className", studentClass);   // Using student's class name
                        cmd.Parameters.AddWithValue("@batchName", studentBatch);   // Using student's batch name

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                upcomingExams.Add(new Exam
                                {
                                    Id = reader.GetInt32("id"),
                                    Name = reader.GetString("name"),
                                    Date = reader.GetDateTime("date").ToShortDateString(),
                                    TimeLimit = reader.GetInt32("time_limit")
                                });
                            }
                        }
                    }
                }

                // Bind the data to the ListBox
                UpcomingExamsList.ItemsSource = upcomingExams;
                NoExamsText.Visibility = upcomingExams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading upcoming exams: " + ex.Message);
            }
        }


        // Event handler when a test is selected
        private void ExamsListBox_SelectionChanged(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element && element.DataContext is Exam selectedExam)
            {
                string studentPin = GetPinByUserId(studentUserId);
                PinVerificationWindow pinWindow = new PinVerificationWindow(studentPin);
                bool? result = pinWindow.ShowDialog();

                if (result == true && pinWindow.IsPinVerified)
                {
                    StudentTestDetailsPage testDetailsPage = new StudentTestDetailsPage(selectedExam, studentUserId);
                    Window fullScreenWindow = new Window
                    {
                        Content = testDetailsPage,
                        WindowState = WindowState.Maximized,
                        WindowStyle = WindowStyle.None,
                        Title = "Test Details"
                    };
                    fullScreenWindow.ShowDialog();
                }
                else
                {
                    MessageBox.Show("PIN verification failed. You cannot access this test.", "Verification Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }





        // Helper function to get the user's PIN from the database by user ID
        private string GetPinByUserId(int userId)
        {
            string userPin = null;

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "SELECT pin FROM users WHERE id = @userId";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            userPin = result.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error retrieving PIN: " + ex.Message);
            }

            return userPin;
        }

        // Helper function to get the user's ID based on their username
        private int GetUserIdByUsername(string username)
        {
            int userId = 0;
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "SELECT id FROM users WHERE username=@username";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            userId = Convert.ToInt32(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error retrieving user ID: " + ex.Message);
            }

            return userId;
        }

        // Logout button click event
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            // Logic to log out and return to login screen
            this.Close();
        }
    }

    // Supporting Exam class
    public class Exam
    {
        public int Id { get; set; } // Add an ID for exam identification
        public string Name { get; set; }
        public string Date { get; set; }
        public int TimeLimit { get; set; }
    }
}
