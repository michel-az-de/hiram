namespace Hiram.IntegrationTests;

// Tests that boot the API host configure it through process environment variables. Sharing one
// collection runs them sequentially so their environment does not race.
[CollectionDefinition("ApiHost")]
public sealed class ApiHostCollection;
