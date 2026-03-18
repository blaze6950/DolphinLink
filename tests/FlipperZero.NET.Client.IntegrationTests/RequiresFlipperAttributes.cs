namespace FlipperZero.NET.Client.IntegrationTests;

/// <summary>
/// A <c>[Fact]</c> that is automatically skipped when
/// <see cref="FlipperFixture.EnvVar"/> (<c>FLIPPER_PORT</c>) is not set.
/// </summary>
public sealed class RequiresFlipperFact : FactAttribute
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
/// A <c>[Theory]</c> that is automatically skipped when
/// <see cref="FlipperFixture.EnvVar"/> (<c>FLIPPER_PORT</c>) is not set.
/// </summary>
public sealed class RequiresFlipperTheory : TheoryAttribute
{
    public RequiresFlipperTheory()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)))
        {
            Skip = $"Set {FlipperFixture.EnvVar} environment variable to run Flipper integration tests.";
        }
    }
}
