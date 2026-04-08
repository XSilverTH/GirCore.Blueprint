#!/bin/bash
set -e

echo "Building and packing XSTH.Blueprint.Helpers locally..."
# Clean old packages
rm -rf pkg || true
mkdir pkg
dotnet pack src/XSTH.Blueprint.Helpers/XSTH.Blueprint.Helpers.csproj -c Release -o ./pkg

echo "Creating a local test feed..."
# Add local source for testing
dotnet new nugetconfig --force
dotnet nuget add source """$(pwd)/pkg""" -n local_blueprint_feed --configfile nuget.config

echo "Installing template locally..."
dotnet new uninstall ./templates/AppTemplate || true
dotnet new install ./templates/AppTemplate

echo "Generating test application..."
rm -rf test_app || true
mkdir test_app
cd test_app
dotnet new gircore-adw -n MyApp
cd MyApp
cp ../../nuget.config .
# Clear NuGet cache for our local package before building
rm -rf ~/.nuget/packages/xsth.blueprint.helpers || true
dotnet build



echo "Success! Testing complete."
