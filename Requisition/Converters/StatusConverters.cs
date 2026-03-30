using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace Requisition.Converters
{
    public class StatusIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // ใช้ glyph ของ Segoe MDL2 Assets (ปรับถ้าต้องการไอคอนอื่น)
            if (value is bool isActive)
                return isActive ? "\uE7E8" : "\uE7E8";
            return "\uE7E8";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
    }

    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isActive)
            {
                // สีเขียวเมื่อ active, สีเทาเมื่อ inactive
                var c = isActive ? Color.FromArgb(255, 16, 185, 129) : Color.FromArgb(255, 156, 163, 175);
                return new SolidColorBrush(c);
            }

            return new SolidColorBrush(Color.FromArgb(255, 156, 163, 175));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
    }

    public class StatusTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isActive)
                return isActive ? "ปิดการใช้งาน" : "เปิดการใช้งาน";
            return "เปลี่ยนสถานะ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
    }
}
