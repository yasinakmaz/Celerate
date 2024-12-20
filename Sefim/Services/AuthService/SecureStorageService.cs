namespace Sefim.Services.AuthService
{
    public class SecureStorageService
    {
        public static bool Started;
        public static bool AuthState;
        public static string UserId;
        public static string UserDisplayName;
        public static string Password;
        public async static void LoadTaskSecureStorage()
        {
            Started = Preferences.Get(PublicService.Started, Started);
            AuthState = Preferences.Get(PublicService.AuthStateKey, AuthState);
            UserId = await SecureStorage.GetAsync(PublicService.UserId) ?? "0";
            UserDisplayName = await SecureStorage.GetAsync(PublicService.UserDisplayName) ?? "DisplayName";
            Password = await SecureStorage.GetAsync(PublicService.Password) ?? "Password";
        }
        public async static void GetToPublicService()
        {
            Started = PublicSettings.Started;
            AuthState = PublicSettings.AuthState;
            UserId = PublicSettings.UserId;
            UserDisplayName = PublicSettings.UserDisplayName;
            Password = PublicSettings.Password;
        }
    }
}
