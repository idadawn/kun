namespace Kun.Testing;

/// <summary>
/// Thrown by <c>Kun.Testing</c> fluent assertion helpers when a
/// condition fails. Test frameworks (xUnit, NUnit, MSTest) surface
/// this as a test failure via the <see cref="Exception.Message"/>
/// string; the exception type itself carries no framework coupling.
/// See ADR-012 §Framework-agnostic failure signalling.
/// </summary>
public sealed class KunAssertionException : Exception
{
    /// <summary>Initializes a new instance with <paramref name="message"/>.</summary>
    public KunAssertionException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with <paramref name="message"/> and <paramref name="inner"/>.</summary>
    public KunAssertionException(string message, Exception inner) : base(message, inner)
    {
    }
}
