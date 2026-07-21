using System;
using System.Collections.Generic;
using System.Text;

namespace Palmap.CollectorApi.src.Configuration
{
    internal record CollectorApiSettings
    {
        public Uri Url { get; init; } = new Uri("http://localhost:5000");

        public string ClientId { get; init; } = string.Empty;

        public string ClientSecret { get; init; } = string.Empty;
    }

    public record CollectorSettings
    {
        public uint PlayerLocationUpdateIntervalMs { get; init; } = 5000;

        public uint GameDataUpdateIntervalMs { get; init; } = 30000;

        public uint ServerSettingsUpdateIntervalMs { get; init; } = 3600000;
    }
}
