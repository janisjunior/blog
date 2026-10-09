using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WheelContentManager.Core;
namespace WheelContentManager.Desktop;
public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    public MainWindow(MainViewModel vm) { this.vm = vm; InitializeComponent(); DataContext = vm; }
    private void ApiKeyChanged(object sender, RoutedEventArgs e) => vm.ApiKey = ((PasswordBox)sender).Password;
    private void SmtpPasswordChanged(object sender, RoutedEventArgs e) => vm.SmtpPassword = ((PasswordBox)sender).Password;
}
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type type, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type type, object p, CultureInfo c) => value is not true;
}
public sealed class StatusConverter : IValueConverter
{
    public object Convert(object value, Type type, object p, CultureInfo c) => value is ArticleStatus s ? s.Label() : "";
    public object ConvertBack(object value, Type type, object p, CultureInfo c) => Binding.DoNothing;
}
public sealed class BrandConverter : IValueConverter
{
    public object Convert(object value, Type type, object p, CultureInfo c) => value is WheelBrand b ? b.Name() : "";
    public object ConvertBack(object value, Type type, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class NotificationStateConverter : IValueConverter
{
    public object Convert(object value, Type type, object p, CultureInfo c) => value?.ToString() switch { "Sent" => "Wysłano / potwierdzono odbiór", "Pending" => "Oczekuje na wysyłkę", "Sending" => "Wysyłka rozpoczęta", "Unknown" => "Niepewny wynik — sprawdź skrzynkę", _ => "Nieznany stan" };
    public object ConvertBack(object value, Type type, object p, CultureInfo c) => Binding.DoNothing;
}
public sealed class SourceFieldConverter : IValueConverter
{
    public object Convert(object value, Type type, object p, CultureInfo c) => value?.ToString() switch { "CarMake" => "Producent auta", "CarModel" => "Model auta", "CarDisplay" => "Nazwa konfiguracji", "CarModelGroup" => "Grupa modelu", "WheelModel" => "Model felg", "Finish" => "Wykończenie", "FrontSize" => "Rozmiar przód", "RearSize" => "Rozmiar tył", "ProductDetails" => "Dane produktu", "AvailableSizes" => "Dostępne rozmiary", "Certifications" => "Certyfikacja", "Diameter" => "Średnica", "FrontWidth" => "Szerokość przód", "RearWidth" => "Szerokość tył", _ => value?.ToString() ?? "" };
    public object ConvertBack(object value, Type type, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class LanguagesConverter : IValueConverter
{
    public object Convert(object value, Type type, object p, CultureInfo c) => value is IEnumerable<ArticleVersion> versions ? string.Join(" / ", versions.Select(x => x.Language).Distinct().Order()) : "Brak wersji";
    public object ConvertBack(object value, Type type, object p, CultureInfo c) => Binding.DoNothing;
}
