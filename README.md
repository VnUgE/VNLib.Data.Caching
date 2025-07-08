# VNLib.Data.Caching
*High-performance and low complexity data caching solution with distributed clustering capabilities for VNLib applications*

[![Issues](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fwww.vaughnnugent.com%2Fapi%2Fgit%2Fissues%3Fmodule%3DVNLib.Data.Caching&query=%24%5B%27result%27%5D.length&label=all%20issues)](https://www.vaughnnugent.com/resources/software/modules/VNLib.Data.Caching-issues)
[![Docker](https://img.shields.io/badge/Docker-Available-2496ED?logo=docker)](https://hub.docker.com/r/vnuge/vncache)
[![NuGet](https://img.shields.io/badge/NuGet-Available-004880?logo=nuget)](https://www.vaughnnugent.com/resources/software/modules)

## What is VNCache?
A stable and performant arbitrary data caching solution designed for VNLib framework developers. Provides both client libraries for integrating caching into Essentials framework applications and a standalone VNCache server with distributed clustering capabilities.

## Key Capabilities
**Multiple Cache Backends** - VNCache server, Redis, or memory-only configurations  
**VNLib Framework Integration** - Purpose-built for VNLib application developers  
**Two-Tier Architecture** - Optional local memory cache tier over distributed cache  
**Runtime Provider Loading** - Configure cache backends via DLL selection  
**Distributed Clustering** - VNCache server with automatic node discovery  
**Cross-Platform Deployment** - Native binaries and Docker containers  

## Components
**For VNLib Framework Developers**

### Client Libraries
- **Plugin Extensions** - Common caching abstractions and runtime DLL loading  
- **Provider Implementations** - VNCache cluster client, Redis client, memory-only cache  
- **Ready-to-Install Packages** - Tarball distributions for easy VNLib app integration  

### VNCache Server (Clustered Memory Cache)
- **Standalone Cache Server** - Distributed caching solution for VNLib ecosystems
- **Clustering Support** - Automatic node discovery and epidemic networking
- **Docker Deployment** - Container-ready for production environments
- **Cross-Platform** - Compatible with Linux, Windows, and macOS (untested)

## Project Information & Resources

#### Quick Links
The easiest way to access the .NET libraries is by adding the [VNLib NuGet feed](https://www.vaughnnugent.com/resources/software/modules#support-info-title) to your project.

📖 **[Documentation & Setup Guides](https://www.vaughnnugent.com/resources/software/articles?tags=docs,_VNLib.Data.Caching)**  
🚀 **[Project Homepage](https://www.vaughnnugent.com/resources/software/modules/VNLib.Data.Caching)**  
💾 **[Package Downloads](https://www.vaughnnugent.com/resources/software/modules/VNLib.Data.Caching?tab=downloads)**  
🐳 **[Docker Images](https://hub.docker.com/r/vnuge/vncache)**  
🐛 **[Issues & Support](https://www.vaughnnugent.com/resources/software/modules/VNLib.Data.Caching-issues)** 

#### Release Cycle & Distribution
VNLib follows a Continuous Delivery model, which allows for rapid and incremental development, aiming for small weekly releases. Projects are distributed as individual packages, and official distributions include:
- Pre-built binaries for most platforms that support Ahead-of-Time (AOT) compilation.
- Component-level source code and build scripts.
- SHA256 checksums and PGP cryptographic signatures for all packages.

#### API Stability & Versioning
As a fast-moving project, VNLib is effectively in a pre-release state.
- **Public APIs are subject to change**, potentially with little warning in any given release.
- Notable and breaking changes will be recorded in the [changelog](CHANGELOG.md) and commit messages.
- Obsoleted APIs will be marked with the `[Obsolete]` attribute where possible and are expected to be removed in a future release. While advance warning will be given, a strict API stability guarantee cannot be provided at this time.

#### Runtime Stability & Cross-Platform Support
A core pillar of VNLib is runtime stability. Great care is taken to ensure that components are reliable and that functionality, once working, continues to work as expected.

VNLib is designed to be cross-platform. Components should work on any platform that supports a C compiler or a modern .NET runtime. While integration testing is not performed on all operating systems, the architecture is platform-agnostic by design.

#### Contributing
Note that GitHub and Codeberg integrations are disabled. VNLib takes its independence seriously and does not use third-party platforms for development, issue tracking, or pull requests. Information about contributing to the project can be found on the official website. While the reach of free platforms is respected, project independence is a core value.

The project is, however, very interested in seeing what is built with VNLib! If you have created a plugin or a project you would like to share, please get in touch via the contact information on the official website.


## License & Copyright
Licensed under GNU Affero General Public License v3+ (AGPL-3.0-or-later). See [LICENSE](LICENSE) file for complete terms. All builds include required license documentation.