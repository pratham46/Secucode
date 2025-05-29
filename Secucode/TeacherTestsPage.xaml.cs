using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MySql.Data.MySqlClient;

namespace Secucode
{
    public partial class TeacherTestsPage : Page
    {
        private readonly string connectionString = "Server=localhost;Database=secucode;User Id=root;Password=root;";
        private readonly int teacherUserId;

        public TeacherTestsPage(string username)
        {
            InitializeComponent(); // Ensure this is called first

            // Get teacher's user ID based on username
            teacherUserId = GetUserIdByUsername(username);

            LoadUserDetails();
            LoadTeacherTests();
        }

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

        private void LoadUserDetails()
        {
            User currentUser = GetUserDetailsById(teacherUserId);
            DataContext = currentUser;
        }

        private User GetUserDetailsById(int userId)
        {
            User user = null;
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "SELECT username, role, pin FROM users WHERE id=@userId";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                user = new User
                                {
                                    Username = reader.GetString("username"),
                                    Role = reader.GetString("role"),
                                    Pin = reader.GetString("pin")
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading user details: " + ex.Message);
            }

            return user;
        }

        private void LoadTeacherTests()
        {
            List<Test> tests = new List<Test>();
            

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = @"
            SELECT e.id, e.name, e.created_by, e.date, e.time_limit, 
                   e.is_active,b.branch_name AS branch, c.class_name AS class, ba.batch_name AS batch, 
                   q.question_text
            FROM examinations e
            LEFT JOIN questions q ON e.id = q.examination_id
            LEFT JOIN branches b ON e.branch_id = b.id
            LEFT JOIN classes c ON e.class_id = c.id
            LEFT JOIN batches ba ON e.batch_id = ba.id
            WHERE e.created_by = @userId";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", teacherUserId);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int testId = reader.GetInt32("id");
                                string testName = reader.IsDBNull(reader.GetOrdinal("name")) ? string.Empty : reader.GetString("name");

                                if (string.IsNullOrEmpty(testName))
                                {
                                    continue;
                                }

                                Test test = tests.Find(t => t.Id == testId);
                                if (test == null)
                                {
                                    test = new Test
                                    {
                                        Id = testId,
                                        Name = testName,
                                        Date = reader.IsDBNull(reader.GetOrdinal("date")) ? string.Empty : reader.GetDateTime("date").ToShortDateString(),
                                        TimeLimit = reader.IsDBNull(reader.GetOrdinal("time_limit")) ? 0 : reader.GetInt32("time_limit"),
                                        CreatedBy = reader.GetInt32("created_by").ToString(),
                                        IsActive = reader.GetBoolean("is_active"),
                                        BranchName = reader.IsDBNull(reader.GetOrdinal("branch")) ? string.Empty : reader.GetString("branch"),
                                        ClassName = reader.IsDBNull(reader.GetOrdinal("class")) ? string.Empty : reader.GetString("class"),
                                        BatchName = reader.IsDBNull(reader.GetOrdinal("batch")) ? string.Empty : reader.GetString("batch"),
                                        Questions = new List<string>()
                                    };
                                    tests.Add(test);
                                }

                                if (!reader.IsDBNull(reader.GetOrdinal("question_text")))
                                {
                                    test.Questions.Add(reader.GetString("question_text"));
                                }
                            }
                        }
                    }
                }

                // Bind the data to the ListBox
                TestsListBox.ItemsSource = tests;

                // Toggle the visibility of the NoTestsMessage based on whether tests are available
                NoTestsMessage.Visibility = tests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading tests: " + ex.Message);
            }
        }

        
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {

            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                {
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }

        // Use this method to find TextBox properly inside the ListBox template when needed.
        private void AccessTextBoxInListBox()
        {
            foreach (var item in TestsListBox.Items)
            {
                ListBoxItem listBoxItem = TestsListBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (listBoxItem != null)
                {
                    ContentPresenter contentPresenter = FindVisualChild<ContentPresenter>(listBoxItem);
                    if (contentPresenter != null)
                    {
                        TextBox textBox = FindVisualChild<TextBox>(contentPresenter);
                        if (textBox != null)
                        {
                            string textValue = textBox.Text;
                            // Do something with the text value here
                        }
                    }
                }
            }
        }

        private void CreateNewTestButton_Click(object sender, RoutedEventArgs e)
        {
            TeacherExamFormWindow examFormWindow = new TeacherExamFormWindow(teacherUserId.ToString());
            examFormWindow.ShowDialog();
            LoadTeacherTests();
        }

        private void EditTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is Test test)
            {
                TeacherExamFormWindow examFormWindow = new TeacherExamFormWindow(teacherUserId.ToString(), test);
                examFormWindow.ShowDialog();
                LoadTeacherTests();
            }
        }

        private void DeleteTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is Test test)
            {
                try
                {
                    using (MySqlConnection conn = new MySqlConnection(connectionString))
                    {
                        conn.Open();
                        string deleteQuestionsQuery = "DELETE FROM questions WHERE examination_id=@examination_id";
                        using (MySqlCommand deleteQuestionsCmd = new MySqlCommand(deleteQuestionsQuery, conn))
                        {
                            deleteQuestionsCmd.Parameters.AddWithValue("@examination_id", test.Id);
                            deleteQuestionsCmd.ExecuteNonQuery();
                        }

                        string deleteExamQuery = "DELETE FROM examinations WHERE id=@id";
                        using (MySqlCommand deleteExamCmd = new MySqlCommand(deleteExamQuery, conn))
                        {
                            deleteExamCmd.Parameters.AddWithValue("@id", test.Id);
                            deleteExamCmd.ExecuteNonQuery();
                        }
                    }

                    LoadTeacherTests();
                    MessageBox.Show("Test deleted successfully.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error deleting test: " + ex.Message);
                }
            }
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (((Button)sender).DataContext is Test test)
                {
                    test.IsActive = !test.IsActive;

                    UpdateTestStatus(test);  // Save the updated status in the database

                    LoadTeacherTests();  // Refresh the UI to reflect the updated status

                    // Update the button content based on the new status
                    Button toggleButton = sender as Button;
                    if (toggleButton != null)
                    {
                        toggleButton.Content = test.IsActive ? "Deactivate" : "Activate";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred while toggling the test status: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTestStatus(Test test)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "UPDATE examinations SET is_active = @isActive WHERE id = @id";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@isActive", test.IsActive);
                        cmd.Parameters.AddWithValue("@id", test.Id);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred while updating the test status in the database: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            // Perform logout logic here
            MessageBox.Show("You have been logged out.");

            // Navigate back to login or close the current window
            Application.Current.Shutdown();  // Or navigate to the login page, depending on your application structure
        }

        private void TestsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TestsListBox.SelectedItem is Test selectedTest)
            {
                TestDetailsPage testDetailsPage = new TestDetailsPage(selectedTest);
                NavigationService?.Navigate(testDetailsPage);
                TestsListBox.SelectedItem = null;
            }
        }
    }
}

