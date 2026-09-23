namespace SmartDiskCleaner.App.Converters;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SmartDiskCleaner.App.ViewModels;
using SmartDiskCleaner.Domain;

public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return SummaryViewModel.FormatBytes(bytes);
        }
        if (value is int intBytes)
        {
            return SummaryViewModel.FormatBytes(intBytes);
        }
        return "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(46, 125, 50)); // Reclaimable
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(230, 81, 0)); // Review
    private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(21, 101, 192)); // Large File
    private static readonly SolidColorBrush PurpleBrush = new(Color.FromRgb(106, 27, 154)); // Protected
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(117, 117, 117)); // Unknown
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(198, 40, 40)); // Warning

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CleanupCategory category)
        {
            return category switch
            {
                CleanupCategory.HighConfidenceReclaimable => GreenBrush,
                CleanupCategory.ReviewRecommended => OrangeBrush,
                CleanupCategory.LargeFileNotJunk => BlueBrush,
                CleanupCategory.Protected => PurpleBrush,
                CleanupCategory.Warning => RedBrush,
                _ => GrayBrush
            };
        }

        return GrayBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
