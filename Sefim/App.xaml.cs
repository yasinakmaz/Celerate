namespace Sefim
{
    public partial class App : Application
    {
        public SefimWindows windowSefim { get; }
        public App(SefimWindows WindowSefim)
        {
            InitializeComponent();
            windowSefim = WindowSefim;
            SecureStorageService.LoadTaskSecureStorage();
            SecureStorageService.GetToPublicService();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            windowSefim.Page = new AppShell();
            return windowSefim;
        }
    }
}