#!/bin/sh
cd /home/codespace
echo Downloading 7zip
wget -q $URL_7Z -O 7zip.tar.xz
mkdir 7zip
tar xf 7zip.tar.xz -C 7zip
rm 7zip.tar.xz
echo Downloading NetCDF
wget -q -O netcdf.exe $URL_NetCDF
7zip/7zz x netcdf.exe bin/*.dll -onetcdf -y -bso0
rm netcdf.exe
echo Downloading required PowerShell Az modules
pwsh -c "Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted; Install-Module -Name Az.Resources,Az.Storage,Az.CosmosDb,Az.Batch"
echo Install Azurite
npm install -g azurite
mkdir ~/azurite
echo install netcdf
sudo sudo apt-get update && sudo apt-get install -y libnetcdf-dev