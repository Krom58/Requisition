using Microsoft.UI.Xaml.Data;
using System;

namespace Requisition.Converters
{
    public class ProductStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is bool b && !b) ? "Inactive" : "Active";
        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
