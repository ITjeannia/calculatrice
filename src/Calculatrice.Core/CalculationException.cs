namespace Calculatrice.Core;


/// Erreur de calcul prévisible (division par zéro, dépassement, domaine invalide).
/// Elle est interceptée par le moteur et transformée en message affiché : l'application ne plante jamais.

public sealed class CalculationException(string message) : Exception(message)
{
    public const string DivisionByZero = "Division par zéro impossible";
    public const string Overflow = "Dépassement de capacité";
    public const string InvalidInput = "Entrée invalide";
}
