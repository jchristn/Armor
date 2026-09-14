using System.Collections.Generic;

namespace Armor.Publisher.Channels
{
    /// <summary>Maps channel ids from publisher.json to their <see cref="IChannel"/> implementation.</summary>
    public static class ChannelRegistry
    {
        /// <summary>Returns the channel implementation for an id, or null if the id is unknown.</summary>
        public static IChannel? Resolve(string id)
        {
            switch (id.ToLowerInvariant())
            {
                // Downloadable installers.
                case "inno": return new InnoChannel();
                case "dmg": return new DmgChannel();
                case "debrpm": return new DebRpmChannel();
                case "appimage": return new AppImageChannel();

                // Package managers.
                case "winget": return new WingetChannel();
                case "choco": return new ChocoChannel();
                case "scoop": return new ScoopChannel();
                case "brewcask": return new BrewCaskChannel();
                case "aptyum": return new AptYumChannel();
                case "snap": return new SnapChannel();
                case "flatpak": return new FlatpakChannel();
                case "dotnettool": return new DotnetToolChannel();

                // Optional/extra containerization (disabled by default in the manifest).
                case "docker":
                    return new StubChannel("docker", "Docker image", HostOs.Any,
                        "docker buildx build --platform linux/amd64,linux/arm64 from a generated Dockerfile, push to a registry.");
                case "helm":
                    return new StubChannel("helm", "Helm chart", HostOs.Any,
                        "helm package a chart templated with the image tag, publish to an OCI/HTTP chart repo.");

                default: return null;
            }
        }

        /// <summary>All channel ids the registry knows about.</summary>
        public static IReadOnlyList<string> KnownIds => new[]
        {
            "inno", "dmg", "debrpm", "appimage",
            "winget", "choco", "scoop", "brewcask", "aptyum", "snap", "flatpak", "dotnettool",
            "docker", "helm"
        };
    }
}
