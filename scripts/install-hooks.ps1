# Configure Git to use the custom .githooks folder
Write-Host "Configuring git hooks path..."
git config core.hooksPath .githooks

# Mark pre-commit script as executable in git index (useful for cross-platform support)
Write-Host "Making pre-commit script executable..."
git update-index --add --chmod=+x .githooks/pre-commit

Write-Host "Git hooks installed successfully!"
