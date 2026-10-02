using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Aura.Converters
{
    /// <summary>
    /// Converts a "#RRGGBB"/"#RGB" hex string to a SolidColorBrush
    /// </summary>
    public class HexToSolidColorBrushConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, string language)
        {
            if (value is string hex && TryParseHex(hex, out var color))
            {
                return new SolidColorBrush(color);
            }

            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        {
            throw new System.NotImplementedException();
        }

        private static bool TryParseHex(string hex, out Color color)
        {
            color = default;

            if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#')
            {
                return false;
            }

            var digits = hex[1..];
            if (digits.Length == 3)
            {
                digits = $"{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}";
            }

            if (digits.Length != 6)
            {
                return false;
            }

            try
            {
                var r = System.Convert.ToByte(digits[..2], 16);
                var g = System.Convert.ToByte(digits[2..4], 16);
                var b = System.Convert.ToByte(digits[4..6], 16);
                color = Color.FromArgb(255, r, g, b);
                return true;
            }
            catch (System.FormatException)
            {
                return false;
            }
        }
    }
}
