namespace FlipperZero.NET.Client.ManualTests;

// xUnit only discovers [CollectionDefinition] attributes in the executing test
// assembly. FlipperFixture lives in the shared Infrastructure library, but the
// collection registration MUST be declared here so xUnit can wire up the fixture
// for all [Collection(FlipperCollection.Name)] test classes in this assembly.
[CollectionDefinition(FlipperCollection.Name)]
public sealed class FlipperCollectionDefinition : ICollectionFixture<FlipperFixture> { }

// HeartbeatManualTests uses [Collection("Flipper heartbeat")] for sequencing
// (no fixture injection). A bare [CollectionDefinition] ensures xUnit
// recognises the collection name and runs its tests serially with
// HeartbeatTests in HardwareTests.
[CollectionDefinition("Flipper heartbeat")]
public sealed class HeartbeatCollectionDefinition { }
