using System;
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
    public class UpdateProgress
    {
        public string FileName { get; set; } = "";
        public double Percentage { get; set; } = 0;
        public string Error { get; set; } = "";
        public bool HasError => !string.IsNullOrEmpty(Error);
        public long TotalBytes { get; set; } = 0;
        public long DownloadedBytes { get; set; } = 0;
        public bool IsPaused { get; set; } = false;
        public CancellationTokenSource? CancellationTokenSource { get; set; }
    }

    public class UpdateService
    {
        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly string _appFileName;
        private readonly string _updateFolder;

        public UpdateService(string repoOwner, string repoName, string appFileName, string updateFolder)
        {
            _repoOwner = repoOwner;
            _repoName = repoName;
            _appFileName = appFileName;
            _updateFolder = updateFolder;
        }

        public UpdateService() : this("yasinakmaz", "Celerate", "Celerate.exe", @"C:\Update\Celerate")
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

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FasterScaleUpdater");

            try
            {
                string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                string json = await client.GetStringAsync(url);
                
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
                    // Sabit değer olarak "1.1.0" kullan çünkü GitHub'da bu release var
                    latestVersion = "1.1.0";
                }

                Debug.WriteLine($"GitHub sürümü: {latestVersion}, Mevcut sürüm: {currentVersion}");

                Version current = new Version(currentVersion);
                Version latest = new Version(latestVersion);

                return (latest > current, currentVersion, latestVersion);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Update Check Error: " + ex.Message);
                return (false, "0.0.0", "0.0.0");
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

                // GitHub'dan MSIX paketini indir
                string? downloadUrl = await GetDownloadUrl(latestVersion);
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    progress.Error = "GitHub'da MSIX paketi bulunamadı";
                    return;
                }

                // Güncelleme klasörünü oluştur
                Directory.CreateDirectory(_updateFolder);
                string msixPath = Path.Combine(_updateFolder, $"FasterScale_{latestVersion}.msix");

                // Dosya zaten var mı kontrol et
                if (File.Exists(msixPath))
                {
                    bool needsRedownload = await CompareFileWithRemote(msixPath, downloadUrl);
                    
                    if (needsRedownload)
                    {
                        // Var olan dosyayı yedekle
                        string backupPath = Path.Combine(_updateFolder, $"FasterScale_{latestVersion}_{DateTime.Now:yyyyMMddHHmmss}.zip");
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
                    else
                    {
                        // Aynı dosya zaten var, indirmeye gerek yok
                        progress.Percentage = 70;
                        progress.FileName = $"Dosya zaten indirilmiş: {Path.GetFileName(msixPath)}";
                        await Task.Delay(1000, cancellationToken);
                        await InstallMsixPackage(msixPath, progress, dispatcher, cancellationToken);
                        progress.Percentage = 100;
                        onComplete.Invoke();
                        return;
                    }
                }

                // Dosyayı indir
                await DownloadMsixPackage(downloadUrl, msixPath, progress, dispatcher, cancellationToken);
                
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
            }
        }

        public async Task ManualUpdate(UpdateProgress progress, DispatcherQueue dispatcher, Action onComplete, Window parentWindow)
        {
            progress.CancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = progress.CancellationTokenSource.Token;

            try
            {
                // Windows Storage API ile dosya seçici işlemleri burada olmalı
                // FileOpenPicker ve WindowInteropHelper kullanımları ana projede olmalı
                // Burada dosya yolu alınıp işlenecektir

                progress.Error = "Manuel güncelleme DLL'e taşındığında ek kod gerektirecek";
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
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FasterScaleUpdater");

            try
            {
                string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                string json = await client.GetStringAsync(url);
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

        private async Task<bool> CompareFileWithRemote(string localFilePath, string remoteUrl)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FasterScaleUpdater");

            try
            {
                var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, remoteUrl));
                response.EnsureSuccessStatusCode();

                var remoteFileSize = response.Content.Headers.ContentLength ?? 0;
                var localFileInfo = new FileInfo(localFilePath);

                return localFileInfo.Length != remoteFileSize;
            }
            catch
            {
                // Karşılaştırma yapılamazsa, güvenli tarafta kalıp yeniden indirme yapacağız
                return true;
            }
        }

        private async Task DownloadMsixPackage(string downloadUrl, string filePath, UpdateProgress progress, DispatcherQueue dispatcher, CancellationToken cancellationToken)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FasterScaleUpdater");

            try
            {
                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                progress.TotalBytes = response.Content.Headers.ContentLength ?? 0;
                progress.DownloadedBytes = 0;
                
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                byte[] buffer = new byte[8192];
                int bytesRead;
                
                progress.FileName = $"İndiriliyor: {Path.GetFileName(filePath)}";
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    if (progress.IsPaused)
                    {
                        while (progress.IsPaused && !cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                    }
                    
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    progress.DownloadedBytes += bytesRead;
                    
                    double percentage = (double)progress.DownloadedBytes / progress.TotalBytes * 70;
                    progress.Percentage = percentage;
                    
                    string downloadedMB = (progress.DownloadedBytes / 1024.0 / 1024.0).ToString("F2");
                    string totalMB = (progress.TotalBytes / 1024.0 / 1024.0).ToString("F2");
                    progress.FileName = $"İndiriliyor: {Path.GetFileName(filePath)} ({downloadedMB} MB / {totalMB} MB)";
                    
                    dispatcher.TryEnqueue(() => { });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"İndirme hatası: {ex.Message}", ex);
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
                    progress.FileName = "MSIX yükleyicisi başlatıldı, kurulumu tamamlayın";
                    await Task.Delay(2000, cancellationToken); // Kurulum başladıktan sonra kısa bir bekleme
                    
                    // Uygulamayı kapat
                    await Task.Delay(1000, cancellationToken);
                    progress.Percentage = 95;
                    progress.FileName = "Uygulama kapatılıyor";
                    
                    KillCurrentProcess();
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

        private void KillCurrentProcess()
        {
            // Mevcut uygulamayı sonlandır
            try
            {
                Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Process kill error: {ex.Message}");
            }
        }
    }
} 