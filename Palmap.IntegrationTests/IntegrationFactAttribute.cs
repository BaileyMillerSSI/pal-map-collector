using Xunit;

namespace Palmap.IntegrationTests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PALMAP_RUN_INTEGRATION_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set PALMAP_RUN_INTEGRATION_TESTS=true and start the Compose sample to run integration tests.";
        }
    }
}
