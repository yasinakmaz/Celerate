# Celerate.Update DLL Kullanım Kılavuzu

Bu DLL, Celerate uygulaması için otomatik güncelleme işlevselliğini sağlar.

## Özellikler

- GitHub Releases üzerinden otomatik güncelleme kontrolü
- MSIX paketleri indirme ve kurulum
- İlerleme takibi ve hata yönetimi
- Manuel güncelleme desteği

## Kullanım

DLL'in ana uygulamanıza entegre edilmesi için aşağıdaki adımları izleyin:

### 1. Referans Ekleme

Projenizdeki referanslara Celerate.Update.dll dosyasını ekleyin.

### 2. Uygulama Yaşam Döngüsü Implementasyonu

IApplicationLifecycle interface'ini uygulayan bir sınıf oluşturun:

```csharp
using Celerate.Update;

public class AppLifecycle : IApplicationLifecycle
{
    public void ShutdownApplication()
    {
        // Uygulamayı kapatmak için gerekli kodlar
        Application.Current.Exit();
    }
}
```

### 3. Dosya Seçici Implementasyonu

IFilePickerService interface'ini uygulayan bir sınıf oluşturun:

```csharp
using Celerate.Update;

public class FilePickerService : IFilePickerService
{
    public async Task<(string? FilePath, string? FileName)> PickMsixFileAsync(Window parentWindow)
    {
        var openPicker = new FileOpenPicker();
        WindowInteropHelper.Initialize(openPicker, parentWindow);

        openPicker.FileTypeFilter.Add(".msix");
        openPicker.SuggestedStartLocation = PickerLocationId.Downloads;

        var file = await openPicker.PickSingleFileAsync();
        
        if (file != null)
        {
            return (file.Path, file.Name);
        }
        
        return (null, null);
    }
}
```

### 4. UpdateService Kullanımı

Uygulamanızın başlangıcında veya güncelleme kontrolü yapmak istediğiniz yerde:

```csharp
// Service oluştur
var appLifecycle = new AppLifecycle();
var updateService = new UpdateServiceImplementation(appLifecycle);

// Güncelleme kontrolü
var (hasUpdate, currentVersion, latestVersion) = await updateService.CheckForUpdateAvailability();

if (hasUpdate)
{
    // Kullanıcıya güncelleme olduğu bilgisini ver
    // Güncelleme işlemini başlat
    var progress = new UpdateProgress();
    await updateService.CheckForUpdates(progress, DispatcherQueue.GetForCurrentThread(), OnUpdateComplete);
}
```

### 5. Manuel Güncelleme

Manuel güncelleme için:

```csharp
var filePickerService = new FilePickerService();
var progress = new UpdateProgress();
await updateService.ManualUpdate(progress, DispatcherQueue.GetForCurrentThread(), OnUpdateComplete, this, filePickerService);
```

## Notlar

- Bu DLL Windows 10/11 sistemlerinde WinUI3 uygulamaları için tasarlanmıştır
- Microsoft.WindowsAppSDK paketine bağımlıdır
- Güncelleme işlemleri arka planda çalışır, UI thread'ini bloke etmez
- İlerleme bilgisi UpdateProgress nesnesi üzerinden takip edilebilir 