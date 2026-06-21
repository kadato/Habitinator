# Check if SonarQube container is already running
$containerName = "sonarqube"
$isRunning = docker ps --filter "name=$containerName" --filter "status=running" --format "{{.Names}}"

if (-not $isRunning) {
    Write-Host "Starting SonarQube container..."
    docker run -d --name $containerName -p 9000:9000 sonarqube:latest
} else {
    Write-Host "SonarQube container is already running."
}

# Wait for UP status
Write-Host "Waiting for SonarQube to start..."
while ($true) {
    try {
        $status = Invoke-RestMethod -Uri "http://localhost:9000/api/system/status" -Method Get -TimeoutSec 5
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
    $res = Invoke-RestMethod -Uri "http://localhost:9000/api/users/change_password?login=admin&previousPassword=admin&password=SonarAdmin123!" -Method Post -Headers $basicAuthHeader
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
    $res = Invoke-RestMethod -Uri "http://localhost:9000/api/projects/create?project=Habitinator&name=Habitinator" -Method Post -Headers $newAuthHeader
    Write-Host "Project 'Habitinator' created."
} catch {
    Write-Host "Project might already exist: $_"
}

# Revoke existing token if any
try {
    $res = Invoke-RestMethod -Uri "http://localhost:9000/api/user_tokens/revoke?name=HabitinatorScanner" -Method Post -Headers $newAuthHeader
    Write-Host "Revoked old token if it existed."
} catch {}

# Generate new token
$tokenRes = Invoke-RestMethod -Uri "http://localhost:9000/api/user_tokens/generate?name=HabitinatorScanner" -Method Post -Headers $newAuthHeader
$token = $tokenRes.token
Write-Host "Generated Token: $token"

# Save token to file for the scanner task
$token | Out-File -FilePath ".sonar_token" -Encoding utf8
Write-Host "Token saved to .sonar_token"
