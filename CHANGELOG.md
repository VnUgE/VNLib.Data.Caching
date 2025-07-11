# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.2-rc.4] - 2025-07-08

### Changed

- Made some code clarifications and simplifcations for object cache
- Ci (deps): use explicitly versioned dependencies in CI
- Bump core version to v0.1.2-rc.4
- Bump extensions version to v0.1.2-rc.4
- Correct build conditions
- Update readme [no ci]

### Fixed

- Fixed a bug for changing cache entry ID's when the key does not already exist

## [0.1.2-rc.3] - 2025-06-30

### Added

- Add Windows CI tests and restructure tasks
- Add plugin-tests section and update test task
- Add changelog generation with git-cliff

### Changed

- Rename extensions and enhance methods
- Replace AddOrUpdateBuffer with VnMemoryStream
- Update copyright and formatting in CacheNodeReplicationManager
- **Breaking Change:** Update serialization method signatures
- Exlcude alpha tags and commit latest log
- #7 closes #7 update the module readme
- Bump test dependency versions
- Update changelog to latest changes

### Fixed

- Update integration tests and improve structure
- Flush and force detatch pm2 after tests and wait for a delay before exiting

### Removed

- Remove redundant call to StopListening() in CI tests

## [0.1.2-rc.2] - 2025-06-14

### Changed

- Update dependencies and add a changelog file
- [no ci] codeberg mirro and, add auto tag on master build,
- Update 3rd party dependencies
- Tag develop branch & conditional master-tag
- Update base images

### Fixed

- Closes #9 update taskfile.dev to v3.44.0

## [0.1.1] - 2025-05-15

### Added

- Add initial integration testing suite
- Add publish artifacts step to capture them

### Changed

- Initial refactor for issue #6
- Minor readability cleanup
- Obsolete IGlobalCacheProvider and change to ICacheClient
- More refactor. moved config validation to config objects. defer plugin context to run method and add internal handle method when runing in plugin context. Memory cache now behaves similar to cache clients so the API is now unified. fix some config naming.
- Match master versioning. Fix server build dependencies and correct some spelling fixes.
- Set windows build executor explicitly
- [no ci] container dependency updates
- Don't convert lf to crlf for bash scripts
- Cleanup and centralize configuration
- [no ci] some function arg changes
- Rename remote cache config
- Disable private buffer heap by default but allow user config
- Use gitversion as dotnet tool instead of invoking .exe
- Use publish agent when running publish job
- Package updates
- Package updates
- Write to log if admin sets raw password in connection string
- Bump container dependency and image versions
- Run tar in ubuntu image
- Debugging build artifacts
- Correct casing to fix warnings, make the usr app dir if not existing
- [no ci] build image on master and develop
- Clone image build for master branch instead of dependency
- [no ci] fix dependency branch update rule
- Slim container layer sizes and avoid running apt repo update, include deb packages instead.
- [no ci] fix master container build job deps
- Update gitversion config for latest vnbuild
- Switch to continous delivery versioning mode

### Fixed

- Testing based fixes and style cleanup
- Fixes #8 fix git package url and copyright date
- Fix styling
- Fix client debug log should only be in plugin debug mode
- Test docker hub build
- Fix image path
- Fix image path

### Removed

- Remove copied comments

[0.1.2-rc.4]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.4&id2=v0.1.2-rc.3
[0.1.2-rc.3]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.3&id2=v0.1.2-rc.2
[0.1.2-rc.2]: https://git.vaughnnugent.com/cgit/vnuge/vnlib-data-caching.git/diff?id=v0.1.2-rc.2&id2=v0.1.1

<!-- generated by git-cliff -->
