using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Secucode
{
    public class TeacherLoginViewModel : INotifyPropertyChanged
    {
        private bool isPasswordEmpty = true;

        public bool IsPasswordEmpty
        {
            get => isPasswordEmpty;
            set
            {
                if (isPasswordEmpty != value)
                {
                    isPasswordEmpty = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
