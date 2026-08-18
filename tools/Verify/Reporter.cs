namespace Verify;

/// <summary>
/// Collects check results. Every check increments the count so a silently-empty run
/// (a JSON file that stopped matching anything, say) is visible rather than a green tick.
/// </summary>
public sealed class Reporter
{
    private readonly List<string> _failures = new();

    public int Checks { get; private set; }

    public int FailureCount => _failures.Count;

    public IReadOnlyList<string> Failures => _failures;

    public void Fail(string message)
    {
        Checks++;
        _failures.Add(message);
    }

    public void Assert(bool condition, string failureMessage)
    {
        Checks++;
        if (!condition)
        {
            _failures.Add(failureMessage);
        }
    }

    public void AssertEqual(string label, string actual, string expected)
    {
        Checks++;
        if (actual != expected)
        {
            _failures.Add($"{label}: got [{actual}], expected [{expected}]");
        }
    }
}
