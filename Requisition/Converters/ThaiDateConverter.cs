using System;
using Microsoft.UI.Xaml.Data;

namespace Requisition.Converters
{
    /// <summary>
    /// Converter สำหรับแปลง DateTime เป็นรูปแบบไทย (พ.ศ.) ใน XAML Binding
    /// </summary>
    public class ThaiDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // Prefer Date + time short format; falls back safely for nullable and strings.
            if (value == null) return "-";

            if (value is DateTime dt)
                return ThaiDateHelper.ToThaiDateTimeShortOrDefault(dt);

            if (value is DateTimeOffset dto)
                return ThaiDateHelper.ToThaiDateTimeShortOrDefault(dto.DateTime);

            if (value is string s && DateTime.TryParse(s, out var parsed))
                return ThaiDateHelper.ToThaiDateTimeShortOrDefault(parsed);

            // Fallback
            return value.ToString() ?? "-";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
    }
}
