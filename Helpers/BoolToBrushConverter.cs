using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Lyxie_desktop.Helpers
{
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolean && parameter is string colorString)
            {
                var colors = colorString.Split(',');
                if (colors.Length == 2)
                {
                    var trueColor = new SolidColorBrush(Color.Parse(colors[0]));
                    var falseColor = new SolidColorBrush(Color.Parse(colors[1]));
                    return boolean ? trueColor : falseColor;
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
