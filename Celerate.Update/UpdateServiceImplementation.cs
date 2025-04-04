using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Celerate.Update
{
    public class UpdateServiceImplementation : IUpdateService
    {
        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly string _appFileName;
        private readonly string _updateFolder;
        private readonly IApplicationLifecycle _appLifecycle;
        private readonly IHttpClientService _httpClient;
        private UpdateThemeSettings _themeSettings;
        private const long MIN_REQUIRED_DISK_SPACE = 500 * 1024 * 1024; // 500 MB minimum gerekli disk alanı

        public UpdateServiceImplementation(
            string repoOwner, 
            string repoName, 
            string appFileName, 
            string updateFolder,
            IApplicationLifecycle appLifecycle)
        {
            _repoOwner = repoOwner;
            _repoName = repoName;
            _appFileName = appFileName;
            _updateFolder = updateFolder;
            _appLifecycle = appLifecycle;
            _httpClient = new HttpClientService();
            _themeSettings = new UpdateThemeSettings(); // Varsayılan tema ayarları
        }

        public UpdateServiceImplementation(IApplicationLifecycle appLifecycle) 
            : this("yasinakmaz", "Celerate", "Celerate.exe", @"C:\Update\Celerate", appLifecycle)
        {
        }

        public async Task<(bool HasUpdate, string CurrentVersion, string LatestVersion)> CheckForUpdateAvailability()
        {
            string appPath = Path.Combine(AppContext.BaseDirectory, _appFileName);

            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(appPath);
            string currentVersion = versionInfo.ProductVersion ?? "0.0.0";
            
            // Commit hash'ini temizle (+ işaretinden sonraki kısmı kaldır)
            if (currentVersion.Contains('+'))
            {
                currentVersion = currentVersion.Split('+')[0];
            }

            try
            {
                string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                string json = await _httpClient.GetStringAsync(url);
                
                Debug.WriteLine($"GitHub API Response: {json}");
                
                using var doc = JsonDocument.Parse(json);
                
                // Tag kontrolü
                string rawTag = doc.RootElement.GetProperty("tag_name").GetString() ?? "0.0.0";
                Debug.WriteLine($"Raw tag: {rawTag}");
                
                // Geçerli bir sürüm formatı mı kontrol et
                string latestVersion;
                bool isValidVersion = false;
                
                // İlk olarak tag'dan sürümü çıkartmaya çalış
                latestVersion = rawTag.TrimStart('v');
                try
                {
                    // Geçerli bir sürüm mü kontrol et
                    new Version(latestVersion);
                    isValidVersion = true;
                }
                catch
                {
                    // Eğer geçersizse, name alanını kontrol et
                    isValidVersion = false;
                }
                
                // Tag geçersizse, name alanını kontrol et
                if (!isValidVersion)
                {
                    if (doc.RootElement.TryGetProperty("name", out var nameElement))
                    {
                        string name = nameElement.GetString() ?? "";
                        // İsimde bir sürüm formatı var mı?
                        var match = System.Text.RegularExpressions.Regex.Match(name, @"\d+\.\d+(\.\d+)*");
                        if (match.Success)
                        {
                            latestVersion = match.Value;
                            isValidVersion = true;
                        }
                    }
                }
                
                // Sürüm hala bulunamadıysa sabit değer kullan
                if (!isValidVersion)
                {
                    // Sabit değer olarak "1.1.0" kullan
                    latestVersion = "1.1.0";
                }

                Debug.WriteLine($"GitHub sürümü: {latestVersion}, Mevcut sürüm: {currentVersion}");

                Version current = new Version(currentVersion);
                Version latest = new Version(latestVersion);

                // Güncelleme yoksa ve eski güncelleme dosyaları varsa temizle
                if (!(latest > current))
                {
                    await CleanUpdateFilesAsync();
                }

                return (latest > current, currentVersion, latestVersion);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Update Check Error: " + ex.Message);
                return (false, "0.0.0", "0.0.0");
            }
        }

        /// <summary>
        /// Eski güncelleme dosyalarını temizler
        /// </summary>
        public async Task CleanUpdateFilesAsync()
        {
            try
            {
                if (Directory.Exists(_updateFolder))
                {
                    string[] files = Directory.GetFiles(_updateFolder, "*.msix");
                    foreach (string file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine($"Eski güncelleme dosyası silindi: {file}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Dosya silinirken hata: {ex.Message}");
                        }
                    }
                    
                    // Yedek dosyaları da temizle
                    string[] backups = Directory.GetFiles(_updateFolder, "*.zip");
                    foreach (string backup in backups)
                    {
                        try
                        {
                            File.Delete(backup);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Yedek silinirken hata: {ex.Message}");
                        }
                    }
                }
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme dosyaları temizlenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Diskte yeterli alan olup olmadığını kontrol eder
        /// </summary>
        /// <param name="requiredSpace">Gerekli disk alanı (byte cinsinden)</param>
        /// <returns>Yeterli alan varsa true, yoksa false</returns>
        private bool HasEnoughDiskSpace(long requiredSpace)
        {
            try
            {
                string drive = Path.GetPathRoot(_updateFolder) ?? "C:\\";
                DriveInfo driveInfo = new DriveInfo(drive);
                
                long freeSpace = driveInfo.AvailableFreeSpace;
                Debug.WriteLine($"Boş disk alanı: {freeSpace / 1024 / 1024} MB, Gerekli: {requiredSpace / 1024 / 1024} MB");
                
                return freeSpace > requiredSpace;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disk alanı kontrolü sırasında hata: {ex.Message}");
                return false; // Hata durumunda false döndür
            }
        }

        public async Task CheckForUpdates(UpdateProgress progress, DispatcherQueue dispatcher, Action onComplete)
        {
            progress.CancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = progress.CancellationTokenSource.Token;

            try
            {
                var (hasUpdate, currentVersion, latestVersion) = await CheckForUpdateAvailability();

                if (!hasUpdate)
                {
                    progress.Percentage = 100;
                    progress.FileName = "Zaten en güncel sürümü kullanıyorsunuz";
                    onComplete.Invoke();
                    return;
                }

                // Ağ durumunu kontrol et
                var networkStatus = await CheckNetworkStatusAsync();
                if (!networkStatus.IsSuitableForDownload)
                {
                    progress.Error = $"Ağ bağlantısı güncelleme için uygun değil: {networkStatus.UnsuitabilityReason}";
                    return;
                }

                // GitHub'dan MSIX paketini indir
                string? downloadUrl = await GetDownloadUrl(latestVersion);
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    progress.Error = "GitHub'da MSIX paketi bulunamadı";
                    return;
                }

                // İndirmeden önce disk alanını kontrol et
                // Önce dosya boyutunu al
                long fileSize = await _httpClient.GetFileSizeAsync(downloadUrl);
                if (fileSize <= 0)
                {
                    fileSize = MIN_REQUIRED_DISK_SPACE;
                }
                
                // Güvenlik payıyla birlikte gereken alanı hesapla
                long requiredSpace = fileSize + (100 * 1024 * 1024); // Dosya boyutu + 100 MB güvenlik payı
                
                // Disk alanı kontrolü
                if (!HasEnoughDiskSpace(Math.Max(requiredSpace, MIN_REQUIRED_DISK_SPACE)))
                {
                    progress.Error = "Diskte yeterli alan yok. Lütfen disk alanı açın ve tekrar deneyin.";
                    return;
                }

                // Güncelleme klasörünü oluştur
                Directory.CreateDirectory(_updateFolder);
                string msixPath = Path.Combine(_updateFolder, $"Celerate_{latestVersion}.msix");

                // Dosya zaten var mı kontrol et
                if (File.Exists(msixPath))
                {
                    // Hash kontrolü yap
                    string expectedHash = await _httpClient.GetFileHashAsync(downloadUrl);
                    bool isValid = false;
                    
                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        isValid = await VerifyFileIntegrityAsync(msixPath, expectedHash);
                    }
                    else
                    {
                        // Hash değeri alınamadıysa, boyut kontrolü yap
                        var fileInfo = new FileInfo(msixPath);
                        isValid = (fileInfo.Length == fileSize && fileSize > 0);
                    }
                    
                    if (isValid)
                    {
                        // Aynı dosya zaten var, indirmeye gerek yok
                        progress.Percentage = 70;
                        progress.FileName = $"Dosya zaten indirilmiş";
                        await Task.Delay(1000, cancellationToken);
                        await InstallMsixPackage(msixPath, progress, dispatcher, cancellationToken);
                        progress.Percentage = 100;
                        onComplete.Invoke();
                        return;
                    }
                    else
                    {
                        // Var olan dosyayı yedekle
                        string backupPath = Path.Combine(_updateFolder, $"Celerate_{latestVersion}_{DateTime.Now:yyyyMMddHHmmss}.zip");
                        try
                        {
                            using (var zipArchive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
                            {
                                zipArchive.CreateEntryFromFile(msixPath, Path.GetFileName(msixPath));
                            }
                            File.Delete(msixPath);
                        }
                        catch (Exception ex)
                        {
                            progress.Error = $"Mevcut dosya yedeklemesi başarısız: {ex.Message}";
                            return;
                        }
                    }
                }

                // İlerleme nesnesi oluştur
                var downloadProgress = new Progress<DownloadProgress>(p =>
                {
                    progress.TotalBytes = p.TotalBytes;
                    progress.DownloadedBytes = p.DownloadedBytes;
                    progress.Percentage = p.ProgressPercentage * 0.7; // %70'e kadar indirme
                    
                    // Kalan süre tahmini
                    if (p.EstimatedRemainingSeconds > 0)
                    {
                        TimeSpan remaining = TimeSpan.FromSeconds(p.EstimatedRemainingSeconds);
                        progress.FileName = $"İndiriliyor ({GetReadableSize(p.DownloadedBytes)}/{GetReadableSize(p.TotalBytes)}) - Kalan süre: {GetReadableTimeSpan(remaining)}";
                    }
                    else
                    {
                        progress.FileName = $"İndiriliyor ({GetReadableSize(p.DownloadedBytes)}/{GetReadableSize(p.TotalBytes)})";
                    }
                    
                    if (progress.IsPaused)
                    {
                        progress.FileName = "İndirme duraklatıldı";
                    }
                });
                
                // Dosyayı indir
                bool downloadSuccess = await _httpClient.DownloadFileAsync(downloadUrl, msixPath, downloadProgress);
                
                if (cancellationToken.IsCancellationRequested)
                {
                    // İndirme iptal edildi
                    if (File.Exists(msixPath))
                    {
                        try { File.Delete(msixPath); } catch { }
                    }
                    progress.Error = "İndirme kullanıcı tarafından iptal edildi";
                    return;
                }
                
                if (!downloadSuccess)
                {
                    progress.Error = "Dosya indirme başarısız oldu";
                    return;
                }
                
                // İndirilen dosyanın bütünlüğünü kontrol et
                string fileHash = await _httpClient.GetFileHashAsync(downloadUrl);
                if (!string.IsNullOrEmpty(fileHash))
                {
                    bool integrityOk = await VerifyFileIntegrityAsync(msixPath, fileHash);
                    if (!integrityOk)
                    {
                        progress.Error = "İndirilen dosya bütünlük kontrolünden geçemedi";
                        try { File.Delete(msixPath); } catch { }
                        return;
                    }
                }

                // MSIX paketini kur
                await InstallMsixPackage(msixPath, progress, dispatcher, cancellationToken);
                
                progress.Percentage = 100;
                progress.FileName = "Güncelleme tamamlandı";
                onComplete.Invoke();
            }
            catch (OperationCanceledException)
            {
                progress.Error = "İşlem iptal edildi";
            }
            catch (Exception ex)
            {
                progress.Error = $"Güncelleme hatası: {ex.Message}";
                Debug.WriteLine("Update Error: " + ex.Message);
                
                // Hata raporlama
                var error = new UpdateError
                {
                    ErrorCode = "UPDATE_FAILED",
                    Message = ex.Message,
                    Details = ex.ToString(),
                    Timestamp = DateTime.Now,
                    StackTrace = ex.StackTrace ?? string.Empty
                };
                
                try
                {
                    await ReportUpdateErrorAsync(error);
                }
                catch { /* Hata raporlama başarısız olsa bile ana işlemi etkilememeli */ }
            }
        }

        public async Task ManualUpdate(UpdateProgress progress, DispatcherQueue dispatcher, Action onComplete, Window parentWindow, IFilePickerService filePickerService)
        {
            progress.CancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = progress.CancellationTokenSource.Token;

            try
            {
                // Interface üzerinden dosya seçimini yap
                var (filePath, fileName) = await filePickerService.PickMsixFileAsync(parentWindow);
                
                if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(fileName))
                {
                    progress.FileName = $"Seçilen dosya";
                    progress.Percentage = 20;

                    // Dosya boyutunu kontrol et
                    var fileInfo = new FileInfo(filePath);
                    
                    // Disk alanı kontrolü
                    if (!HasEnoughDiskSpace(fileInfo.Length + (50 * 1024 * 1024))) // 50 MB güvenlik payı
                    {
                        progress.Error = "Diskte yeterli alan yok. Lütfen disk alanı açın ve tekrar deneyin.";
                        return;
                    }

                    // Güncelleme klasörünü oluştur
                    Directory.CreateDirectory(_updateFolder);
                    string targetPath = Path.Combine(_updateFolder, fileName);
                    
                    if (File.Exists(targetPath) && targetPath != filePath)
                    {
                        File.Delete(targetPath);
                    }

                    if (targetPath != filePath)
                    {
                        // Dosyayı kopyala
                        using (var sourceStream = File.OpenRead(filePath))
                        using (var targetStream = File.Create(targetPath))
                        {
                            progress.FileName = $"Dosya kopyalanıyor";
                            byte[] buffer = new byte[8192];
                            int bytesRead;
                            long totalRead = 0;
                            long fileSize = new FileInfo(filePath).Length;
                            progress.TotalBytes = fileSize;

                            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await targetStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                totalRead += bytesRead;
                                progress.DownloadedBytes = totalRead;
                                progress.Percentage = 20 + (double)totalRead / fileSize * 50;
                                
                                dispatcher.TryEnqueue(() => { });
                            }
                        }
                    }
                    else
                    {
                        progress.Percentage = 70;
                    }
                    
                    // Dosyanın imzasını doğrula
                    progress.FileName = "Dosya imzası doğrulanıyor";
                    if (FileIntegrityChecker.VerifyMsixSignature(targetPath))
                    {
                        progress.Percentage = 75;
                    }
                    else
                    {
                        progress.Error = "MSIX paketi imza doğrulaması başarısız oldu";
                        return;
                    }

                    // MSIX paketini kur
                    await InstallMsixPackage(targetPath, progress, dispatcher, cancellationToken);
                    
                    progress.Percentage = 100;
                    progress.FileName = "Güncelleme tamamlandı";
                    onComplete.Invoke();
                }
                else
                {
                    progress.Error = "Dosya seçilmedi";
                }
            }
            catch (OperationCanceledException)
            {
                progress.Error = "İşlem iptal edildi";
            }
            catch (Exception ex)
            {
                progress.Error = $"Manuel güncelleme hatası: {ex.Message}";
            }
        }

        private async Task<string?> GetDownloadUrl(string version)
        {
            try
            {
                string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                string json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                
                var assets = doc.RootElement.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".msix"))
                    {
                        return asset.GetProperty("browser_download_url").GetString();
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetDownloadUrl Error: {ex.Message}");
                return null;
            }
        }

        private async Task InstallMsixPackage(string msixPath, UpdateProgress progress, DispatcherQueue dispatcher, CancellationToken cancellationToken)
        {
            progress.FileName = "MSIX paketi kuruluyor";
            progress.Percentage = 80;
            
            await Task.Delay(1000, cancellationToken); // UI update için kısa gecikme
            
            try
            {
                // PowerShell ile MSIX paketini aç
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Start-Process '{msixPath}'\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    progress.Percentage = 90;
                    progress.FileName = "MSIX yükleyicisi başlatıldı";
                    await Task.Delay(2000, cancellationToken); // Kurulum başladıktan sonra kısa bir bekleme
                    
                    // Uygulamayı kapat
                    await Task.Delay(1000, cancellationToken);
                    progress.Percentage = 95;
                    progress.FileName = "Uygulama kapatılıyor";
                    
                    // Interface kullanarak ana uygulamadan kapanışı gerçekleştir
                    _appLifecycle.ShutdownApplication();
                }
                else
                {
                    progress.Error = "MSIX yükleyicisi başlatılamadı";
                }
            }
            catch (Exception ex)
            {
                progress.Error = $"MSIX kurulum hatası: {ex.Message}";
            }
        }
        
        public async Task<ChangelogInfo> GetChangelogAsync(string version)
        {
            try
            {
                // GitHub API'den release notlarını al
                string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                
                if (!string.IsNullOrEmpty(version) && version != "latest")
                {
                    // Belirli bir sürüm için tag'i kullan
                    url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/tags/v{version}";
                }
                
                string json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                
                // Değişiklik listesi bilgilerini ayıkla
                string releaseNotes = doc.RootElement.GetProperty("body").GetString() ?? "";
                string versionString = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                
                // Tarih bilgisini al
                string publishedAtStr = doc.RootElement.GetProperty("published_at").GetString() ?? "";
                DateTime publishedAt = DateTime.Now;
                if (!string.IsNullOrEmpty(publishedAtStr))
                {
                    DateTime.TryParse(publishedAtStr, out publishedAt);
                }
                
                // ChangelogParser ile işle
                return ChangelogParser.ParseGitHubReleaseNotes(releaseNotes, versionString.TrimStart('v'), publishedAt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Changelog alma hatası: {ex.Message}");
                return new ChangelogInfo { Version = version };
            }
        }
        
        public async Task ScheduleUpdateAsync(DateTime scheduledTime)
        {
            try
            {
                var (hasUpdate, _, latestVersion) = await CheckForUpdateAvailability();
                
                if (!hasUpdate)
                {
                    Debug.WriteLine("Programlanacak güncelleme yok");
                    return;
                }
                
                // İndirme URL'sini al
                string? downloadUrl = await GetDownloadUrl(latestVersion);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Debug.WriteLine("Güncelleme dosyası bulunamadı");
                    return;
                }
                
                // Güncelleme klasörünü oluştur
                Directory.CreateDirectory(_updateFolder);
                string msixPath = Path.Combine(_updateFolder, $"Celerate_{latestVersion}.msix");
                
                // Dosya zaten var mı kontrol et
                bool needsDownload = true;
                if (File.Exists(msixPath))
                {
                    // Hash kontrolü yap
                    string expectedHash = await _httpClient.GetFileHashAsync(downloadUrl);
                    bool isValid = false;
                    
                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        isValid = await VerifyFileIntegrityAsync(msixPath, expectedHash);
                    }
                    
                    needsDownload = !isValid;
                }
                
                // Gerekirse dosyayı indir
                if (needsDownload)
                {
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        Debug.WriteLine($"İndirme ilerleme: %{p.ProgressPercentage:F2} ({p.DownloadedBytes}/{p.TotalBytes})");
                    });
                    
                    await _httpClient.DownloadFileAsync(downloadUrl, msixPath, progress);
                }
                
                // Görevi zamanla
                await UpdateScheduler.ScheduleUpdateAsync(scheduledTime, msixPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme zamanlaması hatası: {ex.Message}");
                throw;
            }
        }
        
        public async Task ResumeDownloadAsync(UpdateProgress progress, DispatcherQueue dispatcher, Action onComplete)
        {
            if (progress.CancellationTokenSource != null)
            {
                progress.CancellationTokenSource.Dispose();
            }
            
            progress.CancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = progress.CancellationTokenSource.Token;
            
            try
            {
                // Son indirmeyi bul
                if (!Directory.Exists(_updateFolder))
                {
                    progress.Error = "Devam edilecek güncelleme bulunamadı";
                    return;
                }
                
                string[] msixFiles = Directory.GetFiles(_updateFolder, "*.msix");
                if (msixFiles.Length == 0)
                {
                    progress.Error = "Devam edilecek güncelleme bulunamadı";
                    return;
                }
                
                // En son başlanan indirmeyi bul (en yeni dosya)
                string latestFile = msixFiles[0];
                DateTime latestTime = File.GetLastWriteTime(latestFile);
                
                foreach (var file in msixFiles)
                {
                    DateTime fileTime = File.GetLastWriteTime(file);
                    if (fileTime > latestTime)
                    {
                        latestFile = file;
                        latestTime = fileTime;
                    }
                }
                
                // Dosya boyutunu kontrol et
                var fileInfo = new FileInfo(latestFile);
                if (fileInfo.Length == 0)
                {
                    // Sıfır boyutlu dosya, silelim ve yeniden başlayalım
                    File.Delete(latestFile);
                    progress.Error = "Bozuk indirme tespit edildi, lütfen yeniden başlatın";
                    return;
                }
                
                // GitHub'dan en son sürümü kontrol et
                var (hasUpdate, _, latestVersion) = await CheckForUpdateAvailability();
                
                if (!hasUpdate)
                {
                    progress.Percentage = 100;
                    progress.FileName = "Zaten en güncel sürümü kullanıyorsunuz";
                    onComplete.Invoke();
                    return;
                }
                
                // İndirme URL'sini al
                string? downloadUrl = await GetDownloadUrl(latestVersion);
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    progress.Error = "GitHub'da MSIX paketi bulunamadı";
                    return;
                }
                
                // Ağ durumunu kontrol et
                var networkStatus = await CheckNetworkStatusAsync();
                if (!networkStatus.IsSuitableForDownload)
                {
                    progress.Error = $"Ağ bağlantısı güncelleme için uygun değil: {networkStatus.UnsuitabilityReason}";
                    return;
                }
                
                // İlerleme nesnesi oluştur
                var downloadProgress = new Progress<DownloadProgress>(p =>
                {
                    progress.TotalBytes = p.TotalBytes;
                    progress.DownloadedBytes = p.DownloadedBytes;
                    progress.Percentage = p.ProgressPercentage * 0.7; // %70'e kadar indirme
                    
                    // Kalan süre tahmini
                    if (p.EstimatedRemainingSeconds > 0)
                    {
                        TimeSpan remaining = TimeSpan.FromSeconds(p.EstimatedRemainingSeconds);
                        progress.FileName = $"İndirme devam ediyor ({GetReadableSize(p.DownloadedBytes)}/{GetReadableSize(p.TotalBytes)}) - Kalan süre: {GetReadableTimeSpan(remaining)}";
                    }
                    else
                    {
                        progress.FileName = $"İndirme devam ediyor ({GetReadableSize(p.DownloadedBytes)}/{GetReadableSize(p.TotalBytes)})";
                    }
                    
                    if (progress.IsPaused)
                    {
                        progress.FileName = "İndirme duraklatıldı";
                    }
                });
                
                // Dosyayı indir (kaldığı yerden devam)
                bool downloadSuccess = await _httpClient.DownloadFileAsync(downloadUrl, latestFile, downloadProgress, true);
                
                if (cancellationToken.IsCancellationRequested)
                {
                    progress.Error = "İndirme kullanıcı tarafından iptal edildi";
                    return;
                }
                
                if (!downloadSuccess)
                {
                    progress.Error = "Dosya indirme başarısız oldu";
                    return;
                }
                
                // İndirilen dosyanın bütünlüğünü kontrol et
                string fileHash = await _httpClient.GetFileHashAsync(downloadUrl);
                if (!string.IsNullOrEmpty(fileHash))
                {
                    bool integrityOk = await VerifyFileIntegrityAsync(latestFile, fileHash);
                    if (!integrityOk)
                    {
                        progress.Error = "İndirilen dosya bütünlük kontrolünden geçemedi";
                        return;
                    }
                }
                
                // MSIX paketini kur
                await InstallMsixPackage(latestFile, progress, dispatcher, cancellationToken);
                
                progress.Percentage = 100;
                progress.FileName = "Güncelleme tamamlandı";
                onComplete.Invoke();
            }
            catch (OperationCanceledException)
            {
                progress.Error = "İşlem iptal edildi";
            }
            catch (Exception ex)
            {
                progress.Error = $"Güncelleme hatası: {ex.Message}";
                Debug.WriteLine("Update Resume Error: " + ex.Message);
            }
        }
        
        public async Task ReportUpdateErrorAsync(UpdateError error)
        {
            try
            {
                // Hata raporunu bir dosyaya kaydet
                string errorLogPath = Path.Combine(_updateFolder, "UpdateErrors");
                Directory.CreateDirectory(errorLogPath);
                
                string errorFileName = $"error_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
                string errorFilePath = Path.Combine(errorLogPath, errorFileName);
                
                // Ek sistem bilgilerini ekle
                error.AppVersion = GetAppVersion();
                error.SystemInfo = GetSystemInfo();
                
                // JSON olarak kaydet
                string json = JsonSerializer.Serialize(error, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(errorFilePath, json);
                
                // İsteğe bağlı: Hata raporunu bir sunucuya gönder
                // Bu örnekte sadece dosyaya kaydediyoruz
                Debug.WriteLine($"Hata raporu kaydedildi: {errorFilePath}");
                
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hata raporlama sırasında hata: {ex.Message}");
                return;
            }
        }
        
        public async Task<NetworkStatus> CheckNetworkStatusAsync()
        {
            return await NetworkUtility.CheckNetworkStatusAsync();
        }
        
        public async Task<bool> VerifyFileIntegrityAsync(string filePath, string expectedHash)
        {
            try
            {
                // Hash algoritmasını otomatik belirle
                HashAlgorithmType algorithm;
                
                switch (expectedHash.Length)
                {
                    case 32:
                        algorithm = HashAlgorithmType.MD5;
                        break;
                    case 40:
                        algorithm = HashAlgorithmType.SHA1;
                        break;
                    case 64:
                    default:
                        algorithm = HashAlgorithmType.SHA256;
                        break;
                }
                
                // Dosya bütünlüğünü kontrol et
                return await FileIntegrityChecker.VerifyFileIntegrityAsync(filePath, expectedHash, algorithm);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dosya bütünlük kontrolü hatası: {ex.Message}");
                return false;
            }
        }
        
        public void SetThemeSettings(UpdateThemeSettings themeSettings)
        {
            _themeSettings = themeSettings ?? new UpdateThemeSettings();
        }
        
        private string GetAppVersion()
        {
            try
            {
                string appPath = Path.Combine(AppContext.BaseDirectory, _appFileName);
                var versionInfo = FileVersionInfo.GetVersionInfo(appPath);
                return versionInfo.ProductVersion ?? "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }
        
        private Dictionary<string, string> GetSystemInfo()
        {
            var info = new Dictionary<string, string>();
            
            try
            {
                info["OS"] = Environment.OSVersion.ToString();
                info["64Bit"] = Environment.Is64BitOperatingSystem.ToString();
                info["MachineName"] = Environment.MachineName;
                info["ProcessorCount"] = Environment.ProcessorCount.ToString();
                info["DotNetVersion"] = Environment.Version.ToString();
                info["WorkingSet"] = Environment.WorkingSet.ToString();
            }
            catch
            {
                // Bilgiler alınamazsa boş sözlük döndür
            }
            
            return info;
        }
        
        private string GetReadableSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }
        
        private string GetReadableTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
            {
                return $"{span.Hours}s {span.Minutes}d";
            }
            else if (span.TotalMinutes >= 1)
            {
                return $"{span.Minutes}d {span.Seconds}s";
            }
            else
            {
                return $"{span.Seconds}s";
            }
        }
    }
} 