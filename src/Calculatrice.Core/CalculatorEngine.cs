using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Calculatrice.Core;

/// <summary>
/// Moteur de la calculatrice, indépendant de l'interface.
/// L'expression est mémorisée sous forme de liste (valeurs / opérateurs) puis évaluée
/// en respectant la priorité des opérateurs : 2 + 3 × 4 = 14.
/// Les calculs utilisent le type decimal pour éviter les erreurs d'arrondi binaires (0,1 + 0,2 = 0,3).
/// </summary>
public sealed class CalculatorEngine
{
    public const int MaxDigits = 15;
    public const int MaxHistory = 50;
    private const int Decimals = 12;
    private const decimal ScientificThreshold = 1e16m;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly List<decimal> _values = [];
    private readonly List<Operator> _ops = [];

    // Saisie courante, toujours au format invariant ("-12.5").
    private string _entry = "0";
    // Vrai quand l'opérande courant a été fourni (saisi ou calculé) après le dernier opérateur.
    private bool _hasEntry;
    // Vrai quand l'opérande courant provient de frappes clavier (et non d'un calcul).
    private bool _typed;
    private bool _justEvaluated;
    private string _lastExpression = "";
    private string? _error;

    public ObservableCollection<HistoryEntry> History { get; } = [];

    /// <summary>Valeur principale affichée (nombre ou message d'erreur).</summary>
    public string Display => _error ?? (_typed ? Group(_entry) : FormatNumber(CurrentValue));

    /// <summary>Opération en cours, affichée au-dessus du résultat.</summary>
    public string Expression => _error is not null || _justEvaluated ? _lastExpression : BuildExpression(_values, _ops, trailingOperator: true);

    public bool HasError => _error is not null;

    public decimal CurrentValue => decimal.Parse(_entry, NumberStyles.Float, Inv);

    // ---------- Saisie ----------

    public void InputDigit(char digit)
    {
        if (!char.IsAsciiDigit(digit))
            throw new ArgumentOutOfRangeException(nameof(digit));

        PrepareForNewInput();
        if (!_hasEntry || !_typed)
            _entry = digit.ToString();
        else if (_entry == "0")
            _entry = digit.ToString();
        else if (_entry == "-0")
            _entry = "-" + digit;
        else if (DigitCount(_entry) < MaxDigits)
            _entry += digit;

        _hasEntry = _typed = true;
    }

    public void InputDecimalSeparator()
    {
        PrepareForNewInput();
        if (!_hasEntry || !_typed)
            _entry = "0.";
        else if (!_entry.Contains('.') && DigitCount(_entry) < MaxDigits)
            _entry += ".";

        _hasEntry = _typed = true;
    }

    public void InputOperator(Operator op)
    {
        if (_error is not null)
            return;

        if (_justEvaluated)
        {
            // On enchaîne sur le résultat précédent : "= + 2".
            DetachFromResult();
            Push(CurrentValue, op);
        }
        else if (!_hasEntry && _ops.Count > 0)
        {
            // Deux opérateurs de suite : le second remplace le premier.
            _ops[^1] = op;
        }
        else
        {
            Push(CurrentValue, op);
        }
    }

    public void InputPi() => InputValue((decimal)Math.PI);

    /// <summary>Réutilise une valeur (résultat de l'historique, constante…) comme opérande courant.</summary>
    public void InputValue(decimal value)
    {
        PrepareForNewInput();
        SetComputed(value);
    }

    // ---------- Actions ----------

    public void Evaluate()
    {
        if (_error is not null || _justEvaluated)
            return;

        if (_ops.Count == 0)
        {
            // Rien à calculer : on se contente de valider le nombre saisi.
            _lastExpression = FormatNumber(CurrentValue) + " =";
            SetComputed(CurrentValue);
            _justEvaluated = true;
            return;
        }

        // "5 + =" réutilise le nombre affiché comme second opérande (5 + 5).
        var values = new List<decimal>(_values) { CurrentValue };
        var expression = BuildExpression(values, _ops, trailingOperator: false) + " =";

        decimal result;
        try
        {
            result = Evaluate(values, _ops);
        }
        catch (CalculationException ex)
        {
            SetError(ex.Message, expression);
            return;
        }

        _values.Clear();
        _ops.Clear();
        _lastExpression = expression;
        SetComputed(result);
        _justEvaluated = true;

        History.Insert(0, new HistoryEntry(expression, FormatNumber(CurrentValue), CurrentValue));
        while (History.Count > MaxHistory)
            History.RemoveAt(History.Count - 1);
    }

    /// <summary>Remise à zéro totale (AC).</summary>
    public void Clear()
    {
        _values.Clear();
        _ops.Clear();
        _entry = "0";
        _hasEntry = _typed = _justEvaluated = false;
        _lastExpression = "";
        _error = null;
    }

    public void ClearHistory() => History.Clear();

    /// <summary>Efface le dernier caractère saisi (⌫).</summary>
    public void Backspace()
    {
        if (_error is not null)
        {
            Clear();
            return;
        }

        if (_justEvaluated)
        {
            // Un résultat ne s'édite pas : on efface seulement la ligne d'opération.
            DetachFromResult();
            return;
        }

        if (!_hasEntry)
        {
            // Aucun chiffre après l'opérateur : on retire l'opérateur et on revient au nombre précédent.
            if (_ops.Count > 0)
            {
                _entry = ToEntry(_values[^1]);
                _values.RemoveAt(_values.Count - 1);
                _ops.RemoveAt(_ops.Count - 1);
                _hasEntry = _typed = true;
            }
            return;
        }

        _typed = true;
        _entry = _entry[..^1];
        if (_entry is "" or "-" or "-0")
            _entry = "0";
    }

    /// <summary>Changement de signe (±).</summary>
    public void ToggleSign()
    {
        if (_error is not null)
            return;

        if (_justEvaluated)
            DetachFromResult();

        if (!_hasEntry)
        {
            // Permet de saisir directement un nombre négatif après un opérateur.
            _entry = "-0";
            _hasEntry = _typed = true;
            return;
        }

        if (!_typed && CurrentValue == 0)
            return;

        _entry = _entry.StartsWith('-') ? _entry[1..] : "-" + _entry;
    }

    /// <summary>
    /// Pourcentage : après + ou −, x% vaut x % du membre de gauche (200 + 10% → 200 + 20),
    /// sinon x% vaut x / 100 (50 × 10% → 50 × 0,1).
    /// </summary>
    public void Percent()
    {
        if (_error is not null)
            return;

        if (_justEvaluated)
            DetachFromResult();

        var value = CurrentValue;
        try
        {
            if (_ops.Count > 0 && _ops[^1] is Operator.Add or Operator.Subtract)
            {
                var left = Evaluate(_values, _ops.GetRange(0, _ops.Count - 1));
                SetComputed(Checked(() => left * value / 100m));
            }
            else
            {
                SetComputed(value / 100m);
            }
        }
        catch (CalculationException ex)
        {
            SetError(ex.Message, BuildExpression(_values, _ops, trailingOperator: true));
        }
    }

    /// <summary>Fonctions avancées : √x, x², 1/x.</summary>
    public void ApplyFunction(UnaryFunction function)
    {
        if (_error is not null)
            return;

        if (_justEvaluated)
            DetachFromResult();

        var value = CurrentValue;
        try
        {
            var result = function switch
            {
                UnaryFunction.SquareRoot when value < 0 => throw new CalculationException(CalculationException.InvalidInput),
                UnaryFunction.SquareRoot => (decimal)Math.Sqrt((double)value),
                UnaryFunction.Square => Checked(() => value * value),
                UnaryFunction.Reciprocal when value == 0 => throw new CalculationException(CalculationException.DivisionByZero),
                UnaryFunction.Reciprocal => Checked(() => 1m / value),
                _ => throw new ArgumentOutOfRangeException(nameof(function))
            };
            SetComputed(result);
        }
        catch (CalculationException ex)
        {
            var v = FormatNumber(value);
            var text = function switch
            {
                UnaryFunction.SquareRoot => $"√({v})",
                UnaryFunction.Square => $"({v})²",
                _ => $"1/({v})"
            };
            SetError(ex.Message, (BuildExpression(_values, _ops, trailingOperator: true) + " " + text).Trim());
        }
    }

    // ---------- Évaluation ----------

    /// <summary>
    /// Évalue v0 op0 v1 op1 … vn avec l'algorithme à deux piles (shunting-yard),
    /// ce qui respecte la priorité et l'associativité des opérateurs.
    /// </summary>
    public static decimal Evaluate(IReadOnlyList<decimal> values, IReadOnlyList<Operator> ops)
    {
        if (values.Count != ops.Count + 1)
            throw new ArgumentException("Il doit y avoir exactement une valeur de plus que d'opérateurs.");

        var operands = new Stack<decimal>();
        var operators = new Stack<Operator>();
        operands.Push(values[0]);

        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            while (operators.Count > 0 &&
                   (operators.Peek().Precedence() > op.Precedence() ||
                    (operators.Peek().Precedence() == op.Precedence() && !op.IsRightAssociative())))
            {
                Reduce(operands, operators);
            }
            operators.Push(op);
            operands.Push(values[i + 1]);
        }

        while (operators.Count > 0)
            Reduce(operands, operators);

        return operands.Pop();
    }

    private static void Reduce(Stack<decimal> operands, Stack<Operator> operators)
    {
        var right = operands.Pop();
        var left = operands.Pop();
        operands.Push(Apply(left, operators.Pop(), right));
    }

    public static decimal Apply(decimal left, Operator op, decimal right) => op switch
    {
        Operator.Add => Checked(() => left + right),
        Operator.Subtract => Checked(() => left - right),
        Operator.Multiply => Checked(() => left * right),
        Operator.Divide when right == 0 => throw new CalculationException(CalculationException.DivisionByZero),
        Operator.Divide => Checked(() => left / right),
        Operator.Power => Power(left, right),
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    private static decimal Power(decimal x, decimal y)
    {
        // Exposant entier : calcul exact en decimal (2^10 = 1024 sans erreur d'arrondi).
        if (y == decimal.Truncate(y) && Math.Abs(y) <= 1000)
        {
            var n = (int)Math.Abs(y);
            if (x == 0 && y < 0)
                throw new CalculationException(CalculationException.DivisionByZero);

            var result = Checked(() =>
            {
                decimal r = 1, b = x;
                while (n > 0)
                {
                    if ((n & 1) == 1) r *= b;
                    n >>= 1;
                    if (n > 0) b *= b;
                }
                return r;
            });
            return y < 0 ? Checked(() => 1m / result) : result;
        }

        var d = Math.Pow((double)x, (double)y);
        if (double.IsNaN(d))
            throw new CalculationException(CalculationException.InvalidInput);
        return Checked(() => (decimal)d);
    }

    /// <summary>Transforme les dépassements de capacité du type decimal en erreur affichable.</summary>
    private static decimal Checked(Func<decimal> compute)
    {
        try
        {
            return compute();
        }
        catch (OverflowException)
        {
            throw new CalculationException(CalculationException.Overflow);
        }
    }

    // ---------- Formatage ----------

    /// <summary>Formate un nombre pour l'affichage : virgule décimale, espaces de milliers, notation scientifique si très grand.</summary>
    public static string FormatNumber(decimal value)
    {
        value = Round(value);
        if (Math.Abs(value) >= ScientificThreshold)
            return ((double)value).ToString("0.##########E+0", Inv).Replace('.', ',');
        return Group(ToEntry(value));
    }

    private static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    private static string ToEntry(decimal value) => Round(value).ToString("0.############", Inv);

    /// <summary>"-1234567.5" → "-1 234 567,5" (espaces insécables).</summary>
    private static string Group(string invariant)
    {
        var negative = invariant.StartsWith('-');
        var body = negative ? invariant[1..] : invariant;
        var dot = body.IndexOf('.');
        var integer = dot < 0 ? body : body[..dot];
        var fraction = dot < 0 ? "" : "," + body[(dot + 1)..];

        var sb = new StringBuilder();
        for (var i = 0; i < integer.Length; i++)
        {
            if (i > 0 && (integer.Length - i) % 3 == 0)
                sb.Append(' ');
            sb.Append(integer[i]);
        }
        return (negative ? "-" : "") + sb + fraction;
    }

    private static string BuildExpression(IReadOnlyList<decimal> values, IReadOnlyList<Operator> ops, bool trailingOperator)
    {
        if (ops.Count == 0)
            return "";

        var sb = new StringBuilder(FormatNumber(values[0]));
        for (var i = 0; i < ops.Count; i++)
        {
            sb.Append(' ').Append(ops[i].Symbol());
            if (i + 1 < values.Count && (i + 1 < ops.Count || !trailingOperator))
            {
                var v = values[i + 1];
                sb.Append(' ').Append(v < 0 ? $"({FormatNumber(v)})" : FormatNumber(v));
            }
        }
        return sb.ToString();
    }

    // ---------- État interne ----------

    private void Push(decimal value, Operator op)
    {
        _values.Add(value);
        _ops.Add(op);
        _hasEntry = _typed = false;
    }

    private void SetComputed(decimal value)
    {
        _entry = ToEntry(value);
        _hasEntry = true;
        _typed = false;
    }

    private void SetError(string message, string expression)
    {
        _values.Clear();
        _ops.Clear();
        _entry = "0";
        _hasEntry = _typed = _justEvaluated = false;
        _lastExpression = expression;
        _error = message;
    }

    /// <summary>Avant une nouvelle saisie : efface l'erreur ou démarre un nouveau calcul après "=".</summary>
    private void PrepareForNewInput()
    {
        if (_error is not null)
        {
            Clear();
        }
        else if (_justEvaluated)
        {
            DetachFromResult();
            _hasEntry = false;
        }
    }

    private void DetachFromResult()
    {
        _justEvaluated = false;
        _lastExpression = "";
    }

    private static int DigitCount(string entry) => entry.Count(char.IsAsciiDigit);
}
