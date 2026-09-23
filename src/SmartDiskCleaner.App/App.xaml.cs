namespace SmartDiskCleaner.App;

using System.Windows;
using SmartDiskCleaner.App.ViewModels;
using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;
using SmartDiskCleaner.Infrastructure;
using SmartDiskCleaner.Rules;

public partial class App : System.Windows.Application
{
    private IAppLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appPathProvider = new WindowsAppPathProvider();
        _logger = new FileAppLogger(appPathProvider);

        // Global unhandled exception handlers
        DispatcherUnhandledException += (s, args) =>
        {
            _logger?.LogError("DispatcherException", "Unhandled UI dispatcher exception.", args.Exception);
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nDetails have been logged to the application log folder.",
                "Smart Disk Cleaner Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                _logger?.LogError("AppDomainException", "Fatal unhandled domain exception.", ex);
            }
        };

        try
        {
            var configService = new JsonAppConfigService(appPathProvider);
            var clock = new SystemClock();
            var volumeProvider = new WindowsVolumeInfoProvider();
            var metadataReader = new WindowsFileMetadataReader();
            var allocatedProvider = new WindowsAllocatedSizeProvider();
            var identityProvider = new WindowsPhysicalFileIdentityProvider();

            var scanner = new FileSystemScanner(
                metadataReader,
                allocatedProvider,
                identityProvider,
                volumeProvider,
                appPathProvider,
                clock);

            var accounting = new StorageAccountingService();
            var aggregation = new DirectoryAggregationService();
            var systemPaths = new ResolvedKnownPaths(
                VolumeRoot: "C:\\",
                WindowsDirectory: Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                SystemDirectory: Environment.GetFolderPath(Environment.SpecialFolder.System),
                UserProfile: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                LocalAppData: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                RoamingAppData: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                UserTemp: System.IO.Path.GetTempPath(),
                ApplicationDataRoot: appPathProvider.AppDataRoot);
            var rules = BuiltInRuleSetFactory.Create(systemPaths);
            var classification = new ClassificationService(rules);
            var treePolicy = new AdaptiveTreePolicy();

            var pipeline = new ScanPipeline(scanner, accounting, aggregation, classification, treePolicy, clock);
            var repository = new SqliteScanSnapshotRepository(appPathProvider);
            var orchestrator = new ScanOrchestrator(pipeline, repository, volumeProvider, _logger);
            var exportService = new ExportService();
            var searchService = new ScanSearchService();

            var mainVm = new MainWindowViewModel(orchestrator, exportService, searchService, configService, _logger);
            await mainVm.InitializeAsync();

            var window = new MainWindow
            {
                DataContext = mainVm
            };
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError("Startup", "Failed to initialize application.", ex);
            MessageBox.Show(
                $"Application initialization failed:\n\n{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
