namespace TheBleedingDeacons.Intergroup.Register;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();        
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {

        return new Window(new AppShell())
        {
            Title = "Intergroup"
        };
    }

    public static Window MainWindow => Current?.Windows?.FirstOrDefault();

    // Update the root page in the single window (replaces deprecated MainPage setter)
    public static void SetRootPage(Page page)
    {
        if (MainWindow != null)
        {
            MainWindow.Page = page;
        }
    }

    // Get the current page from the single window
    public static Page CurrentPage => MainWindow?.Page;
}
