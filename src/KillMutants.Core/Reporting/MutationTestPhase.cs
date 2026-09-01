namespace KillMutants.Reporting;

/// <summary>The stages of a run, in the order they happen.</summary>
public enum MutationTestPhase
{
    /// <summary>Looking for test projects and the projects they exercise.</summary>
    Discovering,

    /// <summary>Building the test projects, so their output exists before anything is injected.</summary>
    Building,

    /// <summary>Reading each project's compiler command line and parsing it.</summary>
    Analysing,

    /// <summary>Checking that the unmutated code passes its own tests.</summary>
    VerifyingBaseline,

    /// <summary>Working out which tests reach which mutants.</summary>
    MeasuringCoverage,

    /// <summary>Testing the mutants.</summary>
    TestingMutants,
}
