using System.Windows;

namespace Secucode
{
    public partial class TeacherMainWindow : Window
    {
        private string teacherUsername;

        public TeacherMainWindow(string username)
        {
            InitializeComponent();
            teacherUsername = username;

            var success = TeacherFrame.Navigate(new TeacherTestsPage(teacherUsername));
            if (!success)
            {
                MessageBox.Show("Failed to load the Teacher Tests Page.", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
