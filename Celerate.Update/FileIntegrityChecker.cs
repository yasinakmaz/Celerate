using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Celerate.Update
{
    /// <summary>
    /// Dosya bütünlüğünü kontrol eden sınıf
    /// </summary>
    public class FileIntegrityChecker
    {
        /// <summary>
        /// Dosyanın SHA-256 hash değerini hesaplar
        /// </summary>
        public static async Task<string> CalculateSha256Async(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Dosya bulunamadı.", filePath);
            }
            
            try
            {
                using (var sha256 = SHA256.Create())
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    byte[] hash = await sha256.ComputeHashAsync(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hash hesaplama hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Dosyanın SHA-1 hash değerini hesaplar
        /// </summary>
        public static async Task<string> CalculateSha1Async(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Dosya bulunamadı.", filePath);
            }
            
            try
            {
                using (var sha1 = SHA1.Create())
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    byte[] hash = await sha1.ComputeHashAsync(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hash hesaplama hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Dosyanın MD5 hash değerini hesaplar
        /// </summary>
        public static async Task<string> CalculateMd5Async(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Dosya bulunamadı.", filePath);
            }
            
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    byte[] hash = await md5.ComputeHashAsync(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hash hesaplama hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Hesaplanan hash ile beklenen hash'i karşılaştırır
        /// </summary>
        public static bool VerifyHash(string calculatedHash, string expectedHash)
        {
            if (string.IsNullOrEmpty(calculatedHash) || string.IsNullOrEmpty(expectedHash))
            {
                return false;
            }
            
            // Hash değerlerini normalleştir (hepsini küçük harfe çevir, boşlukları ve tire işaretlerini kaldır)
            calculatedHash = NormalizeHash(calculatedHash);
            expectedHash = NormalizeHash(expectedHash);
            
            return string.Equals(calculatedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Hash değerini normalleştirir
        /// </summary>
        private static string NormalizeHash(string hash)
        {
            return hash.Replace("-", "").Replace(" ", "").ToLowerInvariant();
        }
        
        /// <summary>
        /// Hash dosyasından hash değerini okur
        /// </summary>
        public static async Task<string> ReadHashFromFileAsync(string hashFilePath)
        {
            if (!File.Exists(hashFilePath))
            {
                throw new FileNotFoundException("Hash dosyası bulunamadı.", hashFilePath);
            }
            
            try
            {
                string content = await File.ReadAllTextAsync(hashFilePath);
                
                // Hash dosyası genellikle "hash dosya_adı" formatındadır
                // veya sadece hash değerini içerir
                string[] parts = content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    return NormalizeHash(parts[0]);
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hash dosyası okuma hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Dosya bütünlüğünü kontrol eder, hash dosyası formatını otomatik algılar
        /// </summary>
        public static async Task<bool> VerifyFileIntegrityAsync(string filePath, string hashFilePath)
        {
            if (!File.Exists(filePath) || !File.Exists(hashFilePath))
            {
                return false;
            }
            
            try
            {
                string expectedHash = await ReadHashFromFileAsync(hashFilePath);
                
                // Hash uzunluğuna göre algoritma seçimi
                string actualHash;
                switch (expectedHash.Length)
                {
                    case 32: // MD5
                        actualHash = await CalculateMd5Async(filePath);
                        break;
                    case 40: // SHA-1
                        actualHash = await CalculateSha1Async(filePath);
                        break;
                    case 64: // SHA-256
                        actualHash = await CalculateSha256Async(filePath);
                        break;
                    default:
                        // Varsayılan olarak SHA-256 kullan
                        actualHash = await CalculateSha256Async(filePath);
                        break;
                }
                
                return VerifyHash(actualHash, expectedHash);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dosya bütünlük kontrolü hatası: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Dosya bütünlüğünü belirtilen hash ile kontrol eder
        /// </summary>
        public static async Task<bool> VerifyFileIntegrityAsync(string filePath, string expectedHash, HashAlgorithmType algorithmType)
        {
            if (!File.Exists(filePath) || string.IsNullOrEmpty(expectedHash))
            {
                return false;
            }
            
            try
            {
                string actualHash;
                
                switch (algorithmType)
                {
                    case HashAlgorithmType.MD5:
                        actualHash = await CalculateMd5Async(filePath);
                        break;
                    case HashAlgorithmType.SHA1:
                        actualHash = await CalculateSha1Async(filePath);
                        break;
                    case HashAlgorithmType.SHA256:
                    default:
                        actualHash = await CalculateSha256Async(filePath);
                        break;
                }
                
                return VerifyHash(actualHash, expectedHash);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dosya bütünlük kontrolü hatası: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Dosya hash değerini bir dosyaya yazar
        /// </summary>
        public static async Task WriteHashToFileAsync(string filePath, string hashFilePath, HashAlgorithmType algorithmType)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Dosya bulunamadı.", filePath);
            }
            
            try
            {
                string hash;
                
                switch (algorithmType)
                {
                    case HashAlgorithmType.MD5:
                        hash = await CalculateMd5Async(filePath);
                        break;
                    case HashAlgorithmType.SHA1:
                        hash = await CalculateSha1Async(filePath);
                        break;
                    case HashAlgorithmType.SHA256:
                    default:
                        hash = await CalculateSha256Async(filePath);
                        break;
                }
                
                string fileName = Path.GetFileName(filePath);
                await File.WriteAllTextAsync(hashFilePath, $"{hash} {fileName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hash dosyası yazma hatası: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// MSIX paketinin dijital imzasını doğrular
        /// </summary>
        public static bool VerifyMsixSignature(string msixFilePath)
        {
            if (!File.Exists(msixFilePath))
            {
                throw new FileNotFoundException("MSIX dosyası bulunamadı.", msixFilePath);
            }
            
            try
            {
                // PowerShell ile imza doğrulaması yap
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-Command \"Get-AuthenticodeSignature '{msixFilePath}' | Select-Object -ExpandProperty Status\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                // İmza durumunu kontrol et
                return output.Contains("Valid", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MSIX imza doğrulama hatası: {ex.Message}");
                return false;
            }
        }
    }
    
    /// <summary>
    /// Hash algoritma türleri
    /// </summary>
    public enum HashAlgorithmType
    {
        MD5,
        SHA1,
        SHA256
    }
} 