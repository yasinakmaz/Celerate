namespace Sefim
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(StartedPage), typeof(StartedPage));
            Routing.RegisterRoute(nameof(LoginServicePage), typeof(LoginServicePage));
            Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        }
        protected override bool OnBackButtonPressed()
        {
            return false;
        }
    }
}
