namespace Celerate.Services
{
    /// <summary>
    /// Dosya seçim işlemleri için IFilePickerService uygulayan sınıf
    /// </summary>
    public class FilePickerService : IFilePickerService
    {
        /// <summary>
        /// MSIX dosyasını seçmek için dosya seçici açar
        /// </summary>
        /// <returns>Seçilen dosya yolu ve dosya adı, iptal edilirse null</returns>
        public async Task<(string? FilePath, string? FileName)> PickMsixFileAsync(Window parentWindow)
        {
            try
            {
                var openPicker = new FileOpenPicker();
                
                // WinUI3'te FileOpenPicker'ı pencere ile ilişkilendirme
                WinRT.Interop.InitializeWithWindow.Initialize(openPicker, 
                    WinRT.Interop.WindowNative.GetWindowHandle(parentWindow));

                openPicker.FileTypeFilter.Add(".msix");
                openPicker.SuggestedStartLocation = PickerLocationId.Downloads;

                var file = await openPicker.PickSingleFileAsync();
                
                if (file != null)
                {
                    return (file.Path, file.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FilePickerService error: {ex.Message}");
            }
            
            return (null, null);
        }
    }
} 