using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Secucode
{
    public partial class TestDetailsPage : Page
    {
        private readonly Test currentTest;

        public TestDetailsPage(Test test)
        {
            InitializeComponent();
            currentTest = test ?? throw new ArgumentNullException(nameof(test));
            DataContext = currentTest;

            // Log loaded questions
            if (currentTest.Questions?.Count > 0)
            {
                foreach (var question in currentTest.Questions)
                {
                    Debug.WriteLine("Question: " + question);
                }

                QuestionsListBox.ItemsSource = currentTest.Questions;
            }
            else
            {
                Debug.WriteLine("No questions available for this test.");
                QuestionsListBox.ItemsSource = null;
            }
        }

        private void OpenSubmissionsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SubmittedCodes");

                if (!Directory.Exists(baseFolder))
                {
                    MessageBox.Show("Submitted codes folder not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string searchPattern = $"Exam_{currentTest.Id}_";
                string matchedFolder = Directory.GetDirectories(baseFolder)
                    .FirstOrDefault(folder => Path.GetFileName(folder).StartsWith(searchPattern));

                if (!string.IsNullOrEmpty(matchedFolder))
                {
                    Process.Start("explorer.exe", matchedFolder);
                }
                else
                {
                    MessageBox.Show($"No submission folder found for Exam ID: {currentTest.Id}", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening folder: " + ex.Message);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService?.CanGoBack == true)
            {
                NavigationService.GoBack();
            }
        }

        private void OpenTestPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentTest != null)
            {
                var exam = new Exam
                {
                    Id = currentTest.Id,
                    Name = currentTest.Name,
                    Date = currentTest.Date,
                    TimeLimit = currentTest.TimeLimit
                };

                TestPage testPage = new TestPage(exam, -1); // -1 placeholder for studentId
                testPage.Show();
            }
            else
            {
                MessageBox.Show("No test selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
