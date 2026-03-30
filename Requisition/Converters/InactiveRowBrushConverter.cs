using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace Requisition.Converters
{
    public class InactiveRowBrushConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && !b)
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 211, 211, 211));
            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
