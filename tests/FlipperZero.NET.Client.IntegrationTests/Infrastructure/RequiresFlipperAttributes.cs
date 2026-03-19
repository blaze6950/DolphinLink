namespace FlipperZero.NET.Client.IntegrationTests.Infrastructure;

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
            // todo - for development/debugging, set a default port here so the tests run without needing to set the env var every time. Remove this before merging.
            Environment.SetEnvironmentVariable(FlipperFixture.EnvVar, "COM4"); // Clear any whitespace value
            //Skip = $"Set {FlipperFixture.EnvVar} environment variable to run Flipper integration tests.";
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
            Skip = $"Set {FlipperFixture.EnvVar} environment variable to run Flipper integration tests.";
        }
    }
}
