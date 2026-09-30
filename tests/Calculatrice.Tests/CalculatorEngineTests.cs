using Calculatrice.Core;

namespace Calculatrice.Tests;

public class CalculatorEngineTests
{
    /// <summary>
    /// Simule une suite de touches : chiffres, ",", "+ - * / ^", "=", "C" (AC), "<" (⌫), "±", "%".
    /// Les touches sont séparées par des espaces.
    /// </summary>
    private static CalculatorEngine Press(string keys, CalculatorEngine? engine = null)
    {
        engine ??= new CalculatorEngine();
        foreach (var key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (key)
            {
                case "=": engine.Evaluate(); break;
                case "C": engine.Clear(); break;
                case "<": engine.Backspace(); break;
                case "±": engine.ToggleSign(); break;
                case "%": engine.Percent(); break;
                case ",": engine.InputDecimalSeparator(); break;
                case "+" or "-" or "*" or "/" or "^": engine.InputOperator(OperatorExtensions.Parse(key)); break;
                default:
                    foreach (var c in key)
                    {
                        if (c == ',') engine.InputDecimalSeparator();
                        else engine.InputDigit(c);
                    }
                    break;
            }
        }
        return engine;
    }

    [Theory]
    [InlineData("2 + 3 =", "5")]
    [InlineData("9 - 12 =", "-3")]
    [InlineData("6 * 7 =", "42")]
    [InlineData("7 / 2 =", "3,5")]
    [InlineData("2 ^ 10 =", "1 024")]
    public void BasicOperations(string keys, string expected) =>
        Assert.Equal(expected, Press(keys).Display);

    [Theory]
    [InlineData("2 + 3 * 4 =", "14")]
    [InlineData("10 - 4 / 2 =", "8")]
    [InlineData("2 * 3 ^ 2 =", "18")]
    [InlineData("2 ^ 3 ^ 2 =", "512")]
    [InlineData("8 - 3 - 2 =", "3")]
    [InlineData("100 / 10 / 5 =", "2")]
    public void OperatorPrecedenceIsRespected(string keys, string expected) =>
        Assert.Equal(expected, Press(keys).Display);

    [Fact]
    public void DecimalsAreExact()
    {
        Assert.Equal("0,3", Press("0,1 + 0,2 =").Display);
        Assert.Equal("1", Press("1 / 3 * 3 =").Display);
    }

    [Fact]
    public void DecimalSeparatorIsTypedOnlyOnce()
    {
        var e = Press(", 5 , 2");
        Assert.Equal("0,52", e.Display);
    }

    [Fact]
    public void DivisionByZeroShowsMessageWithoutCrashing()
    {
        var e = Press("5 / 0 =");
        Assert.True(e.HasError);
        Assert.Equal(CalculationException.DivisionByZero, e.Display);
        Assert.Equal("5 ÷ 0 =", e.Expression);

        // Toute nouvelle saisie repart d'un état propre.
        Press("3 + 1 =", e);
        Assert.False(e.HasError);
        Assert.Equal("4", e.Display);
    }

    [Fact]
    public void OperatorsAreIgnoredWhileInError()
    {
        var e = Press("1 / 0 = + =");
        Assert.True(e.HasError);
    }

    [Fact]
    public void ExpressionIsDisplayedAboveResult()
    {
        var e = Press("12 + 3 *");
        Assert.Equal("12 + 3 ×", e.Expression);
        Press("2 =", e);
        Assert.Equal("12 + 3 × 2 =", e.Expression);
        Assert.Equal("18", e.Display);
    }

    [Fact]
    public void SecondOperatorReplacesTheFirst()
    {
        var e = Press("8 + * 2 =");
        Assert.Equal("16", e.Display);
    }

    [Fact]
    public void ResultCanBeChained()
    {
        Assert.Equal("15", Press("2 + 3 = * 3 =").Display);
    }

    [Fact]
    public void DigitAfterResultStartsNewCalculation()
    {
        var e = Press("2 + 3 = 7");
        Assert.Equal("7", e.Display);
        Assert.Equal("", e.Expression);
    }

    [Fact]
    public void ClearResetsEverything()
    {
        var e = Press("12 + 7 C");
        Assert.Equal("0", e.Display);
        Assert.Equal("", e.Expression);
        Assert.Equal("5", Press("5 =", e).Display);
    }

    [Fact]
    public void BackspaceRemovesLastCharacter()
    {
        Assert.Equal("12", Press("123 <").Display);
        Assert.Equal("0", Press("7 <").Display);
        Assert.Equal("1", Press("1,5 < <").Display);
        Assert.Equal("0", Press("5 ± <").Display);
    }

    [Fact]
    public void BackspaceAfterOperatorRemovesTheOperator()
    {
        var e = Press("12 + <");
        Assert.Equal("", e.Expression);
        Assert.Equal("12", e.Display);
        // On reprend l'édition du nombre précédent : 12 puis 3 → 123.
        Assert.Equal("123", Press("3 =", e).Display);
    }

    [Fact]
    public void ToggleSign()
    {
        Assert.Equal("-5", Press("5 ±").Display);
        Assert.Equal("5", Press("5 ± ±").Display);
        Assert.Equal("2", Press("5 + ± 3 =").Display);
        Assert.Equal("-8", Press("3 + 5 = ±").Display);
    }

    [Fact]
    public void Percent()
    {
        Assert.Equal("0,5", Press("50 %").Display);
        Assert.Equal("220", Press("200 + 10 % =").Display);
        Assert.Equal("180", Press("200 - 10 % =").Display);
        Assert.Equal("5", Press("50 * 10 % =").Display);
    }

    [Fact]
    public void UnaryFunctions()
    {
        var e = Press("9");
        e.ApplyFunction(UnaryFunction.SquareRoot);
        Assert.Equal("3", e.Display);

        e = Press("12");
        e.ApplyFunction(UnaryFunction.Square);
        Assert.Equal("144", e.Display);

        e = Press("4");
        e.ApplyFunction(UnaryFunction.Reciprocal);
        Assert.Equal("0,25", e.Display);
    }

    [Fact]
    public void UnaryFunctionErrorsAreHandled()
    {
        var e = Press("4 ±");
        e.ApplyFunction(UnaryFunction.SquareRoot);
        Assert.Equal(CalculationException.InvalidInput, e.Display);

        e = Press("0");
        e.ApplyFunction(UnaryFunction.Reciprocal);
        Assert.Equal(CalculationException.DivisionByZero, e.Display);
    }

    [Fact]
    public void OverflowIsHandled()
    {
        var e = Press("999999999999999 ^ 999 =");
        Assert.Equal(CalculationException.Overflow, e.Display);
    }

    [Fact]
    public void InputIsLimitedTo15Digits()
    {
        var e = Press("12345678901234567890");
        Assert.Equal("123 456 789 012 345", e.Display);
    }

    [Fact]
    public void HistoryKeepsCompletedCalculations()
    {
        var e = Press("2 + 2 = 3 * 3 =");
        Assert.Equal(2, e.History.Count);
        Assert.Equal("3 × 3 =", e.History[0].Expression);
        Assert.Equal(9m, e.History[0].Value);

        e.InputValue(e.History[1].Value);
        Press("+ 1 =", e);
        Assert.Equal("5", e.Display);
    }

    [Fact]
    public void Pi()
    {
        var e = new CalculatorEngine();
        e.InputPi();
        Assert.Equal("3,14159265359", e.Display);
    }
}
