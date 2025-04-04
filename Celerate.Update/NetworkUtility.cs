using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace Celerate.Update
{
    /// <summary>
    /// Ağ durumu tespiti ve özelliklerini kontrol eden sınıf
    /// </summary>
    public class NetworkUtility
    {
        private const double DEFAULT_SPEED_THRESHOLD_MBPS = 1.0; // Düşük hız eşiği (Mbps)
        
        /// <summary>
        /// Ağ durumunu kontrol eder
        /// </summary>
        public static async Task<NetworkStatus> CheckNetworkStatusAsync()
        {
            var status = new NetworkStatus
            {
                IsConnected = false,
                ConnectionType = NetworkType.Unknown,
                IsMetered = false,
                EstimatedSpeed = 0,
                IsSuitableForDownload = false
            };
            
            try
            {
                // Ağ bağlantısı kontrolü
                bool isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
                status.IsConnected = isNetworkAvailable;
                
                if (!isNetworkAvailable)
                {
                    status.UnsuitabilityReason = "Ağ bağlantısı yok";
                    return status;
                }
                
                // Ağ arayüzlerini al
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var activeInterface = interfaces.FirstOrDefault(
                    i => i.OperationalStatus == OperationalStatus.Up && 
                         (i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || 
                          i.NetworkInterfaceType == NetworkInterfaceType.Ethernet));
                
                if (activeInterface == null)
                {
                    status.UnsuitabilityReason = "Aktif ağ arayüzü bulunamadı";
                    return status;
                }
                
                // Ağ türünü belirle
                switch (activeInterface.NetworkInterfaceType)
                {
                    case NetworkInterfaceType.Wireless80211:
                        status.ConnectionType = NetworkType.WiFi;
                        break;
                    case NetworkInterfaceType.Ethernet:
                        status.ConnectionType = NetworkType.Ethernet;
                        break;
                    // 'Wwan' yok, bunun yerine mobil türleri için bir kontrol ekleyelim
                    case NetworkInterfaceType.Ppp:
                    case NetworkInterfaceType.GenericModem:
                    case NetworkInterfaceType.Wwanpp:
                    case NetworkInterfaceType.Slip:
                        status.ConnectionType = NetworkType.Mobile;
                        break;
                    default:
                        status.ConnectionType = NetworkType.Other;
                        break;
                }
                
                // Windows NetworkCostType API'sini kullanarak ölçülü bağlantı kontrolü yapabilir
                // Bu örnekte basit bir tahmin yapıyoruz (mobil ağların ölçülü olma ihtimali yüksek)
                status.IsMetered = status.ConnectionType == NetworkType.Mobile;
                
                // Hız tahmini yap
                status.EstimatedSpeed = EstimateNetworkSpeed(activeInterface);
                
                // Güncelleme indirmesi için uygun mu kontrol et
                bool speedOk = status.EstimatedSpeed > DEFAULT_SPEED_THRESHOLD_MBPS;
                bool costOk = !status.IsMetered;
                
                status.IsSuitableForDownload = speedOk && costOk;
                
                if (!speedOk)
                {
                    status.UnsuitabilityReason = "Ağ hızı çok düşük";
                }
                else if (!costOk)
                {
                    status.UnsuitabilityReason = "Ölçülü bağlantı tespit edildi";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ağ durumu kontrolü hatası: {ex.Message}");
                status.UnsuitabilityReason = "Ağ durumu belirlenirken hata oluştu";
            }
            
            // Hız testi yaparak daha doğru sonuçlar elde edebiliriz
            if (status.IsConnected && status.EstimatedSpeed <= 0)
            {
                await RunBasicSpeedTestAsync(status);
            }
            
            return status;
        }
        
        /// <summary>
        /// Ağ hızını tahmin eder
        /// </summary>
        private static double EstimateNetworkSpeed(NetworkInterface networkInterface)
        {
            try
            {
                // NIC'in hızını al (bps)
                long speed = networkInterface.Speed;
                
                // Negatif değer dönerse (sınırsız veya bilinmeyen hız) varsayılan bir değer kullan
                if (speed < 0)
                {
                    return 0;
                }
                
                // bps'den Mbps'ye çevir
                return speed / 1_000_000.0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ağ hızı tahmini hatası: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Basit bir hız testi yapar
        /// </summary>
        private static async Task RunBasicSpeedTestAsync(NetworkStatus status)
        {
            try
            {
                // Basit bir hız testi için küçük bir dosya indir ve hızı ölç
                var httpClient = new System.Net.Http.HttpClient();
                string testUrl = "https://www.google.com/images/branding/googlelogo/1x/googlelogo_color_272x92dp.png";
                
                Stopwatch sw = Stopwatch.StartNew();
                byte[] data = await httpClient.GetByteArrayAsync(testUrl);
                sw.Stop();
                
                // Indirilen veri boyutu (byte)
                double downloadedBytes = data.Length;
                
                // Geçen süre (saniye)
                double elapsedSeconds = sw.ElapsedMilliseconds / 1000.0;
                
                if (elapsedSeconds > 0)
                {
                    // Hızı hesapla (Mbps)
                    double speedMbps = (downloadedBytes * 8) / (elapsedSeconds * 1_000_000);
                    status.EstimatedSpeed = speedMbps;
                    
                    // Hıza göre uygunluğu güncelle
                    bool speedOk = status.EstimatedSpeed > DEFAULT_SPEED_THRESHOLD_MBPS;
                    bool costOk = !status.IsMetered;
                    status.IsSuitableForDownload = speedOk && costOk;
                    
                    if (!speedOk && string.IsNullOrEmpty(status.UnsuitabilityReason))
                    {
                        status.UnsuitabilityReason = "Ağ hızı çok düşük";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hız testi hatası: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Bağlantı türüne göre önerilen indirme boyutu sınırını döndürür (byte)
        /// </summary>
        public static long GetRecommendedDownloadSizeLimit(NetworkType connectionType, bool isMetered)
        {
            const long MB = 1024 * 1024;
            
            // Ölçülü bağlantılar için daha düşük limit
            if (isMetered)
            {
                return 50 * MB; // 50 MB
            }
            
            // Bağlantı türüne göre
            switch (connectionType)
            {
                case NetworkType.Ethernet:
                    return 1024 * MB; // 1 GB
                case NetworkType.WiFi:
                    return 500 * MB; // 500 MB
                case NetworkType.Mobile:
                    return 100 * MB; // 100 MB
                default:
                    return 200 * MB; // 200 MB
            }
        }
        
        /// <summary>
        /// Bağlantı türüne göre önerilen buffer boyutunu döndürür
        /// </summary>
        public static int GetRecommendedBufferSize(NetworkType connectionType, double estimatedSpeedMbps)
        {
            // Yüksek hızlı bağlantılarda daha büyük buffer
            if (estimatedSpeedMbps > 50)
            {
                return 32768; // 32 KB
            }
            else if (estimatedSpeedMbps > 10)
            {
                return 16384; // 16 KB
            }
            else
            {
                return 8192; // 8 KB
            }
        }
        
        /// <summary>
        /// Ping testi yapar
        /// </summary>
        public static async Task<long> MeasurePingAsync(string host = "8.8.8.8", int count = 4)
        {
            try
            {
                using (var ping = new Ping())
                {
                    long totalTime = 0;
                    int successCount = 0;
                    
                    for (int i = 0; i < count; i++)
                    {
                        var reply = await ping.SendPingAsync(host, 1000);
                        
                        if (reply.Status == IPStatus.Success)
                        {
                            totalTime += reply.RoundtripTime;
                            successCount++;
                        }
                        
                        await Task.Delay(100);
                    }
                    
                    return successCount > 0 ? totalTime / successCount : -1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ping testi hatası: {ex.Message}");
                return -1;
            }
        }
    }
} 