using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OCRLessonReport.Converters
{
    public class EnumToStringConverter : MarkupExtensionConverterBase
    {
        public override object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value.ToString();
        }

        public override object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
