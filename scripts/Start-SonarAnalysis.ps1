# Ensure script runs from its directory root
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $projectRoot

# Initialize dotnet tool manifest if it doesn't exist
if (-not (Test-Path ".config/dotnet-tools.json")) {
    Write-Host "Creating dotnet tool manifest..."
    dotnet new tool-manifest
}

# Install or update dotnet-sonarscanner locally
Write-Host "Setting up dotnet-sonarscanner..."
try {
    dotnet tool install dotnet-sonarscanner --local
} catch {
    # If already installed, ensure it is updated
    dotnet tool update dotnet-sonarscanner --local
}

# Load the Sonar token
$tokenPath = ".sonar_token"
if (-not (Test-Path $tokenPath)) {
    Write-Error "SonarQube token file not found at $tokenPath. Please run scripts/Start-SonarServer.ps1 first."
    Pop-Location
    exit 1
}

$token = (Get-Content -Raw -Path $tokenPath).Trim()

Write-Host "Starting SonarQube Scanner..."
dotnet tool run dotnet-sonarscanner begin `
    /k:"Habitinator" `
    /d:sonar.token="$token" `
    /d:sonar.host.url="http://localhost:9900" `
    /d:sonar.cs.vstest.reportsPaths="TestResults/*.trx" `
    /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml" `
    /d:sonar.cpd.exclusions="src/App.MAUI/**/*.cs"

Write-Host "Compiling Habitinator with analysis hooks..."
dotnet build Habitinator.slnx --configuration Debug --no-incremental

Write-Host "Running tests with coverage collection..."
$testProjects = @(
    "tests/App.Shared.Tests/App.Shared.Tests.csproj",
    "tests/App.Shared.RCL.Tests/App.Shared.RCL.Tests.csproj",
    "tests/App.Web.IntegrationTests/App.Web.IntegrationTests.csproj"
)
foreach ($proj in $testProjects) {
    dotnet test $proj `
        --configuration Debug `
        --no-build `
        --results-directory TestResults `
        --logger "trx" `
        --collect:"XPlat Code Coverage" `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
}

Write-Host "Finalizing SonarQube analysis..."
dotnet tool run dotnet-sonarscanner end /d:sonar.token="$token"

Pop-Location
Write-Host "SonarQube scan completed!"
