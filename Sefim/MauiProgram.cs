namespace Sefim
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("Quicksand-Bold.ttf", "FontBold");
                    fonts.AddFont("Quicksand-Light.ttf", "FontLight");
                    fonts.AddFont("Quicksand-Medium.ttf", "FontMedium");
                    fonts.AddFont("Quicksand-Regular.ttf", "FontReguar");
                    fonts.AddFont("Quicksand-SemiBold.ttf", "FontSemiBold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton(new TranslationService("570a37b9-8c20-4d0a-97bb-67e772947b7c:fx"));
            builder.Services.AddSingleton<SefimWindows>();
            builder.Services.AddSingleton<StartedPage>();
            builder.Services.AddSingleton<LoginServicePage>();
            builder.Services.AddSingleton<LoginPage>();

            return builder.Build();
        }
    }
}
