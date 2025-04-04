using Microsoft.Win32.TaskScheduler;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task; // Task sınıfı için belirsizliği gidermek için açık belirtiyoruz

namespace Celerate.Update
{
    /// <summary>
    /// Güncelleme zamanlaması yapan sınıf
    /// </summary>
    public class UpdateScheduler
    {
        private const string TASK_FOLDER = "\\Celerate";
        private const string TASK_NAME = "CelerateAutoUpdate";

        /// <summary>
        /// Belirli bir zamanda çalışacak güncelleme görevi oluşturur
        /// </summary>
        public static bool ScheduleUpdate(DateTime scheduledTime, string msixPath)
        {
            if (string.IsNullOrEmpty(msixPath) || !File.Exists(msixPath))
            {
                Debug.WriteLine("Geçerli MSIX yolu belirtilmedi.");
                return false;
            }
            
            if (scheduledTime <= DateTime.Now)
            {
                Debug.WriteLine("Zamanlama için geçmiş bir tarih belirtildi.");
                return false;
            }
            
            try
            {
                using (TaskService ts = new TaskService())
                {
                    // Görev klasörünü oluştur veya al
                    TaskFolder folder = ts.RootFolder;
                    try
                    {
                        folder = ts.GetFolder(TASK_FOLDER);
                    }
                    catch
                    {
                        folder = ts.RootFolder.CreateFolder(TASK_FOLDER);
                    }
                    
                    // Mevcut görevi temizle (varsa)
                    var existingTasks = folder.GetTasks(new System.Text.RegularExpressions.Regex(TASK_NAME));
                    if (existingTasks.Count > 0)
                    {
                        folder.DeleteTask(TASK_NAME);
                    }
                    
                    // Görev tanımı oluştur
                    TaskDefinition td = ts.NewTask();
                    td.RegistrationInfo.Description = "Celerate otomatik güncelleme görevi";
                    
                    // Zamanlamayı ayarla (tek seferlik)
                    td.Triggers.Add(new TimeTrigger(scheduledTime));
                    
                    // MSIX kurulum komutu
                    string absPath = Path.GetFullPath(msixPath);
                    td.Actions.Add(new ExecAction("powershell.exe", $"-Command \"Start-Process '{absPath}'\"", null));
                    
                    // Görev ayarları
                    td.Settings.Compatibility = TaskCompatibility.V2;
                    td.Settings.DisallowStartIfOnBatteries = false;
                    td.Settings.ExecutionTimeLimit = TimeSpan.Zero; // Sınırsız
                    td.Settings.Hidden = false;
                    td.Settings.Priority = System.Diagnostics.ProcessPriorityClass.Normal;
                    td.Settings.RunOnlyIfNetworkAvailable = true;
                    
                    // Görevi kaydet
                    folder.RegisterTaskDefinition(TASK_NAME, td);
                    
                    Debug.WriteLine($"Güncelleme zamanlandı: {scheduledTime}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme zamanlaması hatası: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Zamanlanmış güncelleme görevini iptal eder
        /// </summary>
        public static bool CancelScheduledUpdate()
        {
            try
            {
                using (TaskService ts = new TaskService())
                {
                    try
                    {
                        TaskFolder folder = ts.GetFolder(TASK_FOLDER);
                        var existingTasks = folder.GetTasks(new System.Text.RegularExpressions.Regex(TASK_NAME));
                        if (existingTasks.Count > 0)
                        {
                            folder.DeleteTask(TASK_NAME);
                            Debug.WriteLine("Zamanlanmış güncelleme görevi iptal edildi.");
                            return true;
                        }
                    }
                    catch
                    {
                        // Klasör veya görev bulunamadı
                        Debug.WriteLine("Zamanlanmış güncelleme görevi bulunamadı.");
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme görevi iptal hatası: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Zamanlanmış güncelleme görevinin durumunu kontrol eder
        /// </summary>
        public static (bool IsScheduled, DateTime ScheduledTime) GetScheduledUpdateStatus()
        {
            try
            {
                using (TaskService ts = new TaskService())
                {
                    try
                    {
                        TaskFolder folder = ts.GetFolder(TASK_FOLDER);
                        var tasks = folder.GetTasks(new System.Text.RegularExpressions.Regex(TASK_NAME));
                        
                        if (tasks.Count > 0)
                        {
                            var scheduledTask = tasks[0];
                            
                            // İlk zamanlayıcıyı bul (tek tetikleyici bekliyoruz)
                            foreach (var trigger in scheduledTask.Definition.Triggers)
                            {
                                if (trigger is TimeTrigger timeTrigger)
                                {
                                    return (true, timeTrigger.StartBoundary);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Klasör veya görev bulunamadı
                    }
                }
                
                return (false, DateTime.MinValue);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme durumu kontrolü hatası: {ex.Message}");
                return (false, DateTime.MinValue);
            }
        }
        
        /// <summary>
        /// Uygulamanın başlangıçta otomatik güncelleme kontrolü yapmasını ayarlar
        /// </summary>
        public static bool SetAutoCheckForUpdates(bool enabled)
        {
            try
            {
                using (TaskService ts = new TaskService())
                {
                    // Görev klasörünü oluştur veya al
                    TaskFolder folder = ts.RootFolder;
                    try
                    {
                        folder = ts.GetFolder(TASK_FOLDER);
                    }
                    catch
                    {
                        folder = ts.RootFolder.CreateFolder(TASK_FOLDER);
                    }
                    
                    // Görevi açıp kapat
                    if (enabled)
                    {
                        // Mevcut görevi temizle (varsa)
                        var existingTasks = folder.GetTasks(new System.Text.RegularExpressions.Regex("CelerateUpdateCheck"));
                        if (existingTasks.Count > 0)
                        {
                            folder.DeleteTask("CelerateUpdateCheck");
                        }
                        
                        // Görev tanımı oluştur
                        TaskDefinition td = ts.NewTask();
                        td.RegistrationInfo.Description = "Celerate otomatik güncelleme kontrolü";
                        
                        // Günlük zamanlamayı ayarla
                        var dailyTrigger = new DailyTrigger { StartBoundary = DateTime.Today + TimeSpan.FromHours(9) }; // Her gün saat 9'da
                        td.Triggers.Add(dailyTrigger);
                        
                        // Güncelleme kontrolü komutu (uygulama yolunu dinamik olarak al)
                        string appPath = Path.Combine(AppContext.BaseDirectory, "Celerate.exe");
                        td.Actions.Add(new ExecAction(appPath, "--check-updates", null));
                        
                        // Görev ayarları
                        td.Settings.Compatibility = TaskCompatibility.V2;
                        td.Settings.DisallowStartIfOnBatteries = false;
                        td.Settings.Hidden = true;
                        td.Settings.Priority = System.Diagnostics.ProcessPriorityClass.Normal;
                        td.Settings.RunOnlyIfNetworkAvailable = true;
                        
                        // Görevi kaydet
                        folder.RegisterTaskDefinition("CelerateUpdateCheck", td);
                        
                        Debug.WriteLine("Otomatik güncelleme kontrolü etkinleştirildi.");
                    }
                    else
                    {
                        // Mevcut görevi sil
                        var existingTasks = folder.GetTasks(new System.Text.RegularExpressions.Regex("CelerateUpdateCheck"));
                        if (existingTasks.Count > 0)
                        {
                            folder.DeleteTask("CelerateUpdateCheck");
                            Debug.WriteLine("Otomatik güncelleme kontrolü devre dışı bırakıldı.");
                        }
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Otomatik güncelleme kontrolü ayar hatası: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Uygun bir güncelleme zamanı önerir (kullanıcı en az aktif olduğu zaman)
        /// </summary>
        public static DateTime SuggestUpdateTime()
        {
            // Varsayılan olarak gece yarısını öner
            DateTime now = DateTime.Now;
            DateTime suggestion = now.Date.AddDays(1).AddHours(1); // Ertesi gün saat 1
            
            // Şu anki saat gece yarısından sonraysa, bir sonraki günün gece yarısını öner
            if (now.Hour >= 1)
            {
                suggestion = now.Date.AddDays(1).AddHours(1);
            }
            
            // Eğer hafta sonu ise, hafta içi olana kadar ertele
            if (suggestion.DayOfWeek == DayOfWeek.Saturday)
            {
                suggestion = suggestion.AddDays(2); // Pazartesi yap
            }
            else if (suggestion.DayOfWeek == DayOfWeek.Sunday)
            {
                suggestion = suggestion.AddDays(1); // Pazartesi yap
            }
            
            return suggestion;
        }
        
        /// <summary>
        /// Asenkron olarak güncelleme zamanlaması yapar
        /// </summary>
        public static async Task<bool> ScheduleUpdateAsync(DateTime scheduledTime, string msixPath)
        {
            return await Task.Run(() => ScheduleUpdate(scheduledTime, msixPath));
        }
        
        /// <summary>
        /// Asenkron olarak zamanlanmış güncelleme görevini iptal eder
        /// </summary>
        public static async Task<bool> CancelScheduledUpdateAsync()
        {
            return await Task.Run(() => CancelScheduledUpdate());
        }
        
        /// <summary>
        /// Asenkron olarak güncelleme görevinin durumunu alır
        /// </summary>
        public static async Task<(bool IsScheduled, DateTime ScheduledTime)> GetScheduledUpdateStatusAsync()
        {
            return await Task.Run(() => GetScheduledUpdateStatus());
        }
    }
} 