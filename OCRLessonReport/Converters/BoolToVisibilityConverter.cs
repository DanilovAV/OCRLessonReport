using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace OCRLessonReport.Converters
{
    public class BoolToVisibilityConverter: MarkupExtensionConverterBase
    {
        public override object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool b = (bool)(value ?? false);
            if (parameter != null)
                b = !b;

            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public override object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
