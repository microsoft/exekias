Publish instructions
--------------------

Prerequisite: NetCDF NC4 64-bit, https://downloads.unidata.ucar.edu/netcdf/

	dotnet publish -c Release -r win-x64

The project includes two non-standard elements:

* `main.bicep` is being transformed to `main.json` template at build time using `Azure.Bicep` NuGet packages
  as explaind in https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/msbuild-bicep-file.
* When publishing the project, additional packages are created for Azure functions and Azure Batch deployment.
