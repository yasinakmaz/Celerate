namespace Celerate.Services
{
    public static class WindowNative
    {
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto, PreserveSig = true, SetLastError = false)]
        public static extern IntPtr GetActiveWindow();

        public static IntPtr GetWindowHandle(Window window)
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
    }

    public static class WindowInteropHelper
    {
        public static void Initialize<T>(T obj, Window window) where T : class
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(obj, hwnd);
        }
    }
} 