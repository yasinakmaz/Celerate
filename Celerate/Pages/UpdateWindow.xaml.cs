namespace Celerate.Pages
{
    public sealed partial class UpdateWindow : Window
    {
        private readonly UpdateProgress _progress = new();
        private IUpdateService _updateService;
        private readonly IFilePickerService _filePickerService;
        private string _currentVersion = "0.0.0";
        private string _latestVersion = "0.0.0";
        private bool _updateInProgress = false;
        private ContentDialog _closeDialog;

        public UpdateWindow()
        {
            this.InitializeComponent();
            
            // Servisler oluştur
            var appLifecycle = new AppLifecycle();
            _updateService = new UpdateServiceImplementation(appLifecycle);
            _filePickerService = new FilePickerService();
            
            // Kapatma diyaloğunu oluştur
            InitializeCloseDialog();
            
            CheckForUpdates();
        }

        /// <summary>
        /// Kapatma diyaloğunu oluştur
        /// </summary>
        private void InitializeCloseDialog()
        {
            _closeDialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Güncelleme İşlemi",
                Content = "Güncelleme işlemi devam ediyor. Kapatmak istediğinizden emin misiniz?",
                PrimaryButtonText = "Evet, Kapat",
                SecondaryButtonText = "Hayır, Devam Et",
                DefaultButton = ContentDialogButton.Secondary
            };
        }

        /// <summary>
        /// App sınıfından UpdateService'in atanması için metod
        /// </summary>
        public void SetUpdateService(IUpdateService updateService)
        {
            _updateService = updateService;
        }

        private async void CheckForUpdates()
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            ActionPanel.Visibility = Visibility.Collapsed;
            ContinueUpdatePanel.Visibility = Visibility.Collapsed;
            ProgressBar.Value = 0;
            
            try
            {
                var (hasUpdate, currentVersion, latestVersion) = await _updateService.CheckForUpdateAvailability();
                _currentVersion = currentVersion;
                _latestVersion = latestVersion;

                if (hasUpdate)
                {
                    // Yeni güncelleme var, kullanıcıya sor
                    UpdateVersionText.Text = $"Mevcut Sürüm: {_currentVersion} → Yeni Sürüm: {_latestVersion}";
                    UpdateNotificationPanel.Visibility = Visibility.Visible;
                    StatusText.Text = "Güncelleme Mevcut";
                }
                else
                {
                    // Zaten güncel
                    ProgressBar.Value = 100;
                    StatusText.Text = "Güncel Sürümü Kullanıyorsunuz";
                    FileText.Text = $"Sürüm {_currentVersion}";
                    await Task.Delay(2000);
                    
                    OpenMainWindow();
                }
            }
            catch (Exception ex)
            {
                ShowError($"Güncelleme kontrolü sırasında hata oluştu: {ex.Message}");
            }
        }

        private async void StartUpdate()
        {
            _updateInProgress = true;
            UpdateNotificationPanel.Visibility = Visibility.Collapsed;
            ActionPanel.Visibility = Visibility.Visible;
            ContinueUpdatePanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ProgressBar.Value = 0;
            StatusText.Text = "Güncelleme Başlatılıyor...";
            
            // Güncelleme işlemini başlat
            await _updateService.CheckForUpdates(_progress, DispatcherQueue, UpdateCompleted);
            
            // UI güncelleme timer'ı başlat
            DispatcherQueue.TryEnqueue(() => StartUpdateProgressTimer());
        }

        private async void ManualUpdate()
        {
            _updateInProgress = true;
            UpdateNotificationPanel.Visibility = Visibility.Collapsed;
            ActionPanel.Visibility = Visibility.Visible;
            ContinueUpdatePanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ProgressBar.Value = 0;
            StatusText.Text = "Manuel Güncelleme Başlatılıyor...";
            
            await _updateService.ManualUpdate(_progress, DispatcherQueue, UpdateCompleted, this, _filePickerService);
            
            DispatcherQueue.TryEnqueue(() => StartUpdateProgressTimer());
        }

        private void StartUpdateProgressTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += (s, e) => UpdateProgressUI();
            timer.Start();
        }

        private void UpdateProgressUI()
        {
            if (!_updateInProgress) return;

            ProgressBar.Value = _progress.Percentage;
            FileText.Text = _progress.FileName;
            
            if (_progress.TotalBytes > 0)
            {
                string downloadedMB = (_progress.DownloadedBytes / 1024.0 / 1024.0).ToString("F2");
                string totalMB = (_progress.TotalBytes / 1024.0 / 1024.0).ToString("F2");
                ProgressText.Text = $"{downloadedMB} MB / {totalMB} MB (%{(int)_progress.Percentage})";
            }
            else
            {
                ProgressText.Text = $"%{(int)_progress.Percentage}";
            }

            PauseResumeButton.Content = _progress.IsPaused ? "Devam Et" : "Durdur";

            if (_progress.HasError)
            {
                _updateInProgress = false;
                ShowError(_progress.Error);
            }
            else if (_progress.Percentage >= 100)
            {
                _updateInProgress = false;
                StatusText.Text = "Güncelleme Tamamlandı";
            }
        }

        private void UpdateCompleted()
        {
            // İşlem tamamlandı, UI güncelle
            _updateInProgress = false;
        }

        private void ShowError(string errorMessage)
        {
            StatusText.Text = "Güncelleme Hatası!";
            ErrorText.Text = errorMessage;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        private void OpenMainWindow()
        {
            new MainWindow().Activate();
            this.Close();
        }

        // Event handlers
        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            StartUpdate();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            OpenMainWindow();
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            _progress.IsPaused = !_progress.IsPaused;
            PauseResumeButton.Content = _progress.IsPaused ? "Devam Et" : "Durdur";
        }

        private void ManualUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            ManualUpdate();
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInProgress)
            {
                // Güncelleme işlemi devam ederken kapatma isteği - onay sor
                var result = await _closeDialog.ShowAsync();
                
                if (result == ContentDialogResult.Primary) // Evet, kapat
                {
                    if (_progress.CancellationTokenSource != null)
                    {
                        try
                        {
                            _progress.CancellationTokenSource.Cancel();
                            // Güncelleme dosyalarını temizle
                            await _updateService.CleanUpdateFilesAsync();
                        }
                        catch { }
                    }
                    
                    OpenMainWindow();
                }
                else if (result == ContentDialogResult.Secondary) // Hayır, devam et
                {
                    // İşlem zaten iptal edilmiş olabilir, tekrar başlatma seçeneği göster
                    if (_progress.CancellationTokenSource?.IsCancellationRequested ?? false)
                    {
                        _updateInProgress = false;
                        ContinueUpdatePanel.Visibility = Visibility.Visible;
                        ActionPanel.Visibility = Visibility.Collapsed;
                    }
                    // Yoksa devam et
                }
            }
            else
            {
                // Güncelleme işlemi devam etmiyorsa doğrudan kapat
                if (_progress.CancellationTokenSource != null)
                {
                    try
                    {
                        _progress.CancellationTokenSource.Cancel();
                    }
                    catch { }
                }
                
                OpenMainWindow();
            }
        }

        private void CopyErrorButton_Click(object sender, RoutedEventArgs e)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(ErrorText.Text);
            Clipboard.SetContent(dataPackage);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdates();
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            OpenMainWindow();
        }
        
        private void ContinueUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            ContinueUpdatePanel.Visibility = Visibility.Collapsed;
            StartUpdate();
        }
    }
}
