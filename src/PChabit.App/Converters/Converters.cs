using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace PChabit.App.Converters;

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class DateToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime date)
        {
            if (date.Date == DateTime.Today)
                return "今天";
            if (date.Date == DateTime.Today.AddDays(-1))
                return "昨天";
            return date.ToString("yyyy年MM月dd日 dddd");
        }
        return string.Empty;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hexColor && !string.IsNullOrEmpty(hexColor))
        {
            var color = Microsoft.UI.Colors.Transparent;
            if (hexColor.StartsWith("#") && hexColor.Length == 7)
            {
                var r = System.Convert.ToByte(hexColor.Substring(1, 2), 16);
                var g = System.Convert.ToByte(hexColor.Substring(3, 2), 16);
                var b = System.Convert.ToByte(hexColor.Substring(5, 2), 16);
                color = Microsoft.UI.ColorHelper.FromArgb(255, r, g, b);
            }
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class FirstLetterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string str && !string.IsNullOrEmpty(str))
        {
            return str[0].ToString().ToUpper();
        }
        return "?";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class HoursToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int hours)
        {
            return $"{hours}h";
        }
        return "0h";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class TrendColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string direction)
        {
            return direction.ToLower() switch
            {
                "up" => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 124, 16)),
                "down" => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 209, 52, 56)),
                _ => new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class InsightBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string type)
        {
            var color = type.ToLower() switch
            {
                "success" => Microsoft.UI.ColorHelper.FromArgb(30, 16, 124, 16),
                "warning" => Microsoft.UI.ColorHelper.FromArgb(30, 255, 140, 0),
                "info" => Microsoft.UI.ColorHelper.FromArgb(30, 0, 120, 212),
                _ => Microsoft.UI.ColorHelper.FromArgb(30, 107, 114, 128)
            };
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class PercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double percentage)
        {
            return $"{percentage:F1}%";
        }
        return "0%";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hexColor && !string.IsNullOrEmpty(hexColor))
        {
            if (hexColor.StartsWith("#") && hexColor.Length == 7)
            {
                var r = System.Convert.ToByte(hexColor.Substring(1, 2), 16);
                var g = System.Convert.ToByte(hexColor.Substring(3, 2), 16);
                var b = System.Convert.ToByte(hexColor.Substring(5, 2), 16);
                return Microsoft.UI.ColorHelper.FromArgb(255, r, g, b);
            }
        }
        return Microsoft.UI.Colors.Gray;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

public class IntToProgramCountTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count == 1 ? "1个程序" : $"{count}个程序";
        }
        return "0个程序";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class ColorSelectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string currentColor && parameter is string selectedColor)
        {
            return string.Equals(currentColor, selectedColor, StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Microsoft.UI.Colors.White)
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class InverseCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class CountToEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0;
        }
        return false;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class SelectedCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0 ? $"删除选中 ({count})" : "删除选中";
        }
        return "删除选中";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class SystemCategoryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isSystem && isSystem)
        {
            return "系统";
        }
        return string.Empty;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

public class TimeSpanToReadableConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}小时{ts.Minutes}分钟";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}分钟";
            return $"{ts.Seconds}秒";
        }
        return "0秒";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class DoubleToScoreConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double score)
        {
            return $"{score:F1}";
        }
        return "0.0";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class ActivityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double score)
        {
            if (score < 0)
            {
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            
            var normalizedScore = Math.Min(score / 100.0, 1.0);
            
            var color = normalizedScore switch
            {
                0 => Microsoft.UI.ColorHelper.FromArgb(255, 26, 26, 46),
                < 0.25 => Microsoft.UI.ColorHelper.FromArgb(255, 22, 33, 62),
                < 0.5 => Microsoft.UI.ColorHelper.FromArgb(255, 15, 52, 96),
                < 0.75 => Microsoft.UI.ColorHelper.FromArgb(255, 233, 69, 96),
                _ => Microsoft.UI.ColorHelper.FromArgb(255, 255, 107, 107)
            };
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 26, 26, 46));
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class ActivityToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double score)
        {
            if (score < 0)
            {
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            
            var normalizedScore = Math.Min(score / 100.0, 1.0);
            
            var color = normalizedScore switch
            {
                < 0.5 => Microsoft.UI.Colors.White,
                _ => Microsoft.UI.Colors.White
            };
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.White);
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class DateToDetailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime date)
        {
            if (date == DateTime.MinValue)
            {
                return string.Empty;
            }
            return date.ToString("MM 月 dd 日 dddd");
        }
        return string.Empty;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class HourToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int hour)
        {
            return $"{hour:D2}:00";
        }
        return "00:00";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
