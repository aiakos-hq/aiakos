# Akka.TestKit and xUnit v3 compatibility

The tests reference Akka.TestKit 1.5.71, whose core package does not depend on an xUnit major version. The selected release's `ITestKitAssertions` interface has nine methods: `Fail`, `AssertTrue`, `AssertFalse`, both `AssertEqual` overloads, `AssertThrows` and its generic overload, and `AssertThrowsAsync` and its generic overload. `XunitV3TestKitAssertions` implements each operation with xUnit v3 assertions.

Inspected upstream package metadata for Akka.TestKit.Xunit2 1.5.71: its nuspec depends on `xunit` 2.8.1. That adapter is incompatible with this xUnit v3 test project, so the tests use the core package and local adapter.

Smoke command:

```sh
dotnet test --project tests/Aiakos.Orchestrator.Tests -c Release --filter-class '*TestKitCompatibilityTests*'
```

Result: passed, 2 tests, 0 failed, 0 skipped. `TestProbe` received exactly `ping` from a real echo actor; xUnit v3 assertion failures were observed; the actor system termination task completed.
