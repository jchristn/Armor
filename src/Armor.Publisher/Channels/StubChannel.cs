using System;
using System.Collections.Generic;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Placeholder for channels that are declared in publisher.json but not yet implemented
    /// (docker, helm, homebrew, aptyum). Building one throws with a clear message so the driver
    /// fails fast instead of silently producing nothing.
    /// </summary>
    public sealed class StubChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id { get; }

        /// <inheritdoc/>
        public string DisplayName { get; }

        /// <inheritdoc/>
        public HostOs RequiredHost { get; }

        /// <summary>A one-line hint about what implementing this channel will involve.</summary>
        public string Plan { get; }

        /// <summary>Creates a stub for the given channel id.</summary>
        public StubChannel(string id, string displayName, HostOs requiredHost, string plan)
        {
            Id = id;
            DisplayName = displayName;
            RequiredHost = requiredHost;
            Plan = plan;
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            throw new NotImplementedException(
                $"Channel '{Id}' ({DisplayName}) is scaffolded but not implemented yet.\n" +
                $"  Planned approach: {Plan}");
        }
    }
}
