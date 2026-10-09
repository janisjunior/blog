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
    private readonly ContentService content; private readonly SettingsService settingsService; private readonly PromptService prompts; private readonly ISecretStore secrets; private readonly NotificationService mail; private readonly WindowsScheduler scheduler;
    private CancellationTokenSource? work;
    private readonly AppPaths paths;
    [ObservableProperty] private bool externalOperationRunning;
    [ObservableProperty] private string backgroundProgress = "Brak aktywnej operacji w tle.";
    public bool CanCancelOperation => Busy || ExternalOperationRunning;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanCancelOperation));
    partial void OnExternalOperationRunningChanged(bool value) => OnPropertyChanged(nameof(CanCancelOperation));
    public event Action? PreparedSetCompleted;
    private bool preparedInitialized;
    private readonly System.Windows.Threading.DispatcherTimer preparationTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool pollingPreparation;
    public ObservableCollection<Article> PreparedArticles { get; } = [];
    [ObservableProperty] private Article? selectedPreparedArticle;
    [ObservableProperty] private string preparationStatus = "Sprawdzanie gotowych artykułów…";
    public ObservableCollection<TopicSuggestion> Suggestions { get; } = [];
    [ObservableProperty] private TopicSuggestion? selectedSuggestion;
    public ObservableCollection<Gallery> Galleries { get; } = []; public ObservableCollection<Article> Articles { get; } = []; public ObservableCollection<ErrorLog> Logs { get; } = []; public ObservableCollection<AutomationRun> Runs { get; } = []; public ObservableCollection<PromptVersion> PromptHistory { get; } = []; public ObservableCollection<string> Models { get; } = []; public ObservableCollection<NotificationHistory> Notifications { get; } = [];
    [ObservableProperty] private NotificationHistory? selectedNotification;
    public ICollectionView GalleryView { get; } public ICollectionView ArticleView { get; }
    public string[] Navigation { get; } = ["Pulpit", "Galerie", "Artykuły", "Prompty AI", "Harmonogram", "Ustawienia", "Historia i błędy"];
    public string[] BrandFilters { get; } = ["Wszystkie marki", "JR Wheels", "Concaver Wheels", "Vesser Forged"]; public WheelBrand[] Brands { get; } = Enum.GetValues<WheelBrand>();
    public string[] SelectionModes { get; } = ["Najnowsze niewykorzystane", "Ręczny"];
    public IEnumerable<Gallery> JrCandidates => Galleries.Where(x => x.Brand == WheelBrand.JR && !x.Used);
    public IEnumerable<Gallery> ConcaverCandidates => Galleries.Where(x => x.Brand == WheelBrand.Concaver && !x.Used);
    public IEnumerable<Gallery> VesserCandidates => Galleries.Where(x => x.Brand == WheelBrand.Vesser && !x.Used);
    public string[] Providers { get; } = ["OpenAI", "Anthropic"]; public string[] SmtpModes { get; } = ["StartTls", "SslOnConnect"];
    public string[] Days { get; } = ["Niedziela", "Poniedziałek", "Wtorek", "Środa", "Czwartek", "Piątek", "Sobota"];
    public string[] UsageFilters { get; } = ["Wszystkie", "Niewykorzystane", "Wykorzystane"]; public string[] Languages { get; } = ["PL", "EN"];
    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private int scheduleDayIndex = 1;
    partial void OnScheduleDayIndexChanged(int value) { if (value is >= 0 and <= 6) Settings.ScheduleDay = (DayOfWeek)value; }
    partial void OnSettingsChanged(AppSettings value) => ScheduleDayIndex = (int)value.ScheduleDay;
    [ObservableProperty] private AppSettings settings = new();
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string status = "Gotowy do pracy";
    [ObservableProperty] private string report = "";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string carMakeFilter = "Wszyscy producenci";
    [ObservableProperty] private string carModelFilter = "";
    [ObservableProperty] private string wheelModelFilter = "";
    public IEnumerable<string> CarMakes => Galleries.Select(x => x.Vehicle.Make).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().Order().Prepend("Wszyscy producenci");
    partial void OnCarMakeFilterChanged(string value) => GalleryView.Refresh();
    partial void OnCarModelFilterChanged(string value) => GalleryView.Refresh();
    partial void OnWheelModelFilterChanged(string value) => GalleryView.Refresh();
    [ObservableProperty] private string articleSearch = "";
    [ObservableProperty] private string brandFilter = "Wszystkie marki";
    [ObservableProperty] private string usageFilter = "Wszystkie";
    [ObservableProperty] private DateTime? addedAfter;
    [ObservableProperty] private Gallery? selectedGallery;
    [ObservableProperty] private Article? selectedArticle;
    [ObservableProperty] private string language = "PL";
    [ObservableProperty] private string articleTitle = "";
    [ObservableProperty] private string articleIntro = "";
    [ObservableProperty] private string articleBody = "";
    [ObservableProperty] private string articleWarnings = "";
    [ObservableProperty] private string articleHistory = "";
    [ObservableProperty] private WheelBrand promptBrand;
    [ObservableProperty] private string promptText = "";
    [ObservableProperty] private bool promptVerified;
    [ObservableProperty] private PromptVersion? selectedPromptVersion;
    [ObservableProperty] private bool galleryConfirmed;
    [ObservableProperty] private string nextRun = "Wyłączony";
    [ObservableProperty] private string lastSync = "Jeszcze nie wykonano";
    [ObservableProperty] private string dataFolder = "";
    public string ApiKey { private get; set; } = ""; public string SmtpPassword { private get; set; } = "";
    public int GalleryCount => Galleries.Count; public int ReadyCount => Articles.Count(x => x.Status is ArticleStatus.Ready or ArticleStatus.Approved); public int CorrectionCount => Articles.Count(x => x.Status == ArticleStatus.NeedsCorrection);
    public string SelectedArticleStatus => SelectedArticle?.Status.Label() ?? "Nie wybrano artykułu";
    public MainViewModel(IServiceProvider services)
    {
        content = services.GetRequiredService<ContentService>(); settingsService = services.GetRequiredService<SettingsService>(); prompts = services.GetRequiredService<PromptService>(); secrets = services.GetRequiredService<ISecretStore>(); mail = services.GetRequiredService<NotificationService>(); scheduler = services.GetRequiredService<WindowsScheduler>(); paths = services.GetRequiredService<AppPaths>(); DataFolder = paths.Root;
        GalleryView = CollectionViewSource.GetDefaultView(Galleries); GalleryView.Filter = FilterGallery;
        ArticleView = CollectionViewSource.GetDefaultView(Articles); ArticleView.Filter = x => x is Article a && a.Display.Contains(ArticleSearch, StringComparison.CurrentCultureIgnoreCase);
    }
    private bool FilterGallery(object item) => item is Gallery g && (BrandFilter == "Wszystkie marki" || BrandFilter == g.Brand.Name()) && (UsageFilter == "Wszystkie" || (UsageFilter == "Wykorzystane") == g.Used) && (!AddedAfter.HasValue || g.FirstDetected.LocalDateTime.Date >= AddedAfter.Value.Date) && (CarMakeFilter == "Wszyscy producenci" || g.Vehicle.Make == CarMakeFilter) && (g.Vehicle.Model ?? "").Contains(CarModelFilter, StringComparison.CurrentCultureIgnoreCase) && (g.Specification.Model ?? "").Contains(WheelModelFilter, StringComparison.CurrentCultureIgnoreCase) && Search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term => g.Display.Contains(term, StringComparison.CurrentCultureIgnoreCase));
    partial void OnSearchChanged(string value) => GalleryView.Refresh(); partial void OnBrandFilterChanged(string value) => GalleryView.Refresh(); partial void OnUsageFilterChanged(string value) => GalleryView.Refresh(); partial void OnAddedAfterChanged(DateTime? value) => GalleryView.Refresh(); partial void OnArticleSearchChanged(string value) => ArticleView.Refresh();
    partial void OnSelectedGalleryChanged(Gallery? value) => GalleryConfirmed = false;
    partial void OnSelectedArticleChanged(Article? value) => LoadArticle(); partial void OnLanguageChanged(string value) => LoadArticle();
    partial void OnPromptBrandChanged(WheelBrand value) { _ = RunAsync(LoadPromptAsync); }
    partial void OnSelectedPromptVersionChanged(PromptVersion? value) { if (value != null) { PromptText = value.Content; PromptVerified = value.EditorialDocumentVerified; } }
    private void LoadArticle()
    {
        var v = SelectedArticle?.Versions.Where(x => x.Language == Language).MaxBy(x => x.Revision);
        ArticleTitle = v?.Title ?? ""; ArticleIntro = v?.Intro ?? ""; ArticleBody = v?.Body ?? "";
        ArticleWarnings = SelectedArticle?.Warnings ?? "";
        ArticleHistory = SelectedArticle == null ? "" : string.Join("\n", SelectedArticle.Versions.OrderByDescending(x => x.Created).Select(x => $"{x.Created.LocalDateTime:g} · {x.Language} · wersja {x.Revision} · prompt {x.PromptVersionId} · tokeny wejście/wyjście: {x.InputTokens}/{x.OutputTokens}"));
        OnPropertyChanged(nameof(SelectedArticleStatus));
    }
    public async Task InitializeAsync()
    {
        Settings = await settingsService.LoadAsync(); await RefreshAsync(); await LoadPromptAsync(); await UpdatePreparedAsync();
        preparationTimer.Tick += async (_, _) =>
        {
            UpdateOperation();
            if (pollingPreparation || Busy) return; pollingPreparation = true;
            try { await UpdatePreparedAsync(); } catch { /* retain the last successful ready set during a transient database error */ }
            finally { pollingPreparation = false; }
        };
        preparationTimer.Start(); preparedInitialized = true;
    }
    public void StopUpdates() => preparationTimer.Stop();
    private void UpdateOperation()
    {
        var state = OperationSession.Read(paths);
        ExternalOperationRunning = work == null && OperationSession.IsRunning(paths);
        BackgroundProgress = state == null ? (OperationSession.Paused(paths) ? "Przygotowanie w tle wstrzymane — kliknij Wznów." : "Brak aktywnej operacji w tle.") : state.Progress;
    }
    private async Task UpdatePreparedAsync()
    {
        UpdateOperation();
        var previousCount = PreparedArticles.Count;
        var ready = await content.ReadySetAsync(); var selected = SelectedPreparedArticle?.Id;
        PreparedArticles.Clear(); foreach (var article in ready) PreparedArticles.Add(article);
        SelectedPreparedArticle = PreparedArticles.FirstOrDefault(a => a.Id == selected) ?? PreparedArticles.FirstOrDefault();
        PreparationStatus = ready.Count == 3 ? "Gotowe 3/3 — możesz od razu przejść do czytania." :
            !Settings.BackgroundPreparationEnabled ? $"Gotowe {ready.Count}/3 — przygotowanie w tle jest wyłączone." :
            string.IsNullOrWhiteSpace(Settings.AiModel) || Settings.InputPricePerMillion <= 0 || Settings.OutputPricePerMillion <= 0 ? $"Gotowe {ready.Count}/3 — uzupełnij AI i ceny w Ustawieniach." :
            $"Gotowe {ready.Count}/3 — program uzupełni brakujące artykuły w tle. Wynik i błędy znajdziesz w Historii.";
        if (OperationSession.Paused(paths)) PreparationStatus = $"Gotowe {ready.Count}/3 — przygotowanie w tle wstrzymane. Kliknij Wznów.";
        else if (ready.Count < 3 && Settings.BackgroundPreparationEnabled)
        {
            var last = (await content.RunsAsync()).FirstOrDefault(r => r.PeriodKey.StartsWith("stock-"));
            if (last?.Status is "Częściowe wykonanie" or "Przerwano") PreparationStatus = $"Gotowe {ready.Count}/3 — ostatnia próba: {last.Status.ToLowerInvariant()}. Szczegóły i przyczynę sprawdzisz w Historii.";
        }
        if (preparedInitialized && previousCount < 3 && ready.Count == 3) PreparedSetCompleted?.Invoke();
    }
    public async Task ConfigureBackgroundAsync()
    {
        try
        {
            WindowsScheduler.ApplyAutoStart(Settings.StartWithWindows, Environment.ProcessPath!);
            if (Settings.BackgroundPreparationEnabled && Settings.SetupCompleted && !string.IsNullOrWhiteSpace(Settings.AiModel) && string.IsNullOrWhiteSpace(secrets.Read(Settings.AiProvider)))
            { PreparationStatus = "Zapisz klucz API w Ustawieniach, aby przygotować artykuły w tle."; return; }
            await scheduler.ApplyBackgroundAsync(Settings, startNow: true);
            await UpdatePreparedAsync();
        }
        catch (Exception e) { PreparationStatus = "Nie uruchomiono przygotowania w tle: " + e.Message; }
    }
    [RelayCommand] private Task OpenPreparedArticle() => RunAsync(async () =>
    {
        if (SelectedPreparedArticle == null) return; var id = SelectedPreparedArticle.Id;
        await RefreshAsync(); SelectedArticle = Articles.FirstOrDefault(a => a.Id == id); SelectedTab = 2;
    });
    [RelayCommand] private Task StartPreparation() => RunAsync(async () =>
    {
        if (OperationSession.Paused(paths))
            while (OperationSession.IsRunning(paths))
            {
                Status = "Oczekiwanie na zakończenie anulowania przed wznowieniem…";
                await Task.Delay(250, Token);
            }
        OperationSession.Resume(paths); await ConfigureBackgroundAsync();
    });
    private async Task RefreshAsync()
    {
        var selected = SelectedArticle?.Id; var gallery = SelectedGallery?.Id;
        Galleries.Clear(); foreach (var x in await content.GalleriesAsync()) Galleries.Add(x);
        Articles.Clear(); foreach (var x in await content.ArticlesAsync()) Articles.Add(x);
        Logs.Clear(); foreach (var x in await content.LogsAsync()) Logs.Add(x);
        Notifications.Clear(); foreach (var x in await mail.HistoryAsync()) Notifications.Add(x);
        Runs.Clear(); foreach (var x in await content.RunsAsync()) Runs.Add(x);
        SelectedArticle = Articles.FirstOrDefault(x => x.Id == selected); SelectedGallery = Galleries.FirstOrDefault(x => x.Id == gallery);
        var dates = await content.SyncDatesAsync(); LastSync = string.Join(" · ", dates.Select(x => x.Key.Name() + ": " + (x.Value?.LocalDateTime.ToString("g") ?? "brak")));
        NextRun = Settings.ScheduleEnabled ? Normalization.NextRun(Settings, DateTime.Now).ToString("dddd, dd.MM.yyyy HH:mm") : "Automatyzacja wyłączona";
        foreach (var name in new[] { nameof(GalleryCount), nameof(ReadyCount), nameof(CorrectionCount), nameof(JrCandidates), nameof(ConcaverCandidates), nameof(VesserCandidates), nameof(CarMakes) }) OnPropertyChanged(name);
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (Busy) return; Busy = true; work = new();
        try { await action(); }
        catch (OperationBusyException) { UpdateOperation(); Status = "Trwa operacja w tle — postęp pokazano poniżej. Możesz ją anulować."; }
        catch (OperationCanceledException) { Status = "Anulowano — postęp zapisano"; }
        catch (Exception e) { Status = "Operacja nie została ukończona"; Report = e is InvalidOperationException or PlatformNotSupportedException ? e.Message : $"Błąd {e.GetType().Name}. Sprawdź konfigurację i historię."; MessageBox.Show(Report, "WT - Blog Generator", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { work.Dispose(); work = null; Busy = false; }
    }
    private Task<T> Background<T>(Func<IProgress<string>, CancellationToken, Task<T>> action)
    {
        var progress = Progress; var token = Token;
        return Task.Run(() => action(progress, token), token);
    }
    private IProgress<string> Progress => new Progress<string>(x => Status = x);
    private CancellationToken Token => work?.Token ?? CancellationToken.None;
    [RelayCommand] private void Cancel()
    {
        work?.Cancel(); OperationSession.RequestCancellation(paths);
        Status = "Anulowanie operacji… Przygotowanie w tle wstrzymano do kliknięcia Wznów.";
        UpdateOperation();
    }
    [RelayCommand] private Task Refresh() => RunAsync(RefreshAsync);
    [RelayCommand] private Task Sync() => RunAsync(async () => { Report = await Background((progress, token) => content.SyncAsync(progress, token)); await RefreshAsync(); try { await LoadSuggestionsAsync(); } catch (Exception e) when (e is not OperationCanceledException) { Suggestions.Clear(); Report += "\nSugestie niedostępne: " + e.Message; } Status = "Sprawdzanie galerii zakończono — zobacz raport"; });
    [RelayCommand] private Task SuggestTopics() => RunAsync(LoadSuggestionsAsync);
    private async Task LoadSuggestionsAsync()
    {
        var token = Token; var topics = await Task.Run(() => content.SuggestTopicsAsync(ct: token), token);
        Suggestions.Clear(); foreach (var topic in topics) Suggestions.Add(topic);
        Status = "Propozycje uwzględniają ostatnie wpisy /blog i gotowe artykuły. Przed AI program sprawdzi duplikaty i kartę modelu.";
    }
    [RelayCommand] private void OpenSuggestedGallery() { if (SelectedSuggestion == null) return; SelectedGallery = Galleries.FirstOrDefault(g => g.Id == SelectedSuggestion.Gallery.Id); SelectedTab = 1; }
    [RelayCommand] private Task GenerateCycle() => RunAsync(async () => { Report = await Background((progress, token) => content.RunCycleAsync(false, progress, token)); await RefreshAsync(); Status = "Cykl zakończony — zobacz raport"; MessageBox.Show(Report, "Przygotowanie artykułów zakończone", MessageBoxButton.OK, MessageBoxImage.Information); });
    [RelayCommand] private void OpenArticles() => SelectedTab = 2;
    [RelayCommand] private void OpenSettings() => SelectedTab = 5;
    [RelayCommand] private Task LoadGallery() => RunAsync(async () => { if (SelectedGallery == null) throw new InvalidOperationException("Wybierz galerię."); var id = SelectedGallery.Id; var token = Token; await Task.Run(() => content.LoadGalleryDetailsAsync(id, token), token); await RefreshAsync(); Status = "Pobrano dane i pełnowymiarowe zdjęcia galerii"; });
    [RelayCommand] private Task SaveGallery() => RunAsync(async () => { if (SelectedGallery == null) throw new InvalidOperationException("Wybierz galerię."); await content.SaveGalleryAsync(SelectedGallery, GalleryConfirmed, Token); await RefreshAsync(); Status = "Korekta zapisana"; });
    private async Task GenerateSelectedAsync(bool regenerate, bool dry, string? language = null)
    {
        var id = language == null ? SelectedGallery?.Id : SelectedArticle?.GalleryId;
        if (id == null) throw new InvalidOperationException("Wybierz galerię lub artykuł.");
        if (regenerate && MessageBox.Show("Ponowne generowanie utworzy nową wersję i spowoduje koszt API. Kontynuować?", "Potwierdzenie ponownego generowania", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var a = await Background((progress, token) => content.GenerateAsync(id.Value, regenerate, dry, language, progress, token));
        await RefreshAsync(); if (dry) Articles.Insert(0, a); SelectedArticle = dry ? a : Articles.FirstOrDefault(x => x.Id == a.Id); SelectedTab = 2;
        Status = dry ? "Test zakończony — galeria niewykorzystana, bez maila i zapisu artykułu" : "Generowanie zakończone";
    }
    [RelayCommand] private Task GenerateSelected() => RunAsync(async () => { await GenerateSelectedAsync(false, false); MessageBox.Show("Artykuł PL i EN jest przygotowany. Sprawdź treść w zakładce Artykuły.", "Artykuł gotowy", MessageBoxButton.OK, MessageBoxImage.Information); });
    [RelayCommand] private Task RegenerateGallery() => RunAsync(() => GenerateSelectedAsync(true, false));
    [RelayCommand] private Task TestPrompt() => RunAsync(async () => { if (SelectedTab == 3) { if (SelectedGallery?.Brand != PromptBrand) throw new InvalidOperationException("Wybierz galerię tej samej marki co prompt."); await prompts.SaveAsync(PromptBrand, PromptText, "Wersja testowana przez użytkownika", PromptVerified, Token); } await GenerateSelectedAsync(false, true); });
    [RelayCommand] private Task RegenerateLanguage() => RunAsync(() => GenerateSelectedAsync(true, false, Language));
    [RelayCommand] private Task SaveArticle() => RunAsync(async () => { var id = SelectedArticle?.Id ?? 0; if (id == 0) throw new InvalidOperationException("Wybierz zapisany artykuł. Wyniku testu nie zapisuje się do bazy."); await content.SaveArticleAsync(id, Language, ArticleTitle, ArticleIntro, ArticleBody, Token); await RefreshAsync(); Status = "Zapisano nową wersję artykułu"; });
    [RelayCommand] private void CopyArticle() { Clipboard.SetText($"{ArticleTitle}\n\n{ArticleIntro}\n\n{ArticleBody}"); Status = "Tekst skopiowano"; }
    [RelayCommand] private Task ExportLanguage() => ExportAsync(Language);
    [RelayCommand] private Task ExportAll() => ExportAsync(null);
    private Task ExportAsync(string? language) => RunAsync(async () => { if (SelectedArticle?.Id is not > 0) throw new InvalidOperationException("Wybierz zapisany artykuł."); Report = await content.ExportArticleAsync(SelectedArticle.Id, language, Token); Status = Settings.ExportImages ? "Wyeksportowano dokumenty i zdjęcia dopuszczone do użycia" : "Wyeksportowano teksty bez zdjęć"; });
    [RelayCommand] private Task Approve() => RunAsync(async () => { if (SelectedArticle == null) return; await content.SetStatusAsync(SelectedArticle.Id, false, Token); await RefreshAsync(); Status = "Zatwierdzono artykuł"; });
    [RelayCommand] private Task MarkPublished() => RunAsync(async () => { if (SelectedArticle == null) return; if (MessageBox.Show("Oznaczyć artykuł jako opublikowany? Program nie wysyła go na stronę internetową.", "Status publikacji", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; await content.SetStatusAsync(SelectedArticle.Id, true, Token); await RefreshAsync(); });
    private async Task LoadPromptAsync() { var p = await prompts.CurrentAsync(PromptBrand); PromptText = p.Content; PromptVerified = p.EditorialDocumentVerified; PromptHistory.Clear(); foreach (var x in await prompts.HistoryAsync(PromptBrand)) PromptHistory.Add(x); }
    [RelayCommand] private Task SavePrompt() => RunAsync(async () => { await prompts.SaveAsync(PromptBrand, PromptText, "Edycja użytkownika", PromptVerified, Token); await LoadPromptAsync(); Status = "Zapisano nową wersję promptu"; });
    [RelayCommand] private Task RestorePrompt() => RunAsync(async () => { await prompts.SaveAsync(PromptBrand, PromptService.Default(PromptBrand), PromptService.DefaultOrigin, true, Token); await LoadPromptAsync(); });
    [RelayCommand] private Task ImportPrompt() => RunAsync(async () => { var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Wybierz Wpisy na bloga.docx", Filter = "Dokument Word (*.docx)|*.docx" }; if (dialog.ShowDialog() != true) return; await prompts.ImportAsync(dialog.FileName, Token); await LoadPromptAsync(); Report = (await prompts.CurrentAsync(PromptBrand, Token)).EditorialDocumentVerified ? "Rozpoznano dostarczony dokument. Trzy gotowe szablony mają zmienne i są zweryfikowane." : "Zachowano pełne sekcje nowego dokumentu. Zastąp jego przykłady zmiennymi i potwierdź każdy szablon."; });
    [RelayCommand] private Task SaveSettings() => RunAsync(async () => { Settings.MinWords = AppSettings.ReferenceMinWords; Settings.MaxWords = AppSettings.ReferenceMaxWords; Settings.Validate(); secrets.Save(Settings.AiProvider, ApiKey); secrets.Save("smtp", SmtpPassword); ApiKey = ""; SmtpPassword = ""; Settings.SetupCompleted = true; await settingsService.SaveAsync(Settings, Token); await RefreshAsync(); await ConfigureBackgroundAsync(); Status = "Ustawienia zapisane; sekrety zabezpieczone DPAPI"; });
    [RelayCommand] private Task LoadModels() => RunAsync(async () => { await settingsService.SaveAsync(Settings, Token); if (!string.IsNullOrWhiteSpace(ApiKey)) { secrets.Save(Settings.AiProvider, ApiKey); ApiKey = ""; } Models.Clear(); foreach (var m in await content.ModelsAsync(Token)) Models.Add(m); Status = "Pobrano modele. Wybierz model obsługujący obrazy i sprawdź jego cennik."; });
    [RelayCommand] private Task TestMail() => RunAsync(async () => { await settingsService.SaveAsync(Settings, Token); secrets.Save("smtp", SmtpPassword); SmtpPassword = ""; await mail.TestAsync(Token); Status = "Wysłano wiadomość testową"; });
    [RelayCommand] private Task MarkMailReceived() => RunAsync(async () => { if (SelectedNotification == null) throw new InvalidOperationException("Wybierz powiadomienie."); if (MessageBox.Show("Potwierdzasz, że sprawdziłeś skrzynkę i ta wiadomość rzeczywiście dotarła? Program oznaczy ją jako wysłaną i nie ponowi wysyłki.", "Potwierdzenie odbioru", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; await mail.MarkReceivedAsync(SelectedNotification.Id, Token); await RefreshAsync(); });
    [RelayCommand] private Task RetryMail() => RunAsync(async () => { if (SelectedNotification == null) throw new InvalidOperationException("Wybierz powiadomienie."); if (MessageBox.Show("Najpierw sprawdź skrzynkę odbiorczą i spam. Ponowienie przy niepewnym wyniku może wysłać drugi egzemplarz. Czy świadomie chcesz ponowić wysyłkę?", "Ponowienie wiadomości", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; await mail.RetryAsync(SelectedNotification.Id, Token); await RefreshAsync(); Status = "Powiadomienie wysłano"; });
    [RelayCommand] private Task ApplySchedule() => RunAsync(async () => { await settingsService.SaveAsync(Settings, Token); await scheduler.ApplyAsync(Settings, Token); await RefreshAsync(); Status = "Zapisano zadanie w Harmonogramie Windows"; });
    [RelayCommand] private void ChooseFolder() { var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder eksportu artykułów" }; if (dialog.ShowDialog() == true) { Settings.ExportFolder = dialog.FolderName; OnPropertyChanged(nameof(Settings)); } }
    [RelayCommand] private void OpenGallerySource() => OpenSource(SelectedGallery?.Url ?? SelectedArticle?.Gallery.Url);
    [RelayCommand] private void OpenProductSource() => OpenSource(SelectedGallery?.Specification.ProductUrl ?? SelectedArticle?.Gallery.Specification.ProductUrl);
    private static void OpenSource(string? source) { if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
    [RelayCommand] private void OpenDataFolder() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DataFolder) { UseShellExecute = true });
}
