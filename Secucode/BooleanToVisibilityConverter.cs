using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Secucode
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter?.ToString()?.ToLower() == "invert";

            if (value is bool boolean)
            {
                return (invert ? !boolean : boolean) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (value is string str)
            {
                bool isEmpty = string.IsNullOrWhiteSpace(str);
                return (invert ? !isEmpty : isEmpty) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
