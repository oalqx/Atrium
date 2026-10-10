using System;
using Atrium.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Atrium.App.Converters;

public sealed class ArtworkImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not Uri uri) return null;
        try { return new BitmapImage(uri); }
        catch (ArgumentException) { return null; }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class PlaytimeFormatterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int minutes)
        {
            if (minutes <= 0) return "Never played";
            if (minutes < 60) return $"{minutes} min";
            var hours = minutes / 60;
            var rem = minutes % 60;
            return rem > 0 ? $"{hours}h {rem}m" : $"{hours}h";
        }
        return "Never played";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class StoreBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is StoreKind store)
        {
            return store switch
            {
                StoreKind.Steam => "#1b2838",
                StoreKind.Epic => "#2a2a2a",
                StoreKind.Gog => "#5c2d91",
                StoreKind.Xbox => "#107c10",
                StoreKind.Local => "#3a3a3a",
                _ => "#444444"
            };
        }
        return "#333333";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class StoreNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is StoreKind store)
        {
            return store switch
            {
                StoreKind.Steam => "Steam",
                StoreKind.Epic => "Epic Games",
                StoreKind.Gog => "GOG",
                StoreKind.Xbox => "Xbox",
                StoreKind.Local => "Local PC",
                _ => store.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility v)
        {
            return v == Visibility.Visible;
        }
        return false;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class FavoriteBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GoldBrush = new(Color.FromArgb(255, 255, 215, 0));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromArgb(200, 200, 200, 200));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isFav && isFav)
        {
            return GoldBrush;
        }
        return GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
