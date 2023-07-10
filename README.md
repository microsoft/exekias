[![CI](https://github.com/vassilyl/exekias/actions/workflows/build-test.yml/badge.svg)](https://github.com/vassilyl/exekias/actions/workflows/build-test.yml)

# EXEKIAS

Storage services for experimental data.

- [Azure Blobs](https://azure.microsoft.com/en-gb/products/storage/blobs) storage for files.
- [Azure CosmosDB](https://azure.microsoft.com/en-gb/products/cosmos-db/) for metadata.
- [Azure Batch](https://azure.microsoft.com/en-gb/products/batch/) for post-processing the data.
- PAYG - serverless computing.

# Getting Started

When you run a physical experiment, a simulation, an ML training you generate precious data.

- Use an [Azure Blobs](https://azure.microsoft.com/en-gb/products/storage/blobs) container to store all the data in a project.
- For each run keep all files together, in a "Run".
- Keep run metadata in a JSON file.

## What's a run

The output of an experiment, a simulation, an ML training is usually a file or a set of files. To reproduce the experiment, the simulation, the training, you usually need same input files, same input parameters.

Run is a set of files that include input files, output files and a JSON file with metadata. The latter captures input parameters, environment and annotations.

Run is a unit of operation for Project Exekias tools and services.

## How to get access to backend services

Create configuration file using interactive `exekias` command.

    exekias config create

The config file contains addresses of the [deployed](#deployment-of-services) blobs container and the CosmosDB database linked to the container.

## How to add a run to a blob storage

Create a uniquely named folder like `20230609-142856-title` with a JSON file `params.json`. The first sequence of digits in the folder name is the run start date,
the second sequence of digits is the run start time and the last portion is the title. These three parts will become searchable run metadata along with
the content of the `params.json` file.

Upload the folder to the to the Azure Blobs container.

- [Azure Storage Explorer](https://azure.microsoft.com/en-gb/products/storage/storage-explorer/).
- [AzCopy, SDKs and other tools](https://learn.microsoft.com/en-GB/azure/storage/blobs/storage-blobs-introduction#move-data-to-blob-storage).
- `exekias` command:

        exekias data upload <folder>

## How to query metadata to find runs

- `exekias` command:

        exekias query "run.params.description='sample'"

    This lists all runs that have `{"description": "sample"}` in their `params.json` file.
    The filter expression is any [Azure Cosmos DB SQL scalar expression](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/query/scalar-expressions). 

## How to get access to data

- Download a folder with [Azure Storage Explorer](https://azure.microsoft.com/en-gb/products/storage/storage-explorer/).
- Download a folder with [AzCopy, SDKs andother tools](https://learn.microsoft.com/en-GB/azure/storage/blobs/storage-blobs-introduction#move-data-to-blob-storage).
- `exekias` command:

        exekias data download <run_id> <folder>

# Deployment of services

You will need to have Contributor role in an Azure subscription.

    exekias backend deploy

The interactive command may create and connect metadata services to an existing blob container or create a new blob container for you.

# Development on Linux
## Running tests
- Install `netcdf`. On Ubuntu, package name is `libnetcdf-dev`. 
- Install azurite, Azure Storage emulator, https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite#install-azurite
- Run azurite, preferably in a separate terminal

    cd ~/azurite
    azurite-blob

- Run `dotnet test` commands.

    dotnet test src/Exekias.Core.Tests/
    dotnet test src/Exekias.Core.Tests/
    dotnet test src/Exekias.AzureStorageEmulator.Tests/

## Build command line utility on Linux

- Install dotnet SDK 6.0 or later, https://dotnet.microsoft.com/en-us/download/dotnet.
- Install bicep CLI, https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/install#linux.
- Set BicepPath environment variable to point to the bicep executable, e.g. `/usr/local/bin/bicep`
- Copy netCDF Windows .dll files and set LIBNETCDFPATH environment variable to the path of `netcdf.dll`.
    - On Windows, download and install latest Windows netCDF NC4 x64 installer package from https://docs.unidata.ucar.edu/netcdf-c/current/winbin.html
    - Copy .dll files from the installed binaries, say, `C:\Program Files\netCDF 4.9.2\bin\*.dll` to the Linux machine, say, `~/netcdf/bin`.
- Run the `dotnet publish` command, e.g.

    BicepPath=/usr/local/bin/bicep LIBNETCDFPATH=~/netcdf/bin/netcdf.dll dotnet publish -c release src/exekias

- Test the command is running

    ./src/exekias/bin/release/net6.0/exekias -h


## Running canary script on Linux

- Install PowerShell, https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell.
- Install Azure PowerShell

    pwsh>Install-Module az

- Login to Azure with your account

    pwsh> Login-AzAccount