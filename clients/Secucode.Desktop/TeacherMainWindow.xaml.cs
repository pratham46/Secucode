using System.Windows;

namespace Secucode
{
    public partial class TeacherMainWindow : Window
    {
        public TeacherMainWindow(User teacher)
        {
            InitializeComponent();
            TeacherFrame.Navigate(new TeacherTestsPage(teacher));
        }
    }
}
