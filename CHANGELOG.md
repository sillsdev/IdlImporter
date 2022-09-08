# Change Log

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

<!-- Available types of changes:
### Added
### Changed
### Fixed
### Deprecated
### Removed
### Security
-->

## [Unreleased]

## [3.0.1] - 2022-09-08

### Added

- publish IdlImpTool as global tool

### Changed

- re-add net461 target
- add net5.0 target
- remove Windows.Forms dependency

## [3.0.0] - 2022-07-08

### Changed

- switched to .NET Standard 2.0

### Removed

- removed TypeLib attribute support in code generation
- removed deprecated binary serialization

## [2.0.0] - 2021-04-27

### Added

- `ILog` interface

### Changed

- Create nuget package
- New constructor overload for IDLImporter that takes a logger (`ILog`)

## [Older]

[Unreleased]: https://github.com/sillsdev/IdlImporter/compare/v3.0.1...master

[3.0.1]: https://github.com/sillsdev/IdlImporter/compare/v3.0.0...v3.0.1
[3.0.0]: https://github.com/sillsdev/IdlImporter/compare/v2.0.0...v3.0.0
[2.0.0]: https://github.com/sillsdev/IdlImporter/compare/fcf0091...v2.0.0
