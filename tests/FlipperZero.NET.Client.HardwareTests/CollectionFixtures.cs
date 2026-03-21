namespace FlipperZero.NET.Client.HardwareTests;

// xUnit only discovers [CollectionDefinition] attributes in the executing test
// assembly. FlipperFixture lives in the shared Infrastructure library, but the
// collection registration MUST be declared here so xUnit can wire up the fixture
// for all [Collection(FlipperCollection.Name)] test classes in this assembly.
[CollectionDefinition(FlipperCollection.Name)]
public sealed class FlipperCollectionDefinition : ICollectionFixture<FlipperFixture> { }

// Bootstrap tests run in their own collection (no shared fixture) to ensure they
// get exclusive access to both serial ports. xUnit runs collections sequentially,
// so this collection starts only after the shared Flipper integration collection
// has released the daemon port.
[CollectionDefinition(BootstrapCollection.Name)]
public sealed class BootstrapCollectionDefinition { }
