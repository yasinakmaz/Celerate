namespace Sefim.Services.AuthService
{
    public class AuthService
    {
        public async Task<bool> IsAuthenticatedAsync()
        {
            var authstate = Preferences.Default.Get<bool>(PublicService.AuthStateKey, false);
            return authstate;
        }
        public async Task LoginAsync(string userId, string username, string password)
        {
            Preferences.Set(PublicService.AuthStateKey, true);
            await SecureStorage.SetAsync(PublicService.UserId, userId);
            await SecureStorage.SetAsync(PublicService.UserDisplayName, username);
            await SecureStorage.SetAsync(PublicService.Password, password);
        }
        public async Task LogoutAsync(string userId, string username, string password)
        {
            Preferences.Set(PublicService.AuthStateKey, false);
            SecureStorage.RemoveAll();
        }
    }
}
