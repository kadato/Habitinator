# Check if SonarQube container is already running
$containerName = "sonarqube"
$port = 9900
$isRunning = docker ps --filter "name=$containerName" --filter "status=running" --format "{{.Names}}"

if (-not $isRunning) {
    # Check if a stopped container with the same name exists, if so, remove it
    $hasContainer = docker ps -a --filter "name=$containerName" --format "{{.Names}}"
    if ($hasContainer) {
        Write-Host "Removing stopped SonarQube container..."
        docker rm $containerName
    }
    Write-Host "Starting SonarQube container on port $port..."
    docker run -d --name $containerName -p "${port}:9000" sonarqube:latest
} else {
    Write-Host "SonarQube container is already running."
}

# Wait for UP status
Write-Host "Waiting for SonarQube to start..."
while ($true) {
    try {
        $status = Invoke-RestMethod -Uri "http://localhost:$port/api/system/status" -Method Get -TimeoutSec 5
        if ($status.status -eq "UP") {
            Write-Host "SonarQube is UP!"
            break
        }
    } catch {
        # Server not ready yet
    }
    Start-Sleep -Seconds 5
}

# Setup Authorization Headers
$pair = "admin:admin"
$bytes = [System.Text.Encoding]::ASCII.GetBytes($pair)
$base64 = [System.Convert]::ToBase64String($bytes)
$basicAuthHeader = @{ Authorization = "Basic $base64" }

# Change password from admin to SonarAdmin123!
try {
    $res = Invoke-RestMethod -Uri "http://localhost:$port/api/users/change_password?login=admin&previousPassword=admin&password=SonarAdmin123!" -Method Post -Headers $basicAuthHeader
    Write-Host "Password changed successfully to SonarAdmin123!"
} catch {
    Write-Host "Failed to change password with admin:admin. Checking if already updated..."
}

# Update auth header for new credentials
$pairNew = "admin:SonarAdmin123!"
$bytesNew = [System.Text.Encoding]::ASCII.GetBytes($pairNew)
$base64New = [System.Convert]::ToBase64String($bytesNew)
$newAuthHeader = @{ Authorization = "Basic $base64New" }

# Create Project
try {
    $res = Invoke-RestMethod -Uri "http://localhost:$port/api/projects/create?project=Habitinator&name=Habitinator" -Method Post -Headers $newAuthHeader
    Write-Host "Project 'Habitinator' created."
} catch {
    Write-Host "Project might already exist: $_"
}

# Revoke existing token if any
try {
    $res = Invoke-RestMethod -Uri "http://localhost:$port/api/user_tokens/revoke?name=HabitinatorScanner" -Method Post -Headers $newAuthHeader
    Write-Host "Revoked old token if it existed."
} catch {}

# Generate new token
$tokenRes = Invoke-RestMethod -Uri "http://localhost:$port/api/user_tokens/generate?name=HabitinatorScanner" -Method Post -Headers $newAuthHeader
$token = $tokenRes.token
Write-Host "Generated Token: $token"

# Save token to file for the scanner task
$token | Out-File -FilePath ".sonar_token" -Encoding utf8
Write-Host "Token saved to .sonar_token"
