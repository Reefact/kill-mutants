namespace KillMutants.Mutations;

/// <summary>What a mutant's fate says about the test suite.</summary>
/// <remarks>
/// <para>
/// <see cref="MutantStatus"/> records <em>what happened</em>; this records <em>what it means</em>.
/// The distinction exists because the score depends only on the second question, and answering it in
/// one place stops every reporter and threshold deciding for itself what a timeout or an uncovered
/// mutant is worth.
/// </para>
/// <para>
/// The line that matters is between <see cref="Undetected"/> and <see cref="Untestable"/>. A mutant
/// nothing covers <em>is</em> undetected: the suite would not have noticed the change, and knowing
/// that in advance is an optimisation, not an excuse. Excluding it would mean that adding untested
/// code raises the score, which is exactly backwards. Only a mutant KillMutants never managed to put
/// in front of the tests is untestable, and that is a limitation of this tool rather than a fact
/// about the suite.
/// </para>
/// </remarks>
public enum MutantOutcome
{
    /// <summary>The suite noticed the change.</summary>
    Detected,

    /// <summary>The suite did not notice the change, whether it ran or could not have.</summary>
    Undetected,

    /// <summary>The suite was never asked, because KillMutants could not produce a testable mutant.</summary>
    Untestable,
}
