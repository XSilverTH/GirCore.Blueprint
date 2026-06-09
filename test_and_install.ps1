$ErrorActionPreference = "Stop"

Write-Host "Building and packing XSTH.Blueprint.Helpers locally..."
# Clean old packages
if (Test-Path "pkg") {
    Remove-Item -Path "pkg" -Recurse -Force
}
New-Item -ItemType Directory -Path "pkg" -Force | Out-Null

dotnet pack src/XSTH.Blueprint.Helpers/XSTH.Blueprint.Helpers.csproj -c Release -o ./pkg

Write-Host "Creating a local test feed..."
# Add local source for testing
dotnet new nugetconfig --force
dotnet nuget add source ./pkg -n local_blueprint_feed --configfile nuget.config

Write-Host "Installing template locally..."
# Try to uninstall, ignore errors if it doesn't exist
dotnet new uninstall ./templates/AppTemplate | Out-Null
dotnet new install ./templates/AppTemplate

Write-Host "Generating test application..."
if (Test-Path "test_app") {
    Remove-Item -Path "test_app" -Recurse -Force
}
New-Item -ItemType Directory -Path "test_app" -Force | Out-Null
Push-Location test_app

dotnet new gircore-adw -n MyApp
Set-Location MyApp

# Clear NuGet cache for our local package before building
$cachePath = Join-Path $HOME ".nuget/packages/xsth.blueprint.helpers"
if (Test-Path $cachePath) {
    Remove-Item -Path $cachePath -Recurse -Force
}

dotnet build

Pop-Location # Back to test_app
Pop-Location # Back to root

Write-Host "Success! Testing complete."
