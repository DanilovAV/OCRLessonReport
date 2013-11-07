using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Globalization;
using System.Windows;

namespace OCRLessonReport.Converters
{
    [ValueConversion(typeof(object), typeof(bool))]
    public class EnumToBoolConverter : MarkupExtensionConverterBase
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return DependencyProperty.UnsetValue;

            if (value.GetType() == parameter.GetType())
                return value.Equals(parameter);

            string strParameter = parameter.ToString().Trim();

            if (String.IsNullOrEmpty(strParameter))
                return DependencyProperty.UnsetValue;

            try
            {
                return Enum.Parse(value.GetType(), strParameter).Equals(value);
            }
            catch (ArgumentException)
            {
            }

            return DependencyProperty.UnsetValue;
        }

        public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null || targetType == null)
                return DependencyProperty.UnsetValue;

            bool v = System.Convert.ToBoolean(value);

            if (!v)
                return DependencyProperty.UnsetValue;

            if (targetType == parameter.GetType())
                return parameter;

            string strParameter = parameter.ToString().Trim();

            if (String.IsNullOrEmpty(strParameter))
                return DependencyProperty.UnsetValue;

            try
            {
                var cTargetType = Nullable.GetUnderlyingType(targetType);

                if (cTargetType != null)
                    targetType = cTargetType;

                return Enum.Parse(targetType, strParameter);
            }
            catch(ArgumentException)
            {
            }

            return DependencyProperty.UnsetValue;
        }
    }
}
