namespace Celerate.Services
{
    /// <summary>
    /// Uygulama yaşam döngüsü kontrolü için IApplicationLifecycle uygulayan sınıf
    /// </summary>
    public class AppLifecycle : IApplicationLifecycle
    {
        /// <summary>
        /// Uygulamayı sonlandırır
        /// </summary>
        public void ShutdownApplication()
        {
            try
            {
                // Uygulama prosesini sonlandır
                Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShutdownApplication error: {ex.Message}");
                
                // Proses sonlandırma başarısız olursa, alternatif yöntem
                try
                {
                    Application.Current.Exit();
                }
                catch
                {
                    // Tüm yöntemler başarısız olursa en son çare olarak Environment.Exit kullan
                    Environment.Exit(0);
                }
            }
        }
    }
} 