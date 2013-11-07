using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Markup;
using System.Windows.Data;

namespace OCRLessonReport.Converters
{
    public abstract class MarkupExtensionConverterBase : MarkupExtension, IValueConverter
    {
        private static readonly Dictionary<Type, MarkupExtensionConverterBase> registeredConverters = new Dictionary<Type, MarkupExtensionConverterBase>();

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return GetOrCreateConverter();
        }

        public MarkupExtensionConverterBase GetOrCreateConverter()
        {
            MarkupExtensionConverterBase result;

            lock (registeredConverters)
            {
                registeredConverters.TryGetValue(GetType(), out result);
                if (result == null)
                {
                    result = (MarkupExtensionConverterBase)Activator.CreateInstance(GetType());
                    registeredConverters.Add(GetType(), result);
                }
            }

            return result;
        }

        public abstract object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture);

        public abstract object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture);

    }
}
