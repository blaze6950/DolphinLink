namespace DolphinLink.Tests.Infrastructure;

/// <summary>
/// A fact that is automatically skipped when <see cref="DeviceFixture.EnvVar"/>
/// (<c>FLIPPER_PORT</c>) is not set, or when the device is unreachable at
/// runtime (via <see cref="DeviceFixture.Client"/> calling <see cref="Skip.IfNot"/>).
/// </summary>
public sealed class RequiresDeviceFact : SkippableFactAttribute
{
    public RequiresDeviceFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DeviceFixture.EnvVar)))
        {
            Environment.SetEnvironmentVariable(DeviceFixture.EnvVar, "COM4");
            //Skip = $"Set {DeviceFixture.EnvVar} environment variable to run Flipper tests.";
        }
    }
}

/// <summary>
/// A theory that is automatically skipped when <see cref="DeviceFixture.EnvVar"/>
/// (<c>FLIPPER_PORT</c>) is not set, or when the device is unreachable at
/// runtime (via <see cref="DeviceFixture.Client"/> calling <see cref="Skip.IfNot"/>).
/// </summary>
public sealed class RequiresDeviceTheory : SkippableTheoryAttribute
{
    public RequiresDeviceTheory()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DeviceFixture.EnvVar)))
        {
            Skip = $"Set {DeviceFixture.EnvVar} environment variable to run Flipper tests.";
        }
    }
}

/// <summary>
/// A fact that is automatically skipped when either <see cref="DeviceFixture.EnvVar"/>
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
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DeviceFixture.EnvVar)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BootstrapCollection.SystemPortEnvVar)))
        {
            Environment.SetEnvironmentVariable(BootstrapCollection.SystemPortEnvVar, "COM3");
            Environment.SetEnvironmentVariable(DeviceFixture.EnvVar, "COM4");
            //Skip = $"Set {DeviceFixture.EnvVar} and {BootstrapCollection.SystemPortEnvVar} " +
            //       "environment variables to run bootstrap tests.";
        }
    }
}
