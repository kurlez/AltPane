namespace AltPane.Core.Input;

public enum EscDecision
{
    /// <summary>An IME composition is active. The key must reach the IME.</summary>
    PassToIme,
    /// <summary>The search box has text. Clear it and keep the switcher open.</summary>
    ClearQuery,
    /// <summary>The search box is empty. Close the switcher and restore the previous window.</summary>
    Close
}

/// <summary>
/// Esc while the switcher is open. The first press clears a query. The next
/// press, or the only press when the query is already empty, cancels. A
/// composition in progress belongs to the IME and changes nothing else.
/// </summary>
public static class EscAction
{
    public static EscDecision Decide(bool imeComposing, bool hasQuery)
    {
        if (imeComposing) return EscDecision.PassToIme;
        if (hasQuery) return EscDecision.ClearQuery;
        return EscDecision.Close;
    }
}
