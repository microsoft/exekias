# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.2] - 2023-06-27

### Changed
- Python `exekias.Config.init_local_run()` creates indented `params.json`. 

### Fixed
- 27630 "No subscription available" on an Azure VM. Disabled use of Azure managed identities.
- 27685 "Same set of functions on both deployed function apps. Waits completion of on ZIP-deployment before starting another one.
- 27724 "Batch task failed" Quote arguments for the batch task command line to account for spaces in file paths.

## [2.0.1] - 2023-06-07

### Changed
- Use new SDSLite 3.0.1 capabilities to locate native assemblies.
  No netCDF assemblies in source tree anymore. Instead, use local netCDF installation locally,
  64-bit netCDF libraries side by side with application binaries on services,
  and netCDF package downloaded from UNIDATA in CI build.
- Deprecated x86 platform.
- Reengineered build system to create exekias package with MSBuild.

## [2.0.0] - 2023-05-15

### Changed
- `exekias congig create` now saved full subscription resource identifier.

### Added
- Global version number in `Version.proj`.
- Change log file `CHANGELOG.md`.

### Fixed
- `exekias backend deploy` now properly instantiate subscription resource from an existing config file.
- ZIP deployment of Function Apps now uses Bearer authorization which works when Basic authorization is disabled for the subscription.