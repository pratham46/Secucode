using System.Windows;

namespace Secucode
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
        }

        private void btnTeacher_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var teacherLogin = new TeacherLoginWindow();
                teacherLogin.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open Teacher Login: " + ex.Message);
            }
        }

        private void btnStudent_Click(object sender, RoutedEventArgs e)
        {
            StudentLoginWindow studentLogin = new StudentLoginWindow();
            studentLogin.Show();
            this.Close();
        }
    }
}
