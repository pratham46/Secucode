using System.Windows;

namespace Secucode
{
    public partial class TestWindow : Window
    {
        private readonly Test test;

        public TestWindow(Test test)
        {
            InitializeComponent();
            this.test = test;

            // Load test questions and details
            LoadTestDetails();
        }

        private void LoadTestDetails()
        {
            // Display test details in the UI
            lblTestName.Text = test.Name;
            lblTestDate.Text = test.Date;
            lblTestTimeLimit.Text = $"{test.TimeLimit} minutes";

            // Load the test questions into the UI
            QuestionsListBox.ItemsSource = test.Questions;
        }

        private void SubmitTestButton_Click(object sender, RoutedEventArgs e)
        {
            // Logic for submitting the test
            MessageBox.Show("Test Submitted!");
            this.Close();
        }
    }
}
