# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]


## [2.3.1] - 2024-07-09

### Fixed
- in `exekias backend deploy` command, user authorization is now properly set up for the backend.
- `exekias` diagnostics of non-existant config file.
- `NullReferenceException` in `FullScan` activity.

### Added
- `--metadatafilepattern` option added to the `exekias backend deploy` command for fine grained control of metadata file pattern.

### Removed
- obsolete `--interactiveauth` option.


## [2.3.0] - 2024-06-26

### Added
- `exekias backend allow user/group` command to add role assignments necessary to work with the backend.
- `--credential` option added to the `exekias` utility for fine grained control of user authentication.

### Security
- Upgrade package references due to vulnerable version of `Azure.Identity`.
- `exekias backend deploy` command creates Cosmos DB account configured for minimum TSL version 1.2.
- Azure CosmosDB and Storage accounts requests can no longer be authorized with keys, only Entra ID access tokens.


## [2.2.1] - 2024-01-18

### Changed
- `exekias backend deploy` command creates an Azure app service configured https only.

## [2.1.0] - 2023-10-07

### Changed
- `exekias query ...` command is now `exekias runs query ...`.
  It doesn't show hidden runs by default.
  `exekias query ... --hidden` command shows only hidden runs instead.

### Added
- `exekias runs show <run>` command to print run metadata.
- `exekias runs hide <run> [--unhide]` command to hide/unhide runs.


## [2.0.5] - 2023-09-26
## [2.0.4] - 2023-09-26

## Changed
- Backend function apps have more secure properties on deployment: minimum TLS version 1.2, disabled remote debugging, disable FTP clear text access.
- Backend deployment now have unique names to improve diagnostics whith multiple deployments to the same resource group.
- A more robust storage account creation and more logging during backend deployment.

## [2.0.3] - 2023-08-23

### Changed
- Backend doesn't use Discover Run operation any more. The operation causes performance and cost issues.
  The downside is that data updates to old existing runs are picked up by periodic housekeeping only.
- Backend auxiliary storage account is configured with public blob access disabled
- Backend canary test does several attempts to generate update events to account for transient provcesses.
- `exekias` upload/download properly propagate last write time and skip data transfer when
  file and blob are identical.

## [2.0.2] - 2023-06-27

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
