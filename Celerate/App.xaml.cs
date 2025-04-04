namespace Celerate
{
    public partial class App : Application
    {
        private readonly AppLifecycle _appLifecycle;
        private readonly IUpdateService _updateService;

        public App()
        {
            // Uygulama yaşam döngüsü servisi oluştur
            _appLifecycle = new AppLifecycle();
            
            // UpdateService'i oluştur
            _updateService = new UpdateServiceImplementation(_appLifecycle);

            this.InitializeComponent();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            string[] cmdArgs = Environment.GetCommandLineArgs();
            bool skipUpdate = cmdArgs.Any(arg => arg.Equals("--skip-update", StringComparison.OrdinalIgnoreCase));

            if (!skipUpdate)
            {
                // DLL üzerinden güncelleme kontrolü yap
                var (hasUpdate, currentVersion, latestVersion) = await _updateService.CheckForUpdateAvailability();

                if (hasUpdate)
                {
                    m_window = new UpdateWindow();
                    m_window.Activate();
                    return;
                }
            }
            m_window = new MainWindow();
            m_window.Activate();
        }

        private Window m_window;
    }
}
