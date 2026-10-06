using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MySql.Data.MySqlClient;

namespace Secucode
{
    public partial class TeacherTestsPage : Page
    {
        private readonly User teacher;

        public TeacherTestsPage(User teacher)
        {
            InitializeComponent();
            this.teacher = teacher;
            DataContext = teacher;
            LoadTeacherTests();
        }

        private void LoadTeacherTests()
        {
            var tests = new Dictionary<int, Test>();

            try
            {
                using (MySqlConnection conn = Db.Open())
                {
                    string query = @"
                        SELECT e.id, e.name, e.date, e.time_limit, e.is_active,
                               e.branch_id, e.class_id, e.batch_id,
                               u.username AS creator,
                               b.branch_name AS branch, c.class_name AS class, ba.batch_name AS batch,
                               q.question_text
                        FROM examinations e
                        LEFT JOIN users u ON e.created_by = u.id
                        LEFT JOIN questions q ON e.id = q.examination_id
                        LEFT JOIN branches b ON e.branch_id = b.id
                        LEFT JOIN classes c ON e.class_id = c.id
                        LEFT JOIN batches ba ON e.batch_id = ba.id
                        WHERE e.created_by = @userId
                        ORDER BY e.date DESC, e.id, q.id";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", teacher.Id);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int testId = reader.GetInt32("id");
                                if (!tests.TryGetValue(testId, out Test? test))
                                {
                                    test = new Test
                                    {
                                        Id = testId,
                                        Name = reader.GetString("name"),
                                        ExamDate = reader.GetDateTime("date"),
                                        TimeLimit = reader.GetInt32("time_limit"),
                                        CreatedBy = reader.IsDBNull(reader.GetOrdinal("creator")) ? "" : reader.GetString("creator"),
                                        IsActive = reader.GetBoolean("is_active"),
                                        BranchId = reader.GetInt32("branch_id"),
                                        ClassId = reader.GetInt32("class_id"),
                                        BatchId = reader.GetInt32("batch_id"),
                                        BranchName = reader.IsDBNull(reader.GetOrdinal("branch")) ? "" : reader.GetString("branch"),
                                        ClassName = reader.IsDBNull(reader.GetOrdinal("class")) ? "" : reader.GetString("class"),
                                        BatchName = reader.IsDBNull(reader.GetOrdinal("batch")) ? "" : reader.GetString("batch"),
                                    };
                                    tests.Add(testId, test);
                                }

                                if (!reader.IsDBNull(reader.GetOrdinal("question_text")))
                                    test.Questions.Add(reader.GetString("question_text"));
                            }
                        }
                    }
                }

                TestsListBox.ItemsSource = tests.Values;
                NoTestsMessage.Visibility = tests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading tests: " + ex.Message);
            }
        }

        private void CreateNewTestButton_Click(object sender, RoutedEventArgs e)
        {
            new TeacherExamFormWindow(teacher.Id).ShowDialog();
            LoadTeacherTests();
        }

        private void EditTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is Test test)
            {
                new TeacherExamFormWindow(teacher.Id, test).ShowDialog();
                LoadTeacherTests();
            }
        }

        private void DeleteTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(((Button)sender).DataContext is Test test))
                return;

            if (MessageBox.Show($"Delete \"{test.Name}\" with all its questions and submissions?", "Confirm Delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                // questions and submissions are removed by ON DELETE CASCADE
                using MySqlConnection conn = Db.Open();
                using MySqlCommand cmd = new MySqlCommand("DELETE FROM examinations WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@id", test.Id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error deleting test: " + ex.Message);
            }
            LoadTeacherTests();
        }

        private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(((Button)sender).DataContext is Test test))
                return;

            try
            {
                using MySqlConnection conn = Db.Open();
                using MySqlCommand cmd = new MySqlCommand("UPDATE examinations SET is_active = @isActive WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@isActive", !test.IsActive);
                cmd.Parameters.AddWithValue("@id", test.Id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error updating test status: " + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            LoadTeacherTests();
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            new MainWindow().Show();
            Window.GetWindow(this)?.Close();
        }

        private void TestsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TestsListBox.SelectedItem is Test selectedTest)
            {
                NavigationService?.Navigate(new TestDetailsPage(selectedTest));
                TestsListBox.SelectedItem = null;
            }
        }
    }
}
