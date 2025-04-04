namespace Celerate
{
    public sealed partial class MainWindow : Window
    {
        public NavigationView ViewPanel => NvView;
        public MainWindow()
        {
            this.InitializeComponent();

            ContentFrame.Navigate(typeof(DashboardPage));
            ViewPanel.ItemInvoked += NvView_ItemInvoked;
        }

        private void NvView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            var selectedItem = sender.MenuItems.OfType<NavigationViewItem>()
                                .FirstOrDefault(x => (string)x.Content == (string)args.InvokedItem);

            if (selectedItem != null && selectedItem.Tag != null)
            {
                string pageTag = selectedItem.Tag.ToString();
                Type pageType = Type.GetType($"FasterScale.Pages.{pageTag}");

                if (pageType != null)
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }
    }
}
