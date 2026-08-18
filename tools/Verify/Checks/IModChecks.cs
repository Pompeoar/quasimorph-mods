namespace Verify.Checks;

/// <summary>
/// A behavioural check for one mod. Metadata checks are declarative (patch-targets.json);
/// this is for the arithmetic and logic a JSON file cannot express.
/// </summary>
public interface IModChecks
{
    string ModName { get; }

    void Run(Reporter reporter);
}
