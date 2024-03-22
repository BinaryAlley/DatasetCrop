#region ========================================================================= USING =====================================================================================
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#endregion

namespace DatasetCrop;

public partial class App : Application
{
    #region ================================================================== FIELD MEMBERS ================================================================================
    public static StyleManager? StyleManager { get; set; }
    #endregion

    #region ===================================================================== METHODS ===================================================================================
    /// <summary>
    /// Initializes the application by loading XAML etc.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Framework initialization code
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        StyleManager = new StyleManager(this);
        MainWindow.ThemeChanged += StyleManager.SwitchThemeByIndex;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
    #endregion
}