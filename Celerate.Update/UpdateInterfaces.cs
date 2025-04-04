using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Celerate.Update
{
    /// <summary>
    /// DLL ile ana uygulama arasında dosya seçimi işlemleri için kullanılacak arayüz
    /// </summary>
    public interface IFilePickerService
    {
        /// <summary>
        /// MSIX dosyasını seçmek için dosya seçici açar
        /// </summary>
        /// <returns>Seçilen dosya yolu ve dosya adı, iptal edilirse null</returns>
        Task<(string? FilePath, string? FileName)> PickMsixFileAsync(Window parentWindow);
    }

    /// <summary>
    /// Güncelleme işlemleri için genişletilmiş servis
    /// </summary>
    public interface IUpdateService
    {
        /// <summary>
        /// Güncelleme olup olmadığını kontrol eder
        /// </summary>
        Task<(bool HasUpdate, string CurrentVersion, string LatestVersion)> CheckForUpdateAvailability();

        /// <summary>
        /// Otomatik güncelleme işlemini başlatır
        /// </summary>
        Task CheckForUpdates(UpdateProgress progress, DispatcherQueue dispatcher, Action onComplete);

        /// <summary>
        /// Manuel güncelleme işlemini başlatır, dosya seçici servis gerektirir
        /// </summary>
        Task ManualUpdate(UpdateProgress progress, DispatcherQueue dispatcher, Action onComplete, Window parentWindow, IFilePickerService filePickerService);
        
        /// <summary>
        /// Eski güncelleme dosyalarını temizler
        /// </summary>
        Task CleanUpdateFilesAsync();
        
        /// <summary>
        /// Güncelleme sürüm notlarını (changelog) getirir
        /// </summary>
        Task<ChangelogInfo> GetChangelogAsync(string version);
        
        /// <summary>
        /// Güncellemeyi belirli bir zamana programlar
        /// </summary>
        Task ScheduleUpdateAsync(DateTime scheduledTime);
        
        /// <summary>
        /// Bağlantı kesintisinden sonra indirmeyi kaldığı yerden devam ettirir
        /// </summary>
        Task ResumeDownloadAsync(UpdateProgress progress, DispatcherQueue dispatcher, Action onComplete);
        
        /// <summary>
        /// Güncelleme hatası raporlar
        /// </summary>
        Task ReportUpdateErrorAsync(UpdateError error);
        
        /// <summary>
        /// Güncel ağ durumunu kontrol eder ve uygun güncelleme stratejisini belirler
        /// </summary>
        Task<NetworkStatus> CheckNetworkStatusAsync();
        
        /// <summary>
        /// İndirilen dosyanın hash değerini kontrol eder
        /// </summary>
        Task<bool> VerifyFileIntegrityAsync(string filePath, string expectedHash);
        
        /// <summary>
        /// UI tema ayarlarını değiştirir
        /// </summary>
        void SetThemeSettings(UpdateThemeSettings themeSettings);
    }

    /// <summary>
    /// Uygulama yaşam döngüsü kontrolü için interface
    /// </summary>
    public interface IApplicationLifecycle
    {
        /// <summary>
        /// Uygulamayı sonlandırır
        /// </summary>
        void ShutdownApplication();
    }
    
    /// <summary>
    /// Değişiklik listesi bilgilerini içeren sınıf
    /// </summary>
    public class ChangelogInfo
    {
        /// <summary>
        /// Sürüm numarası
        /// </summary>
        public string Version { get; set; } = string.Empty;
        
        /// <summary>
        /// Yayın tarihi
        /// </summary>
        public DateTime ReleaseDate { get; set; }
        
        /// <summary>
        /// Yeni özellikler listesi
        /// </summary>
        public List<ChangelogItem> NewFeatures { get; set; } = new List<ChangelogItem>();
        
        /// <summary>
        /// İyileştirmeler listesi
        /// </summary>
        public List<ChangelogItem> Improvements { get; set; } = new List<ChangelogItem>();
        
        /// <summary>
        /// Hata düzeltmeleri listesi
        /// </summary>
        public List<ChangelogItem> BugFixes { get; set; } = new List<ChangelogItem>();
        
        /// <summary>
        /// Güvenlik güncellemeleri
        /// </summary>
        public List<ChangelogItem> SecurityUpdates { get; set; } = new List<ChangelogItem>();
        
        /// <summary>
        /// Ham markdown formatındaki değişiklik listesi
        /// </summary>
        public string RawMarkdown { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Değişiklik listesindeki bir öğeyi temsil eder
    /// </summary>
    public class ChangelogItem
    {
        /// <summary>
        /// Değişiklik açıklaması
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// İlgili GitHub issue numarası veya URL'si
        /// </summary>
        public string IssueReference { get; set; } = string.Empty;
        
        /// <summary>
        /// Önem seviyesi
        /// </summary>
        public ChangeImportance Importance { get; set; } = ChangeImportance.Normal;
    }
    
    /// <summary>
    /// Değişikliğin önem seviyesi
    /// </summary>
    public enum ChangeImportance
    {
        Low,
        Normal,
        High,
        Critical
    }
    
    /// <summary>
    /// Güncelleme hatası bilgilerini içeren sınıf
    /// </summary>
    public class UpdateError
    {
        /// <summary>
        /// Hata kodu
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;
        
        /// <summary>
        /// Hata mesajı
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// Ayrıntılı hata bilgileri
        /// </summary>
        public string Details { get; set; } = string.Empty;
        
        /// <summary>
        /// Hata zamanı
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        /// <summary>
        /// Uygulama sürümü
        /// </summary>
        public string AppVersion { get; set; } = string.Empty;
        
        /// <summary>
        /// Hedef güncelleme sürümü
        /// </summary>
        public string TargetVersion { get; set; } = string.Empty;
        
        /// <summary>
        /// Sistem bilgileri
        /// </summary>
        public Dictionary<string, string> SystemInfo { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// Yığın izi
        /// </summary>
        public string StackTrace { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Ağ durumu bilgilerini içeren sınıf
    /// </summary>
    public class NetworkStatus
    {
        /// <summary>
        /// Ağ bağlantısı var mı
        /// </summary>
        public bool IsConnected { get; set; }
        
        /// <summary>
        /// Ağ bağlantı türü
        /// </summary>
        public NetworkType ConnectionType { get; set; }
        
        /// <summary>
        /// Ölçülü bağlantı mı
        /// </summary>
        public bool IsMetered { get; set; }
        
        /// <summary>
        /// Tahmini bağlantı hızı (Mbps)
        /// </summary>
        public double EstimatedSpeed { get; set; }
        
        /// <summary>
        /// Güncelleme indirmesi için uygun mu
        /// </summary>
        public bool IsSuitableForDownload { get; set; }
        
        /// <summary>
        /// Uygun olmama nedeni
        /// </summary>
        public string UnsuitabilityReason { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Ağ bağlantı türleri
    /// </summary>
    public enum NetworkType
    {
        Unknown,
        Ethernet,
        WiFi,
        Mobile,
        Other
    }
    
    /// <summary>
    /// Güncelleme tema ayarları
    /// </summary>
    public class UpdateThemeSettings
    {
        /// <summary>
        /// Birincil renk (HEX formatında)
        /// </summary>
        public string PrimaryColor { get; set; } = "#2196F3";
        
        /// <summary>
        /// Vurgu rengi (HEX formatında)
        /// </summary>
        public string AccentColor { get; set; } = "#FF5252";
        
        /// <summary>
        /// Arka plan rengi (HEX formatında)
        /// </summary>
        public string BackgroundColor { get; set; } = "#2f3640";
        
        /// <summary>
        /// Metin rengi (HEX formatında)
        /// </summary>
        public string TextColor { get; set; } = "#FFFFFF";
        
        /// <summary>
        /// İkincil metin rengi (HEX formatında)
        /// </summary>
        public string SecondaryTextColor { get; set; } = "#CCCCCC";
        
        /// <summary>
        /// Logo görüntü yolu
        /// </summary>
        public string LogoPath { get; set; } = string.Empty;
        
        /// <summary>
        /// İlerleme çubuğu kalınlığı
        /// </summary>
        public double ProgressBarThickness { get; set; } = 8;
        
        /// <summary>
        /// Yazı tipi ailesi
        /// </summary>
        public string FontFamily { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// HTTP isteklerini yönetmek için özel servis
    /// </summary>
    public interface IHttpClientService
    {
        /// <summary>
        /// HTTP GET isteği yapar
        /// </summary>
        Task<string> GetStringAsync(string url);
        
        /// <summary>
        /// Dosya indirmeyi başlatır ve ilerlemeyi raporlar
        /// </summary>
        Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<DownloadProgress> progress, bool resumeIfExists = true);
        
        /// <summary>
        /// Belirtilen aralıktaki veriyi indirir (range request)
        /// </summary>
        Task<bool> DownloadRangeAsync(string url, string destinationPath, long from, long to, IProgress<DownloadProgress> progress);
        
        /// <summary>
        /// Dosya boyutunu öğrenir
        /// </summary>
        Task<long> GetFileSizeAsync(string url);
        
        /// <summary>
        /// Dosya hash değerini alır
        /// </summary>
        Task<string> GetFileHashAsync(string url);
    }
    
    /// <summary>
    /// İndirme ilerlemesi bilgilerini içeren sınıf
    /// </summary>
    public class DownloadProgress
    {
        /// <summary>
        /// Toplam bayt sayısı
        /// </summary>
        public long TotalBytes { get; set; }
        
        /// <summary>
        /// İndirilen bayt sayısı
        /// </summary>
        public long DownloadedBytes { get; set; }
        
        /// <summary>
        /// İlerleme yüzdesi
        /// </summary>
        public double ProgressPercentage => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
        
        /// <summary>
        /// İndirme hızı (bayt/saniye)
        /// </summary>
        public long BytesPerSecond { get; set; }
        
        /// <summary>
        /// Tahmini kalan süre (saniye)
        /// </summary>
        public double EstimatedRemainingSeconds => BytesPerSecond > 0 ? (TotalBytes - DownloadedBytes) / (double)BytesPerSecond : 0;
    }
} 