namespace ImageWatermarkPortable;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ImageWatermarkPortable.Forms.MainForm());
    }
}
