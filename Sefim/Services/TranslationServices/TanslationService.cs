using System.Net.Http.Json;

namespace Sefim.Services.TranslationServices
{
    public class TranslationService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public TranslationService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api-free.deepl.com/v2/")
            };
        }

        public async Task<string> TranslateTextAsync(string text, string targetLanguage)
        {
            var requestData = new
            {
                text = text,
                target_lang = targetLanguage
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "translate");
            requestMessage.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
            requestMessage.Content = JsonContent.Create(requestData);

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DeepLTranslationResponse>();
            return result?.Translations?[0]?.Text ?? string.Empty;
        }
    }

    public class DeepLTranslationResponse
    {
        public Translation[] Translations { get; set; }
    }

    public class Translation
    {
        public string Text { get; set; }
    }
}
