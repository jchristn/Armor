using System.Collections.Generic;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// One packaging channel (inno, dmg, debrpm, ...). A channel takes published payloads and
    /// produces one or more installer/package files in the output directory.
    /// </summary>
    public interface IChannel
    {
        /// <summary>Channel id as it appears in publisher.json (e.g. "inno").</summary>
        string Id { get; }

        /// <summary>Human-readable name for logs.</summary>
        string DisplayName { get; }

        /// <summary>
        /// The host OS this channel must run on. The driver refuses to run a channel on the wrong
        /// OS with an actionable message rather than failing deep inside a missing tool.
        /// </summary>
        HostOs RequiredHost { get; }

        /// <summary>Builds the packages. Returns the absolute paths of the produced files.</summary>
        IReadOnlyList<string> Build(ChannelContext context);
    }

    /// <summary>Host operating systems a channel can require.</summary>
    public enum HostOs
    {
        /// <summary>Runs on any OS (e.g. homebrew formula generation, helm package).</summary>
        Any,
        /// <summary>Requires Windows (Inno Setup).</summary>
        Windows,
        /// <summary>Requires macOS (dmg creation + notarization).</summary>
        MacOs,
        /// <summary>Requires Linux (dpkg-deb / rpmbuild / repo metadata).</summary>
        Linux
    }
}
