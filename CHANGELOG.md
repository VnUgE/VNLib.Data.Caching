# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.2] - 2025-09-20

### Changed

- Update StackExchange.Redis to version 2.9.17 - (deps) [6bc20fa](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=6bc20fa1b9c5184152a854d9885f0dc9935e59f2)
- Update Dockerfile for .NET runtime to version 8.0.20 - (deps) [41970ee](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=41970ee769bf1f149b96119d92d1b76c087bc239)
- Update vnlib.core to v0.1.2 - (deps) [a3876df](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=a3876df65d9e75741c25d219463893ff522c54cf)
- Update vnlib.plugins.extensions to v0.1.2 - (deps) [5202482](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=520248262d7ee5374bfddc20e2d60b2b400b49af)

## [0.1.2-rc.8] - 2025-09-10

### Added

- Enable rpmalloc linking for network compression on posix and container platforms - [69d7318](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=69d731868311c24cc98c05efbcf18eb9dec1d7e5)

### Changed

- Update vnlib.core to `v0.1.2-rc.10` - (deps) [8466f63](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=8466f63f9ebd08d3afb483918f095b455d42117a)
- Update vnlib.plugins.extensions to `v0.1.2-rc.8` - (deps) [7010e80](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=7010e80500f71a46e0d602eab78770dfac2faa8d)

## [0.1.2-rc.7] - 2025-08-30

### Changed

- Update `StackExchange.Redis` to v2.9.11 - (deps) [0c101ec](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=0c101ec2bbfa2e919e65402bfb5f9bb1c441c962)
- Update `vnlib.core` to v0.1.2-rc.9 - (deps) [43ad710](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=43ad710441127fe4156093bea714a5a11937564f)
- Update `vnlib.plugins.extensions` to v0.1.2-rc.7 - (deps) [540ff9f](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=540ff9f08d6d67a2d94330fec4e09bd4ee2a2758)
- Centralize MSBuild config via Directory.Build.props; drop MS_ARGS - [26dd66f](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=26dd66f7edaf10734dea7840849d9e2d584fc1ed)

## [0.1.2-rc.6] - 2025-08-14

### Added

- Add support for extended tcp features from vnlib.core v0.1.2-rc.8 - (server) [91d7ffd](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=91d7ffd37d10a858c3ac654181b849e983fb5a20)

### Changed

- Patch for vnlib.core IUmanagedHeap typo fix - [8c913c3](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=8c913c33bc423303aead1d3abbcc47816ca3a936)
- Change native build image to ubuntu:24.04 (aka noble) - (docker) [b5da3ea](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=b5da3eaf2047b9810aaa18a569403565c89d375e)
- Update vnlib.core to v0.1.2-rc.8 - (deps) [d0fbbe6](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=d0fbbe6da55571422ee479ea80a38ce782f047a7)
- Update vnlib.plugins.extensions to v0.1.2-rc.6 - (deps) [d5c698c](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=d5c698ce77d7f64936e0b4fef258209dc14a90e1)

## [0.1.2-rc.5] - 2025-07-29

### Changed

- Update ErrorProne.NET.CoreAnalyzers to 0.8.0-beta.1 - (deps) [88e07be](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=88e07be50a16250278337d59d69443e6d756f1df)
- Update StackExchange.Redis to 2.8.58 - (deps) [d1f5185](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=d1f5185ba3dc8674e24e1eaf2c796e4c8e177f0c)
- Update VNLib.Core to v0.1.2-rc.7 - (deps) [5a2f39c](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=5a2f39cf3c240974beb7ea3f7b456fd5092aa6fb)
- Update VNLib.Plugins.Extensions to v0.1.2-rc.5 - (deps) [13721a6](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=13721a60cba89fdae9db7f55f1821783f1f31c26)

### Fixed

- Fixed the ordering of docker statements - (server) [e1ceda6](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=e1ceda6f1f5c89f259adc251534c0631b44fb490)

## [0.1.2-rc.4] - 2025-07-08

### Changed

- Made some code clarifications and simplifcations for object cache - (libs) [25e159c](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=25e159c81e92c4c106e68f0d50acd26fda6528ba)
- Bump core version to v0.1.2-rc.4 - (deps) [4335059](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=43350590187ed4473cbc08f0c3532d6b15476aaf)
- Bump extensions version to v0.1.2-rc.4 - (deps) [5a21075](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=5a2107534ed981ffdb6c46b334c4eefe6656b2c8)

### Fixed

- Fixed a bug for changing cache entry ID's when the key does not already exist - [10fdc58](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=10fdc581e64e7ab445e29df7b18fa00a1d7176c0)

## [0.1.2-rc.3] - 2025-06-30

### Added

- Add plugin-tests section and update test task - (tests) [3eea45e](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=3eea45e69c11f64b0b11673cb117840246179b1e)

### Changed

- Rename extensions and enhance methods - (lib) [af46381](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=af46381720fcb7cdf0d7405dfe574f01de9a7b5d)
- Replace AddOrUpdateBuffer with VnMemoryStream - (caching) [b07b2bf](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=b07b2bf8bf6ac56027f64c094c4ec1873c24aed4)
- Bump test dependency versions - (deps) [320e66a](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=320e66a678ef4234286449b8a160ab1a897bd30d)

### Fixed

- Update integration tests and improve structure - (lib) [08e9181](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=08e918183a31b24f7ded0bd9cf41551822ccd2c5)
- Flush and force detatch pm2 after tests and wait for a delay before exiting - (taskfile) [9449874](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=9449874f6680410c774b0d48b91c3cc7cbde9347)
- Remove redundant call to StopListening() in CI tests - [054b265](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=054b265eec6790c06c7e2c940998ecbc7632709f)

### Refactor

- **Breaking Change:** Update serialization method signatures - (serializer) [10d9e43](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=10d9e4326d41ae2ade49f196337a757f435160ae)

### Tool

- Add changelog generation with git-cliff - (changelog) [3530adc](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=3530adc7acbde41d3f2574b9e6868ea0fa4f7846)

## [0.1.2-rc.2] - 2025-06-14

### Changed

- Tag develop branch & conditional master-tag - (app) [276457a](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=276457a55d322abc1d4652c1ce868136baf40a2f)
- Update base images - (server) [7d9445e](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=7d9445ea77778460ea76fc4aa614cb18f10a91ae)

## [0.1.1] - 2025-05-15

### Added

- Add future support for memory diagnostics, and some docs - [016a96a](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=016a96a80cce025a86c6cf26707738f6a2eb2658)
- Message checksum support & dynamic serializers - [c74440f](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=c74440ff12daa03cc4b7792d0c3baad46a11a465)

### Changed

- #2 Centralize server state, default discovery endpoints & more - [4d8cfc1](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=4d8cfc10382105b0acbd94df93ad3d05ff91db54)
- Change connection logging verbosity - [33c9fad](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=33c9fad14891914268d6ad6bb63c880b52b08860)
- Update entity result caching - [24929f4](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=24929f4e7acce9847f4cbe813e850ee57d474723)

### Fixed

- #1 shared cluster index on linux & latested core updates - [456ead9](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=456ead9bc8b0f61357bae93152ad0403c4940101)
- Update restsharp configuration - [b21ee53](https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/commit/?id=b21ee53a99b30a21cecd1687ca337d713c919877)

[0.1.2]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2&id2=v0.1.2-rc.8
[0.1.2-rc.8]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.8&id2=v0.1.2-rc.7
[0.1.2-rc.7]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.7&id2=v0.1.2-rc.6
[0.1.2-rc.6]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.6&id2=v0.1.2-rc.5
[0.1.2-rc.5]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.5&id2=v0.1.2-rc.4
[0.1.2-rc.4]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.4&id2=v0.1.2-rc.3
[0.1.2-rc.3]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.3&id2=v0.1.2-rc.2
[0.1.2-rc.2]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.2&id2=v0.1.1

<!-- generated by git-cliff -->
