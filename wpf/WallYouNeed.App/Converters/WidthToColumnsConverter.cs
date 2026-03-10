using System;
using System.Globalization;
using System.Windows.Data;

namespace WallYouNeed.App.Converters
{
    public class WidthToColumnsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            const int maxColumns = 4;
            const int defaultColumns = 3;

            if (value is double width && parameter is string minWidth)
            {
                var minColumnWidth = double.Parse(minWidth);
                var calculatedColumns = (int)Math.Floor(width / minColumnWidth);
                return Math.Clamp(calculatedColumns, 1, maxColumns);
            }
            return defaultColumns;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 