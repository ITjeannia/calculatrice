using Calculatrice.Core;

namespace Calculatrice;

/// <summary>
/// Code-behind de la calculatrice : il relaie les événements des boutons au moteur
/// (<see cref="CalculatorEngine"/>) puis rafraîchit l'affichage. Il gère aussi la disposition
/// portrait / paysage et l'adaptation des tailles de police à l'espace disponible.
/// </summary>
public partial class MainPage : ContentPage
{
    private const double MaxResultFontSize = 72;
    private const double MinResultFontSize = 16;

    private readonly CalculatorEngine _engine = new();
    private bool? _isLandscape;
    private bool? _isShortScreen;

    public MainPage()
    {
        InitializeComponent();
        BindableLayout.SetItemsSource(HistoryList, _engine.History);
        ApplyLayout(landscape: false);
        Refresh();
    }

    // ---------- Événements du clavier ----------

    private void OnDigitClicked(object? sender, EventArgs e)
    {
        if (sender is Button { Text: [var digit] })
            Run(() => _engine.InputDigit(digit));
    }

    private void OnDecimalClicked(object? sender, EventArgs e) => Run(_engine.InputDecimalSeparator);

    private void OnOperatorClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string token })
            Run(() => _engine.InputOperator(OperatorExtensions.Parse(token)));
    }

    private void OnEqualsClicked(object? sender, EventArgs e) => Run(_engine.Evaluate);

    private void OnClearClicked(object? sender, EventArgs e) => Run(_engine.Clear);

    private void OnBackspaceClicked(object? sender, EventArgs e) => Run(_engine.Backspace);

    private void OnSignClicked(object? sender, EventArgs e) => Run(_engine.ToggleSign);

    private void OnPercentClicked(object? sender, EventArgs e) => Run(_engine.Percent);

    private void OnPiClicked(object? sender, EventArgs e) => Run(_engine.InputPi);

    private void OnFunctionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string name })
            return;

        var function = name switch
        {
            "sqrt" => UnaryFunction.SquareRoot,
            "square" => UnaryFunction.Square,
            _ => UnaryFunction.Reciprocal
        };
        Run(() => _engine.ApplyFunction(function));
    }

    // ---------- En-tête et historique ----------

    private void OnScienceToggled(object? sender, EventArgs e) => SetScienceVisible(!SciencePanel.IsVisible);

    private void SetScienceVisible(bool visible)
    {
        SciencePanel.IsVisible = visible;
        ScienceToggle.BackgroundColor = visible ? (Color)Resources["CalcFunction"] : (Color)Resources["CalcSurface"];
    }

    private void OnHistoryToggled(object? sender, EventArgs e) => SetHistoryVisible(!HistoryPanel.IsVisible);

    private void OnClearHistoryClicked(object? sender, EventArgs e) => _engine.ClearHistory();

    private void OnHistoryItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: HistoryEntry entry })
        {
            Run(() => _engine.InputValue(entry.Value));
            SetHistoryVisible(false);
        }
    }

    private void SetHistoryVisible(bool visible)
    {
        HistoryPanel.IsVisible = visible;
        HistoryToggle.Text = visible ? "Clavier" : "Historique";

        // Le panneau remplace le pavé : on masque ce qu'il recouvre pour qu'aucune touche ne dépasse de ses coins arrondis.
        Keypad.IsVisible = !visible;
        SciencePanel.Opacity = visible ? 0 : 1;
        SciencePanel.InputTransparent = visible;
    }

    /// <summary>Exécute une action du moteur, puis met à jour l'écran. Aucune exception ne remonte jusqu'à l'utilisateur.</summary>
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Filet de sécurité : le moteur gère déjà les erreurs de calcul, ceci couvre l'imprévu.
            System.Diagnostics.Debug.WriteLine(ex);
            _engine.Clear();
        }

        Refresh();
        TryHapticClick();
    }

    private static void TryHapticClick()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // Retour haptique indisponible (Windows, émulateur) : on ignore.
        }
    }

    // ---------- Affichage ----------

    private void Refresh()
    {
        var expression = _engine.Expression;
        ExpressionLabel.Text = string.IsNullOrEmpty(expression) ? " " : expression;

        ResultLabel.Text = _engine.Display;
        if (_engine.HasError)
        {
            ResultLabel.TextColor = Color.FromArgb("#FF6B6B");
            ResultLabel.LineBreakMode = LineBreakMode.WordWrap;
            ResultLabel.MaxLines = 2;
        }
        else
        {
            ResultLabel.TextColor = (Color)Resources["CalcText"];
            ResultLabel.LineBreakMode = LineBreakMode.HeadTruncation;
            ResultLabel.MaxLines = 1;
        }

        UpdateResultFontSize();
        ScrollExpressionToEnd();
    }

    /// <summary>
    /// Ajuste la taille du résultat pour qu'il tienne toujours dans l'écran d'affichage,
    /// quelle que soit la longueur du nombre, la taille de l'appareil ou l'orientation.
    /// </summary>
    private void UpdateResultFontSize()
    {
        var width = DisplayCard.Width - DisplayCard.Padding.HorizontalThickness;
        if (width <= 0)
            return;
        // Sur un écran très bas, on garantit au moins la taille minimale plutôt que de masquer le résultat.
        var height = Math.Max(DisplayCard.Height - DisplayCard.Padding.VerticalThickness
                              - ExpressionScroll.Height - DisplayStack.Spacing, MinResultFontSize * 1.35);

        var length = Math.Max(ResultLabel.Text?.Length ?? 1, 1);
        double size;
        if (_engine.HasError)
        {
            // Message sur deux lignes au maximum.
            size = Math.Min(height / 2.8, width * 2 / (length * 0.58));
            size = Math.Clamp(size, 14, 30);
        }
        else
        {
            // Largeur moyenne d'un chiffre ≈ 0,6 × taille de police ; hauteur de ligne ≈ 1,35 × taille.
            size = Math.Min(height / 1.35, width / (length * 0.62));
            size = Math.Clamp(size, MinResultFontSize, MaxResultFontSize);
        }

        if (Math.Abs(ResultLabel.FontSize - size) > 0.5)
            ResultLabel.FontSize = size;
    }

    private void ScrollExpressionToEnd()
    {
        // L'opération la plus récente (à droite) reste visible même si l'expression est longue.
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await ExpressionScroll.ScrollToAsync(ExpressionLabel, ScrollToPosition.End, false);
            }
            catch
            {
                // Vue pas encore mesurée : sans conséquence.
            }
        });
    }

    private void OnDisplaySizeChanged(object? sender, EventArgs e) => UpdateResultFontSize();

    private void OnExpressionScrollSizeChanged(object? sender, EventArgs e)
    {
        // Le label occupe au moins toute la largeur visible pour que le texte reste aligné à droite.
        ExpressionLabel.MinimumWidthRequest = ExpressionScroll.Width;
        UpdateResultFontSize();
    }

    /// <summary>La police et l'arrondi des touches suivent la taille réelle des cellules du pavé.</summary>
    private void OnKeypadSizeChanged(object? sender, EventArgs e)
    {
        if (Keypad.Width <= 0 || Keypad.Height <= 0)
            return;

        var cellWidth = (Keypad.Width - Keypad.ColumnSpacing * 3) / 4;
        var cellHeight = (Keypad.Height - Keypad.RowSpacing * 4) / 5;
        var cell = Math.Min(cellWidth, cellHeight);

        var fontSize = Math.Clamp(cell * 0.42, 14, 32);
        var radius = (int)Math.Clamp(cell * 0.3, 8, 28);

        foreach (var key in Keypad.Children.OfType<Button>())
        {
            key.FontSize = fontSize;
            key.CornerRadius = radius;
        }
    }

    // ---------- Orientation ----------

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0)
            return;

        var landscape = Width > Height;
        if (landscape != _isLandscape)
            ApplyLayout(landscape);

        if (!landscape)
        {
            // Portrait : l'écran d'affichage reçoit une part fixe de la hauteur, jamais moins de 110,
            // pour que le résultat reste lisible ; le pavé prend tout le reste.
            RootGrid.RowDefinitions[1].Height = new GridLength(Math.Clamp(Height * 0.28, 110, 280));
        }

        // Écran très bas : les fonctions avancées sont masquées automatiquement (le bouton f(x) permet de les rappeler).
        var shortScreen = Height < (landscape ? 300 : 520);
        if (shortScreen != _isShortScreen)
        {
            _isShortScreen = shortScreen;
            SetScienceVisible(!shortScreen);
        }

        // Écran étroit : le titre laisse la place aux boutons de l'en-tête au lieu de les chevaucher.
        var headerWidth = landscape ? Width * 4 / 9 : Width;
        TitleLabel.IsVisible = headerWidth >= 360;
    }

    /// <summary>
    /// Réorganise la grille principale :
    /// portrait → en-tête, écran, fonctions et pavé empilés ;
    /// paysage  → écran et fonctions à gauche, pavé à droite (la hauteur, rare en paysage, n'est jamais dépassée).
    /// </summary>
    private void ApplyLayout(bool landscape)
    {
        _isLandscape = landscape;
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        if (!landscape)
        {
            RootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            RootGrid.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Star)));
            RootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            RootGrid.RowDefinitions.Add(new RowDefinition(new GridLength(5, GridUnitType.Star)));
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            Place(HeaderBar, row: 0, column: 0);
            Place(DisplayCard, row: 1, column: 0);
            Place(SciencePanel, row: 2, column: 0);
            Place(Keypad, row: 3, column: 0);
            Place(HistoryPanel, row: 2, column: 0, rowSpan: 2);
        }
        else
        {
            RootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            RootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            RootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4, GridUnitType.Star)));
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(5, GridUnitType.Star)));

            Place(HeaderBar, row: 0, column: 0);
            Place(DisplayCard, row: 1, column: 0);
            Place(SciencePanel, row: 2, column: 0);
            Place(Keypad, row: 0, column: 1, rowSpan: 3);
            Place(HistoryPanel, row: 0, column: 1, rowSpan: 3);
        }

        UpdateResultFontSize();
    }

    private static void Place(View view, int row, int column, int rowSpan = 1)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetRowSpan(view, rowSpan);
    }
}
