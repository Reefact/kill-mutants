namespace KillMutants.Reporting;

/// <summary>Where in a run a part of its scope was set aside.</summary>
public enum SetAsideStage
{
    /// <summary>A project MSBuild could not read, so nothing is known of what it holds.</summary>
    Discovery,

    /// <summary>A test project that would not build, with every target it was to measure.</summary>
    Build,

    /// <summary>A project under test whose compilation could not be reconstructed.</summary>
    Analysis,

    /// <summary>A target whose suite did not pass against unmutated code.</summary>
    Baseline,

    /// <summary>A target whose coverage could not be measured.</summary>
    Coverage,
}

/// <summary>A part of the run's scope the run could not measure, and why (DEC0022).</summary>
/// <param name="Stage">Where in the run it happened.</param>
/// <param name="Name">The project or target, as the reader knows it.</param>
/// <param name="Reason">What stopped it, in the words the run would otherwise have stopped with.</param>
/// <remarks>
/// A mutant that cannot be judged is not one of these: it is an outcome inside the measured scope,
/// counted as untestable, and DEC0022 keeps it there so one ordinary mutant cannot turn a complete
/// run red. What lands here is what leaves part of the intended scope unmeasured.
/// </remarks>
public sealed record SetAside(SetAsideStage Stage, string Name, string Reason)
{
    /// <summary>
    /// True when this leaves the population itself unknown: a project discovery could not read may
    /// have been the only suite reaching some code, which would then read as uncovered or not be
    /// selected at all, rather than as not measured.
    /// </summary>
    public bool HidesThePopulation => Stage == SetAsideStage.Discovery;
}
