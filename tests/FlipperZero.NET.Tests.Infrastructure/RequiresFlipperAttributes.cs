namespace FlipperZero.NET.Tests.Infrastructure;

/// <summary>
/// A fact that is automatically skipped when <see cref="FlipperFixture.EnvVar"/>
/// (<c>FLIPPER_PORT</c>) is not set, or when the device is unreachable at
/// runtime (via <see cref="FlipperFixture.Client"/> calling <see cref="Skip.IfNot"/>).
/// </summary>
public sealed class RequiresFlipperFact : SkippableFactAttribute
{
    public RequiresFlipperFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)))
        {
            Environment.SetEnvironmentVariable(FlipperFixture.EnvVar, "COM4");
            //Skip = $"Set {FlipperFixture.EnvVar} environment variable to run Flipper tests.";
        }
    }
}

/// <summary>
/// A theory that is automatically skipped when <see cref="FlipperFixture.EnvVar"/>
/// (<c>FLIPPER_PORT</c>) is not set, or when the device is unreachable at
/// runtime (via <see cref="FlipperFixture.Client"/> calling <see cref="Skip.IfNot"/>).
/// </summary>
public sealed class RequiresFlipperTheory : SkippableTheoryAttribute
{
    public RequiresFlipperTheory()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)))
        {
            Skip = $"Set {FlipperFixture.EnvVar} environment variable to run Flipper tests.";
        }
    }
}
