using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sefim.ViewModels
{
    public class LanguageSelectionViewModel
    {
        public ObservableCollection<LanguageModel> Languages { get; set; }

        public LanguageSelectionViewModel()
        {
            Languages = new ObservableCollection<LanguageModel>
            {
                new LanguageModel { CountryName = "Türkçe", LanguageName = "Turkish", LanguageCode = "TR", FlagImage = "turkey_flag.png" },
                new LanguageModel { CountryName = "English", LanguageName = "English", LanguageCode = "EN", FlagImage = "usa_flag.png" },
                new LanguageModel { CountryName = "Deutsch", LanguageName = "German", LanguageCode = "DE", FlagImage = "germany_flag.png" },
                new LanguageModel { CountryName = "Français", LanguageName = "French", LanguageCode = "FR", FlagImage = "france_flag.png" }
            };
        }
    }
}
