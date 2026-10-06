using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using MySql.Data.MySqlClient;

namespace Secucode
{
    public partial class StudentDashboard : Window
    {
        private readonly User student;

        public StudentDashboard(User loggedInUser)
        {
            InitializeComponent();
            student = loggedInUser;
            LoadUpcomingExams();
        }

        // Active exams for the student's branch/class/batch, today or later, not yet submitted.
        private void LoadUpcomingExams()
        {
            List<Exam> upcomingExams = new List<Exam>();

            try
            {
                using (MySqlConnection conn = Db.Open())
                {
                    string query = @"
                        SELECT e.id, e.name, e.date, e.time_limit
                        FROM examinations e
                        JOIN users u ON u.id = @userId
                            AND e.branch_id = u.branch_id
                            AND e.class_id = u.class_id
                            AND e.batch_id = u.batch_id
                        WHERE e.date >= CURDATE()
                          AND e.is_active = 1
                          AND NOT EXISTS (SELECT 1 FROM submissions s WHERE s.examination_id = e.id AND s.user_id = @userId)
                        ORDER BY e.date";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", student.Id);

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                upcomingExams.Add(new Exam
                                {
                                    Id = reader.GetInt32("id"),
                                    Name = reader.GetString("name"),
                                    ExamDate = reader.GetDateTime("date"),
                                    TimeLimit = reader.GetInt32("time_limit")
                                });
                            }
                        }
                    }
                }

                UpcomingExamsList.ItemsSource = upcomingExams;
                NoExamsText.Visibility = upcomingExams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading upcoming exams: " + ex.Message);
            }
        }

        private void ExamsListBox_SelectionChanged(object sender, MouseButtonEventArgs e)
        {
            if (!(e.OriginalSource is FrameworkElement element && element.DataContext is Exam selectedExam))
                return;

            if (selectedExam.ExamDate.Date != DateTime.Today)
            {
                MessageBox.Show($"This exam opens on {selectedExam.Date}.", "Not Yet Available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? studentPin = GetPin(student.Id);
            if (string.IsNullOrEmpty(studentPin))
            {
                MessageBox.Show("No PIN is set for your account. Ask your teacher to set one.", "PIN Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PinVerificationWindow pinWindow = new PinVerificationWindow(studentPin) { Owner = this };
            if (pinWindow.ShowDialog() != true)
                return;

            Window fullScreenWindow = new Window
            {
                Content = new StudentTestDetailsPage(selectedExam, student.Id),
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None,
                Title = "Test Details"
            };
            fullScreenWindow.ShowDialog(); // returns after the test (if started) is finished

            LoadUpcomingExams();
        }

        private static string? GetPin(int userId)
        {
            try
            {
                using MySqlConnection conn = Db.Open();
                using MySqlCommand cmd = new MySqlCommand("SELECT pin FROM users WHERE id = @userId", conn);
                cmd.Parameters.AddWithValue("@userId", userId);
                return cmd.ExecuteScalar() as string;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error retrieving PIN: " + ex.Message);
                return null;
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            new MainWindow().Show();
            Close();
        }
    }
}
