using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HltbDatasetPlugin;

public class HltbFieldConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var title = value as string ?? "";
        var fieldName = parameter as string ?? "";
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(fieldName))
            return "";

        try
        {
            if (HltbPlugin.DisplayCache.TryGetValue(title, out var fields) &&
                fields.TryGetValue(fieldName, out var val))
                return val;
        }
        catch { }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class HltbHasDataConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var title = value as string ?? "";
        if (string.IsNullOrEmpty(title)) return Visibility.Collapsed;

        try
        {
            if (HltbPlugin.DisplayCache.TryGetValue(title, out var fields) &&
                fields.Count > 0)
                return Visibility.Visible;
        }
        catch { }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
