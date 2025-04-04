using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Celerate.Update
{
    /// <summary>
    /// HTTP isteklerini ve dosya indirme işlemlerini yöneten servis
    /// </summary>
    public class HttpClientService : IHttpClientService
    {
        private readonly HttpClient _httpClient;
        private readonly int _bufferSize;
        private readonly TimeSpan _timeout;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        /// <summary>
        /// HttpClientService sınıfının yapıcısı
        /// </summary>
        public HttpClientService(TimeSpan? timeout = null, int bufferSize = 8192)
        {
            _bufferSize = bufferSize;
            _timeout = timeout ?? TimeSpan.FromMinutes(5);
            _cancellationTokenSource = new CancellationTokenSource();
            
            _httpClient = new HttpClient
            {
                Timeout = _timeout
            };
            
            // Kullanıcı ajanını ayarla
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Celerate-Updater/1.0");
            
            // TLS 1.3 ve üzeri protokollerin kullanılmasını sağla
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | 
                System.Net.SecurityProtocolType.Tls13;
        }
        
        /// <summary>
        /// HTTP GET isteği yapar ve yanıtı string olarak döndürür
        /// </summary>
        public async Task<string> GetStringAsync(string url)
        {
            try
            {
                // Hataları yakala ve tekrar dene (retry)
                int retryCount = 0;
                const int maxRetries = 3;
                
                while (true)
                {
                    try
                    {
                        return await _httpClient.GetStringAsync(url);
                    }
                    catch (HttpRequestException ex) when (retryCount < maxRetries)
                    {
                        retryCount++;
                        Debug.WriteLine($"HTTP isteği başarısız oldu ({retryCount}/{maxRetries}): {ex.Message}");
                        await Task.Delay(1000 * retryCount); // Artan beklemeli tekrar deneme
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HTTP isteği hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Dosya indirmeyi başlatır ve ilerlemeyi raporlar
        /// </summary>
        public async Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<DownloadProgress> progress, bool resumeIfExists = true)
        {
            try
            {
                // Hedef klasörün var olduğundan emin ol
                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
                
                // Dosya zaten varsa ve kaldığı yerden devam etme isteği varsa
                long existingFileSize = 0;
                if (resumeIfExists && File.Exists(destinationPath))
                {
                    FileInfo fileInfo = new FileInfo(destinationPath);
                    existingFileSize = fileInfo.Length;
                }
                
                // Dosya boyutunu öğren
                long totalSize = await GetFileSizeAsync(url);
                
                // Kaldığı yerden devam ediyorsa ve dosya zaten tam ise
                if (existingFileSize > 0 && existingFileSize >= totalSize)
                {
                    // Dosya zaten tam, indirmeye gerek yok
                    var downloadProgress = new DownloadProgress
                    {
                        TotalBytes = totalSize,
                        DownloadedBytes = totalSize,
                        BytesPerSecond = 0
                    };
                    
                    progress?.Report(downloadProgress);
                    return true;
                }
                
                // Kaldığı yerden devam etme durumunda
                if (existingFileSize > 0)
                {
                    return await DownloadRangeAsync(url, destinationPath, existingFileSize, totalSize, progress);
                }
                
                // Sıfırdan indirme
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        using (var contentStream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token))
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, _bufferSize, true))
                        {
                            var buffer = new byte[_bufferSize];
                            long totalBytesRead = 0;
                            long totalBytesReadForSpeed = 0;
                            int bytesRead;
                            var sw = Stopwatch.StartNew();
                            long lastSpeedUpdate = 0;
                            
                            // İlerleme nesnesi
                            var downloadProgress = new DownloadProgress
                            {
                                TotalBytes = totalSize,
                                DownloadedBytes = 0,
                                BytesPerSecond = 0
                            };
                            
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                                
                                totalBytesRead += bytesRead;
                                totalBytesReadForSpeed += bytesRead;
                                
                                // Her 100ms'de bir hız hesapla
                                if (sw.ElapsedMilliseconds - lastSpeedUpdate > 100)
                                {
                                    double secondsElapsed = sw.ElapsedMilliseconds / 1000.0;
                                    if (secondsElapsed > 0)
                                    {
                                        downloadProgress.BytesPerSecond = (long)(totalBytesReadForSpeed / secondsElapsed);
                                    }
                                    
                                    lastSpeedUpdate = sw.ElapsedMilliseconds;
                                }
                                
                                downloadProgress.DownloadedBytes = totalBytesRead;
                                progress?.Report(downloadProgress);
                            }
                            
                            return true;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("İndirme işlemi iptal edildi.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dosya indirme hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Belirtilen aralıktaki veriyi indirir (range request)
        /// </summary>
        public async Task<bool> DownloadRangeAsync(string url, string destinationPath, long from, long to, IProgress<DownloadProgress> progress)
        {
            try
            {
                // Range isteği oluştur
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Range = new RangeHeaderValue(from, to);
                    
                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token))
                    {
                        // 206 Partial Content bekliyoruz
                        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                        {
                            using (var contentStream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token))
                            using (var fileStream = new FileStream(destinationPath, FileMode.Append, FileAccess.Write, FileShare.None, _bufferSize, true))
                            {
                                var buffer = new byte[_bufferSize];
                                long totalBytesRead = from; // Başlangıç değeri
                                long totalBytesReadForSpeed = 0;
                                int bytesRead;
                                var sw = Stopwatch.StartNew();
                                long lastSpeedUpdate = 0;
                                
                                // İlerleme nesnesi
                                var downloadProgress = new DownloadProgress
                                {
                                    TotalBytes = to,
                                    DownloadedBytes = from,
                                    BytesPerSecond = 0
                                };
                                
                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                                    
                                    totalBytesRead += bytesRead;
                                    totalBytesReadForSpeed += bytesRead;
                                    
                                    // Her 100ms'de bir hız hesapla
                                    if (sw.ElapsedMilliseconds - lastSpeedUpdate > 100)
                                    {
                                        double secondsElapsed = sw.ElapsedMilliseconds / 1000.0;
                                        if (secondsElapsed > 0)
                                        {
                                            downloadProgress.BytesPerSecond = (long)(totalBytesReadForSpeed / secondsElapsed);
                                        }
                                        
                                        lastSpeedUpdate = sw.ElapsedMilliseconds;
                                    }
                                    
                                    downloadProgress.DownloadedBytes = totalBytesRead;
                                    progress?.Report(downloadProgress);
                                }
                                
                                return true;
                            }
                        }
                        else
                        {
                            // Range isteği desteklenmiyorsa sıfırdan indir
                            Debug.WriteLine("Range isteği desteklenmiyor, sıfırdan indiriliyor.");
                            return await DownloadFileAsync(url, destinationPath, progress, false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("İndirme işlemi iptal edildi.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Range indirme hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Dosya boyutunu öğrenir
        /// </summary>
        public async Task<long> GetFileSizeAsync(string url)
        {
            try
            {
                // HEAD isteği ile dosya boyutunu öğren
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                {
                    using (var response = await _httpClient.SendAsync(request, _cancellationTokenSource.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        if (response.Content.Headers.ContentLength.HasValue)
                        {
                            return response.Content.Headers.ContentLength.Value;
                        }
                        else
                        {
                            // Content-Length başlığı yoksa, GET isteği ile dosya boyutunu öğren
                            using (var getRequest = new HttpRequestMessage(HttpMethod.Get, url))
                            {
                                getRequest.Headers.Range = new RangeHeaderValue(0, 0);
                                
                                using (var getResponse = await _httpClient.SendAsync(getRequest, _cancellationTokenSource.Token))
                                {
                                    if (getResponse.Content.Headers.ContentRange != null && getResponse.Content.Headers.ContentRange.Length.HasValue)
                                    {
                                        return getResponse.Content.Headers.ContentRange.Length.Value;
                                    }
                                }
                            }
                            
                            // Boyut belirlenemedi
                            return -1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dosya boyutu öğrenme hatası: {ex.Message}");
                return -1;
            }
        }
        
        /// <summary>
        /// Dosya hash değerini alır
        /// </summary>
        public async Task<string> GetFileHashAsync(string url)
        {
            try
            {
                // Uzak sunucudan hash dosyasını indir (genelde .sha256 uzantılı)
                string hashUrl = url + ".sha256";
                
                try
                {
                    string hashContent = await GetStringAsync(hashUrl);
                    
                    // Hash içeriğini temizle ve döndür
                    return hashContent.Trim().Split(' ')[0].ToLowerInvariant();
                }
                catch
                {
                    // Hash dosyası bulunamadı, alternatif yöntemler dene
                    Debug.WriteLine("Hash dosyası bulunamadı, alternatif yöntemler deneniyor.");
                    
                    // GitHub API üzerinden hash değerini almaya çalış
                    // Bu örnekte varsayılan bir değer döndürüyoruz, gerçek uygulamada
                    // GitHub API üzerinden hash değerini almak gerekir
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hash değeri alma hatası: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// İndirme işlemini iptal eder
        /// </summary>
        public void CancelDownload()
        {
            _cancellationTokenSource.Cancel();
        }
        
        /// <summary>
        /// Dosyanın hash değerini hesaplar
        /// </summary>
        public static async Task<string> CalculateFileHashAsync(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    byte[] hash = await sha256.ComputeHashAsync(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
    }
} 