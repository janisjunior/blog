using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WheelContentManager.Core;
using WheelContentManager.Infrastructure;
namespace WheelContentManager.Desktop;
public partial class MainViewModel : ObservableObject
{
    private readonly ContentService content;
    private readonly AppPaths paths;
    private readonly IServiceProvider services;
    private CancellationTokenSource? work;
    private string? weeklyBatchKey;
    private string? weeklyMailState;
    public ObservableCollection<Gallery> Galleries { get; } = [];
    public ICollectionView GalleryView { get; }
    public WheelBrand[] Brands { get; } = Enum.GetValues<WheelBrand>();
    public ObservableCollection<WeeklyPromptItem> WeeklyItems { get; } = [];
    [ObservableProperty] private string weeklyStatus = "Paczki tygodniowe: trwa sprawdzanie.";
    [ObservableProperty] private string weeklyBatchLabel = "Paczki tygodniowe — piątek 12:00 (Polska)";
    [ObservableProperty] private bool backgroundBusy;
    public bool CanCancel => Busy || BackgroundBusy;
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanCancel)); SendWeeklyMailCommand.NotifyCanExecuteChanged(); }
    partial void OnBackgroundBusyChanged(bool value) { OnPropertyChanged(nameof(CanCancel)); SendWeeklyMailCommand.NotifyCanExecuteChanged(); }
    public event Action? CancelBackgroundRequested;
    public event Action? ResumeBackgroundRequested;
    [ObservableProperty] private WheelBrand selectedBrand;
    [ObservableProperty] private Gallery? selectedGallery;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string status = "Wybierz markę i galerię.";
    [ObservableProperty] private string lastChecked = "Lista nie została jeszcze sprawdzona.";
    [ObservableProperty] private string outputFolder = "";
    [ObservableProperty] private string prompt = "";
    [ObservableProperty] private GalleryPromptPackage? package;
    public string BrandTitle => SelectedBrand.Name();
    public string UsageButtonLabel => SelectedGallery?.PromptUsed == true ? "Cofnij oznaczenie wykorzystania" : "Oznacz galerię jako wykorzystaną";
    public string CountLabel => $"{GalleryView.Cast<Gallery>().Count()} dostępnych galerii";
    partial void OnSearchChanged(string value) { GalleryView.Refresh(); OnPropertyChanged(nameof(CountLabel)); }
    partial void OnSelectedGalleryChanged(Gallery? value) { Prompt = ""; Package = null; OnPropertyChanged(nameof(UsageButtonLabel)); }
    public MainViewModel(IServiceProvider services)
    {
        this.services = services;
        content = services.GetRequiredService<ContentService>(); paths = services.GetRequiredService<AppPaths>();
        OutputFolder = Path.Combine(paths.Exports, "ChatGPT");
        GalleryView = CollectionViewSource.GetDefaultView(Galleries);
        GalleryView.SortDescriptions.Add(new(nameof(Gallery.PromptUsed), ListSortDirection.Ascending));
        GalleryView.SortDescriptions.Add(new(nameof(Gallery.ListingOrder), ListSortDirection.Ascending));
        GalleryView.Filter = item => item is Gallery g && Search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => g.Display.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }
    public async Task LoadWeeklyAsync()
    {
        var batches = await services.GetRequiredService<WeeklyPromptService>().BatchesAsync();
        var batch = batches.Where(b => b.MailState != "Sent").OrderBy(b => b.Due).FirstOrDefault() ?? batches.FirstOrDefault();
        weeklyBatchKey = batch?.Key; weeklyMailState = batch?.MailState;
        WeeklyBatchLabel = batch == null ? "Paczki tygodniowe — piątek 12:00 (Polska)" : "Paczki na " + batch.Key + " — piątek 12:00 (Polska)";
        WeeklyItems.Clear(); if (batch != null) foreach (var item in batch.Items.Where(i => i.Package != null)) WeeklyItems.Add(item);
        SendWeeklyMailCommand.NotifyCanExecuteChanged();
        var cached = await content.CachedPromptGalleriesAsync(SelectedBrand);
        if (Galleries.Count == 0) { SetList(cached.Galleries); return; }
        var selectedId = SelectedGallery?.Id; var previousPrompt = Prompt; var previousPackage = Package;
        foreach (var g in cached.Galleries)
        {
            var index = Galleries.ToList().FindIndex(old => old.Id == g.Id);
            if (index >= 0 && Galleries[index].PromptUsed != g.PromptUsed) Galleries[index] = g;
        }
        SelectedGallery = Galleries.FirstOrDefault(g => g.Id == selectedId); Prompt = previousPrompt; Package = previousPackage;
    }
    [RelayCommand] private void ConfigureMail() => new MailSettingsWindow(services) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
    private bool CanSendWeeklyMail() => !Busy && !BackgroundBusy && weeklyBatchKey != null && weeklyMailState == "Pending" && WeeklyItems.Count == 3;
    [RelayCommand(CanExecute = nameof(CanSendWeeklyMail))] private Task SendWeeklyMail() => RunAsync(async ct =>
    {
        var key = weeklyBatchKey ?? throw new InvalidOperationException("Najpierw przygotuj 3 paczki.");
        Status = "Sprawdzanie /blog i wysyłanie 3 paczek…";
        try { WeeklyStatus = Status = await Task.Run(() => services.GetRequiredService<WeeklyPromptService>().SendNowAsync(key, DateTimeOffset.UtcNow, ct), ct); }
        finally { await LoadWeeklyAsync(); }
    });
    [RelayCommand] private void OpenWeeklyPackage(string folder) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    [RelayCommand] private void OpenWeeklyGallery(string url) => OpenUrl(url);
    [RelayCommand] private void ResumeBackground() { OperationSession.Resume(paths); ResumeBackgroundRequested?.Invoke(); WeeklyStatus = "Wznowiono przygotowanie paczek w tle."; }
    private bool needsInitialCheck;
    public Task InitialCheckAsync() => needsInitialCheck ? RefreshCommand.ExecuteAsync(null) : Task.CompletedTask;
    public Task InitializeAsync() => LoadCachedAsync();
    private async Task LoadCachedAsync()
    {
        var cached = await content.CachedPromptGalleriesAsync(SelectedBrand);
        needsInitialCheck = cached.Checked == null;
        SetList(cached.Galleries); LastChecked = cached.Checked == null ? "Kliknij Sprawdź /blog, aby pobrać listę." : "Sprawdzono: " + cached.Checked.Value.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    }
    private void SetList(IEnumerable<Gallery> rows)
    {
        Galleries.Clear(); foreach (var row in rows) Galleries.Add(row); SelectedGallery = Galleries.FirstOrDefault();
        GalleryView.Refresh(); OnPropertyChanged(nameof(CountLabel));
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (Busy) return; Busy = true; work = new();
        try { await action(work.Token); }
        catch (OperationCanceledException) { Status = "Anulowano. Możesz wybrać inną galerię lub ponowić sprawdzanie."; }
        catch (Exception e)
        {
            Status = e is InvalidOperationException ? e.Message : "Nie udało się wykonać operacji. Sprawdź połączenie i ponów.";
            MessageBox.Show(Status, "WT - Blog Generator", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { work.Dispose(); work = null; Busy = false; }
    }
    [RelayCommand] private async Task ChooseBrand(WheelBrand brand)
    {
        await RunAsync(async _ => { SelectedBrand = brand; OnPropertyChanged(nameof(BrandTitle)); Search = ""; await LoadCachedAsync(); });
        await InitialCheckAsync();
    }
    [RelayCommand] private Task Refresh() => RunAsync(async ct =>
    {
        var progress = new Progress<string>(message => Status = message);
        var rows = await Task.Run(() => content.RefreshPromptGalleriesAsync(SelectedBrand, progress, ct), ct);
        needsInitialCheck = false; SetList(rows); LastChecked = "Sprawdzono: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"); Status = "Lista gotowa. Wybierz galerię i przygotuj plik do ChatGPT.";
    });
    [RelayCommand] private Task PrepareFile() => RunAsync(async ct =>
    {
        if (SelectedGallery == null) throw new InvalidOperationException("Wybierz galerię z listy.");
        var id = SelectedGallery.Id; var progress = new Progress<string>(message => Status = message);
        Package = await Task.Run(() => content.ExportGalleryPromptFilesAsync(id, OutputFolder, progress, ct), ct);
        Prompt = Package.Prompt;
        Status = Package.Photo == null ? "Plik gotowy. Otwórz folder, wrzuć DOCX do ChatGPT. Zdjęcia pobierzesz z linku galerii." : "Gotowe. Wrzuć DOCX do ChatGPT — polecenie i zdjęcie JPG/PNG są w pliku. Zdjęcie znajdziesz też osobno w folderze.";
        await LoadSelectedAsync(id, ct); OpenFolder();
    });
    private async Task LoadSelectedAsync(long id, CancellationToken ct)
    {
        var current = await content.GalleryAsync(id, ct);
        if (current != null)
        {
            var previousPrompt = Prompt; var previousPackage = Package;
            var index = Galleries.ToList().FindIndex(g => g.Id == id); if (index >= 0) Galleries[index] = current;
            SelectedGallery = current; Prompt = previousPrompt; Package = previousPackage; GalleryView.Refresh();
        }
    }
    [RelayCommand] private Task ToggleUsage() => RunAsync(async ct =>
    {
        if (SelectedGallery == null) throw new InvalidOperationException("Wybierz galerię z listy.");
        var id = SelectedGallery.Id; var used = !SelectedGallery.PromptUsed;
        await content.SetPromptGalleryUsedAsync(id, used, ct); await LoadSelectedAsync(id, ct);
        Status = used ? "Galeria przekreślona i przeniesiona na koniec listy." : "Cofnięto oznaczenie. Galeria wróciła do niewykorzystanych tematów.";
    });
    [RelayCommand] private void ChooseFolder()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Folder plików do ChatGPT" };
        if (picker.ShowDialog() == true) OutputFolder = picker.FolderName;
    }
    [RelayCommand] private void OpenGallery() => OpenUrl(SelectedGallery?.Url ?? Package?.GalleryUrl);
    [RelayCommand] private void OpenProduct() => OpenUrl(SelectedGallery?.Specification.ProductUrl ?? Package?.ProductUrl);
    [RelayCommand] private void OpenPhoto() => OpenUrl(SelectedGallery?.Images.FirstOrDefault()?.Url ?? SelectedGallery?.ThumbnailUrl);
    [RelayCommand] private void OpenFolder()
    {
        var folder = Package?.Folder ?? OutputFolder; Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }
    private static void OpenUrl(string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
    [RelayCommand] private void Cancel() { work?.Cancel(); CancelBackgroundRequested?.Invoke(); OperationSession.RequestCancellation(paths); Status = "Anulowanie…"; }
    public void StopUpdates() => work?.Cancel();
}
