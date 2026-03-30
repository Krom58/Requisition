using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Converters
{
    public class DateFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            DateTime dt;
            if (value is DateTime d)
                dt = d;
            else if (value is string s && DateTime.TryParse(s, out var parsed))
                dt = parsed;
            else
                return "";
            return dt.ToString("dd/MM/yyyy");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
