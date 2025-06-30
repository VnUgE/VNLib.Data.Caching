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

## Quick Links
📖 **[Documentation & Setup Guides](https://www.vaughnnugent.com/resources/software/articles?tags=docs,_VNLib.Data.Caching)**  
🚀 **[Project Homepage](https://www.vaughnnugent.com/resources/software/modules/VNLib.Data.Caching)**  
💾 **[Package Downloads](https://www.vaughnnugent.com/resources/software/modules/VNLib.Data.Caching?tab=downloads)**  
🐳 **[Docker Images](https://hub.docker.com/r/vnuge/vncache)**  
🐛 **[Issues & Support](https://www.vaughnnugent.com/resources/software/modules/VNLib.Data.Caching-issues)**  

## Dependencies
Built on VNLib.Core framework. Select components include third-party dependencies: RestSharp (REST client), StackExchange.Redis (Redis provider). All dependencies except the .NET runtime and compilers are included in distribution packages.

## License & Copyright
Licensed under GNU Affero General Public License v3+ (AGPL-3.0-or-later). See [LICENSE](LICENSE) file for complete terms. All builds include required license documentation.