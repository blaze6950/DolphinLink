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

/// <summary>
/// A fact that is automatically skipped when either <see cref="FlipperFixture.EnvVar"/>
/// (<c>FLIPPER_PORT</c>) or <see cref="BootstrapCollection.SystemPortEnvVar"/>
/// (<c>FLIPPER_SYSTEM_PORT</c>) is not set.
///
/// Used by bootstrap hardware tests that require exclusive access to both CDC
/// interface 0 (native protobuf RPC / system port) and CDC interface 1 (daemon
/// NDJSON port).
/// </summary>
public sealed class RequiresBootstrapFact : SkippableFactAttribute
{
    public RequiresBootstrapFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FlipperFixture.EnvVar)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BootstrapCollection.SystemPortEnvVar)))
        {
            Environment.SetEnvironmentVariable(BootstrapCollection.SystemPortEnvVar, "COM3");
            Environment.SetEnvironmentVariable(FlipperFixture.EnvVar, "COM4");
            //Skip = $"Set {FlipperFixture.EnvVar} and {BootstrapCollection.SystemPortEnvVar} " +
            //       "environment variables to run bootstrap tests.";
        }
    }
}
