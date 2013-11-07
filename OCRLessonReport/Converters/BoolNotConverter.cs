using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OCRLessonReport.Converters
{
    public class BoolNotConverter:MarkupExtensionConverterBase
    {
        public override object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool? b = value as bool?;
            return b != true;
        }

        public override object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool? b = value as bool?;
            return b != true;
        }
    }
}
