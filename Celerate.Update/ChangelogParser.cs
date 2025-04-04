using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Celerate.Update
{
    /// <summary>
    /// Değişiklik listesi (changelog) dosyalarını parse eden sınıf
    /// </summary>
    public class ChangelogParser
    {
        private static readonly Regex VersionHeaderRegex = new Regex(@"^#+\s*(?:v|version)?\s*(\d+\.\d+\.\d+(?:[.\-+][0-9a-zA-Z\-.]+)?)\s*(?:\((.*?)\))?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        private static readonly Regex DateRegex = new Regex(@"(\d{4}-\d{2}-\d{2})|(\d{1,2}\s+\w+\s+\d{4})");
        private static readonly Regex SectionHeaderRegex = new Regex(@"^#+\s*(added|new|feature|improve|enhanced|fixed|bugfix|security|removed|deprecated|breaking|change)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        private static readonly Regex ListItemRegex = new Regex(@"^[\s-]*[*+-]\s+(.+)$", RegexOptions.Multiline);
        private static readonly Regex IssueReferenceRegex = new Regex(@"(?:issue|pr|pull request|#)\s*#?(\d+)", RegexOptions.IgnoreCase);
        
        /// <summary>
        /// Markdown formatındaki changelog dosyasını parse eder
        /// </summary>
        public static ChangelogInfo ParseMarkdown(string markdownContent, string targetVersion = null)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                return new ChangelogInfo();
            }
            
            // Tüm sürümleri bul
            var versionMatches = VersionHeaderRegex.Matches(markdownContent);
            
            if (versionMatches.Count == 0)
            {
                return new ChangelogInfo { RawMarkdown = markdownContent };
            }
            
            // Belirli bir sürüm için arama yapılıyorsa
            if (!string.IsNullOrEmpty(targetVersion))
            {
                foreach (Match versionMatch in versionMatches)
                {
                    string version = versionMatch.Groups[1].Value;
                    
                    if (NormalizeVersion(version) == NormalizeVersion(targetVersion))
                    {
                        // Sürüm aralığını belirle
                        int startIndex = versionMatch.Index;
                        int endIndex = markdownContent.Length;
                        
                        // Sonraki sürüm başlığını bul (varsa)
                        for (int i = 0; i < versionMatches.Count; i++)
                        {
                            if (versionMatches[i].Index == startIndex && i < versionMatches.Count - 1)
                            {
                                endIndex = versionMatches[i + 1].Index;
                                break;
                            }
                        }
                        
                        // Bu sürümün changelog içeriğini al
                        string versionContent = markdownContent.Substring(startIndex, endIndex - startIndex);
                        return ParseVersionContent(version, versionContent);
                    }
                }
                
                // Hedef sürüm bulunamadı
                return new ChangelogInfo();
            }
            
            // İlk sürüm bilgilerini kullan (en güncel)
            string latestVersion = versionMatches[0].Groups[1].Value;
            
            // Sürüm aralığını belirle
            int latestStartIndex = versionMatches[0].Index;
            int latestEndIndex = markdownContent.Length;
            
            // Sonraki sürüm başlığını bul (varsa)
            if (versionMatches.Count > 1)
            {
                latestEndIndex = versionMatches[1].Index;
            }
            
            // En güncel sürümün changelog içeriğini al
            string latestVersionContent = markdownContent.Substring(latestStartIndex, latestEndIndex - latestStartIndex);
            return ParseVersionContent(latestVersion, latestVersionContent);
        }
        
        /// <summary>
        /// Bir sürüm için changelog içeriğini parse eder
        /// </summary>
        private static ChangelogInfo ParseVersionContent(string version, string versionContent)
        {
            var changelogInfo = new ChangelogInfo
            {
                Version = version,
                RawMarkdown = versionContent
            };
            
            // Yayın tarihini bul
            var dateMatch = DateRegex.Match(versionContent);
            if (dateMatch.Success)
            {
                string dateStr = dateMatch.Value;
                if (DateTime.TryParse(dateStr, out DateTime releaseDate))
                {
                    changelogInfo.ReleaseDate = releaseDate;
                }
            }
            
            // Bölümleri bul
            var sectionMatches = SectionHeaderRegex.Matches(versionContent);
            
            if (sectionMatches.Count == 0)
            {
                // Bölüm başlığı yoksa, tüm liste öğelerini doğrudan ekle
                var listItems = ListItemRegex.Matches(versionContent);
                foreach (Match item in listItems)
                {
                    ParseListItem(item.Groups[1].Value, changelogInfo);
                }
                
                return changelogInfo;
            }
            
            // Her bölümü işle
            for (int i = 0; i < sectionMatches.Count; i++)
            {
                string sectionType = sectionMatches[i].Groups[1].Value.ToLowerInvariant();
                
                int sectionStart = sectionMatches[i].Index + sectionMatches[i].Length;
                int sectionEnd = (i < sectionMatches.Count - 1) ? sectionMatches[i + 1].Index : versionContent.Length;
                
                string sectionContent = versionContent.Substring(sectionStart, sectionEnd - sectionStart);
                
                // Bu bölümdeki liste öğelerini bul
                var listItems = ListItemRegex.Matches(sectionContent);
                
                foreach (Match item in listItems)
                {
                    string description = item.Groups[1].Value.Trim();
                    ChangelogItem changeItem = CreateChangelogItem(description);
                    
                    // Bölüm türüne göre sınıflandır
                    if (sectionType.Contains("add") || sectionType.Contains("new") || sectionType.Contains("feature"))
                    {
                        changelogInfo.NewFeatures.Add(changeItem);
                    }
                    else if (sectionType.Contains("improv") || sectionType.Contains("enhanc") || sectionType.Contains("change"))
                    {
                        changelogInfo.Improvements.Add(changeItem);
                    }
                    else if (sectionType.Contains("fix") || sectionType.Contains("bug"))
                    {
                        changelogInfo.BugFixes.Add(changeItem);
                    }
                    else if (sectionType.Contains("secur"))
                    {
                        changeItem.Importance = ChangeImportance.High;
                        changelogInfo.SecurityUpdates.Add(changeItem);
                    }
                    else
                    {
                        // Diğer bölümler
                        ParseListItem(description, changelogInfo);
                    }
                }
            }
            
            return changelogInfo;
        }
        
        /// <summary>
        /// Liste öğesini işler ve uygun kategoriye ekler
        /// </summary>
        private static void ParseListItem(string description, ChangelogInfo changelogInfo)
        {
            ChangelogItem changeItem = CreateChangelogItem(description);
            
            string lowerDescription = description.ToLowerInvariant();
            
            if (lowerDescription.Contains("fix") || lowerDescription.Contains("bugfix") || lowerDescription.Contains("resolv") || lowerDescription.Contains("solve"))
            {
                changelogInfo.BugFixes.Add(changeItem);
            }
            else if (lowerDescription.Contains("add") || lowerDescription.Contains("new") || lowerDescription.Contains("feature") || lowerDescription.Contains("implement"))
            {
                changelogInfo.NewFeatures.Add(changeItem);
            }
            else if (lowerDescription.Contains("improv") || lowerDescription.Contains("enhanc") || lowerDescription.Contains("updat") || lowerDescription.Contains("optimiz"))
            {
                changelogInfo.Improvements.Add(changeItem);
            }
            else if (lowerDescription.Contains("secur") || lowerDescription.Contains("vulnerab") || lowerDescription.Contains("protect") || lowerDescription.Contains("attack"))
            {
                changeItem.Importance = ChangeImportance.High;
                changelogInfo.SecurityUpdates.Add(changeItem);
            }
            else
            {
                // Tespit edilemeyen kategoriye ekle (varsayılan olarak iyileştirme)
                changelogInfo.Improvements.Add(changeItem);
            }
        }
        
        /// <summary>
        /// Açıklamadan ChangelogItem oluşturur
        /// </summary>
        private static ChangelogItem CreateChangelogItem(string description)
        {
            var changeItem = new ChangelogItem
            {
                Description = description.Trim()
            };
            
            // Issue referanslarını bul
            Match issueMatch = IssueReferenceRegex.Match(description);
            if (issueMatch.Success)
            {
                changeItem.IssueReference = issueMatch.Groups[1].Value;
            }
            
            // Önem seviyesini belirle
            string lowerDescription = description.ToLowerInvariant();
            
            if (lowerDescription.Contains("critical") || lowerDescription.Contains("severe") || lowerDescription.Contains("urgent") || lowerDescription.Contains("major"))
            {
                changeItem.Importance = ChangeImportance.Critical;
            }
            else if (lowerDescription.Contains("important") || lowerDescription.Contains("significant"))
            {
                changeItem.Importance = ChangeImportance.High;
            }
            else if (lowerDescription.Contains("minor") || lowerDescription.Contains("small") || lowerDescription.Contains("trivial"))
            {
                changeItem.Importance = ChangeImportance.Low;
            }
            
            return changeItem;
        }
        
        /// <summary>
        /// Sürümü normalleştirir (karşılaştırma için)
        /// </summary>
        private static string NormalizeVersion(string version)
        {
            // Ön ek veya son ekleri kaldır
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                version = version.Substring(1);
            }
            
            // Semantik sürümleme formatına uygun hale getir
            string[] parts = version.Split('.');
            
            if (parts.Length >= 3)
            {
                // Major.Minor.Patch formatını kullan
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            }
            
            return version;
        }
        
        /// <summary>
        /// GitHub sürüm notlarından Changelog oluşturur
        /// </summary>
        public static ChangelogInfo ParseGitHubReleaseNotes(string releaseNotes, string version, DateTime releaseDate)
        {
            // GitHub sürüm notlarını markdown olarak kabul et
            ChangelogInfo changelog = ParseMarkdown(releaseNotes);
            
            // Eksik bilgileri ekle
            changelog.Version = version;
            changelog.ReleaseDate = releaseDate;
            changelog.RawMarkdown = releaseNotes;
            
            return changelog;
        }
        
        /// <summary>
        /// Changelog dosyasını okur ve parse eder
        /// </summary>
        public static ChangelogInfo ParseFromFile(string filePath, string targetVersion = null)
        {
            if (!File.Exists(filePath))
            {
                return new ChangelogInfo();
            }
            
            try
            {
                string markdownContent = File.ReadAllText(filePath);
                return ParseMarkdown(markdownContent, targetVersion);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Changelog dosyası okuma hatası: {ex.Message}");
                return new ChangelogInfo();
            }
        }
        
        /// <summary>
        /// ChangelogInfo'yu markdown formatına dönüştürür
        /// </summary>
        public static string ToMarkdown(ChangelogInfo changelog)
        {
            if (string.IsNullOrEmpty(changelog.Version))
            {
                return string.Empty;
            }
            
            var sections = new List<string>();
            
            // Başlık ve tarih
            string dateStr = changelog.ReleaseDate != default ? changelog.ReleaseDate.ToString("yyyy-MM-dd") : string.Empty;
            sections.Add($"## v{changelog.Version} {(string.IsNullOrEmpty(dateStr) ? string.Empty : $"({dateStr})")}");
            sections.Add(string.Empty);
            
            // Yeni özellikler
            if (changelog.NewFeatures.Any())
            {
                sections.Add("### Added");
                foreach (var item in changelog.NewFeatures)
                {
                    sections.Add($"- {item.Description} {(string.IsNullOrEmpty(item.IssueReference) ? string.Empty : $"(#{item.IssueReference})")}");
                }
                sections.Add(string.Empty);
            }
            
            // İyileştirmeler
            if (changelog.Improvements.Any())
            {
                sections.Add("### Improved");
                foreach (var item in changelog.Improvements)
                {
                    sections.Add($"- {item.Description} {(string.IsNullOrEmpty(item.IssueReference) ? string.Empty : $"(#{item.IssueReference})")}");
                }
                sections.Add(string.Empty);
            }
            
            // Hata düzeltmeleri
            if (changelog.BugFixes.Any())
            {
                sections.Add("### Fixed");
                foreach (var item in changelog.BugFixes)
                {
                    sections.Add($"- {item.Description} {(string.IsNullOrEmpty(item.IssueReference) ? string.Empty : $"(#{item.IssueReference})")}");
                }
                sections.Add(string.Empty);
            }
            
            // Güvenlik güncellemeleri
            if (changelog.SecurityUpdates.Any())
            {
                sections.Add("### Security");
                foreach (var item in changelog.SecurityUpdates)
                {
                    sections.Add($"- {item.Description} {(string.IsNullOrEmpty(item.IssueReference) ? string.Empty : $"(#{item.IssueReference})")}");
                }
                sections.Add(string.Empty);
            }
            
            return string.Join(Environment.NewLine, sections);
        }
    }
} 