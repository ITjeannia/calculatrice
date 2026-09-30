namespace Calculatrice.Core;

//Opérateurs binaires pris en charge par la calculatrice
public enum Operator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Power
}

//Fonctions unaires appliquées au nombre affiché
public enum UnaryFunction
{
    SquareRoot,
    Square,
    Reciprocal
}

public static class OperatorExtensions
{
    //Symbole affiché à l'écran pour un opérateur.
    public static string Symbol(this Operator op) => op switch
    {
        Operator.Add => "+",
        Operator.Subtract => "−",
        Operator.Multiply => "×",
        Operator.Divide => "÷",
        Operator.Power => "^",
        _ => "?"
    };

    //Priorité de l'opérateur : ^ avant × ÷, eux-mêmes avant +
    public static int Precedence(this Operator op) => op switch
    {
        Operator.Power => 3,
        Operator.Multiply or Operator.Divide => 2,
        _ => 1
    };

    //La puissance est associative à droite (2^3^2 = 2^9).
    public static bool IsRightAssociative(this Operator op) => op == Operator.Power;

    //Convertit le paramètre d'un bouton XAML (+, -, *, /, ^) en opérateur
    public static Operator Parse(string token) => token switch
    {
        "+" => Operator.Add,
        "-" or "−" => Operator.Subtract,
        "*" or "×" => Operator.Multiply,
        "/" or "÷" => Operator.Divide,
        "^" => Operator.Power,
        _ => throw new ArgumentException($"Opérateur inconnu : {token}", nameof(token))
    };
}
