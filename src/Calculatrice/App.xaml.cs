namespace Calculatrice;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		// Thème sombre imposé : l'interface est conçue sur fond foncé.
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell())
		{
			Title = "Calculatrice"
		};

#if WINDOWS
		// Sur ordinateur, la fenêtre démarre au format d'un téléphone ; elle reste redimensionnable
		// (l'élargir fait passer l'interface en disposition paysage).
		window.Width = 400;
		window.Height = 740;
		window.MinimumWidth = 300;
		window.MinimumHeight = 420;
#endif
		return window;
	}
}
