namespace FlipperZero.NET.Client.HardwareTests;

// xUnit only discovers [CollectionDefinition] attributes in the executing test
// assembly. FlipperFixture lives in the shared Infrastructure library, but the
// collection registration MUST be declared here so xUnit can wire up the fixture
// for all [Collection(FlipperCollection.Name)] test classes in this assembly.
[CollectionDefinition(FlipperCollection.Name)]
public sealed class FlipperCollectionDefinition : ICollectionFixture<FlipperFixture> { }
