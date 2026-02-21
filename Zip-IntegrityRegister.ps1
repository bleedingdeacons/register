# Zip-DotNetProjects.ps1
# PowerShell script to zip multiple .NET projects while excluding bin and obj folders

# Configuration - Update these paths to your actual project folders
$projectPaths = @(
    "C:\Users\David\Local Sites\unity-dev\app\public\wp-content\plugins\integrity\sharp\TheBleedingDeacons.Unity.Models",
    "C:\Users\David\Local Sites\unity-dev\app\public\wp-content\plugins\integrity\sharp\TheBleedingDeacons.Unity.Client",
    "C:\Data\dev\Register\TheBleedingDeacons.Intergroup.Register"
)

# Output directory for zip files (defaults to current directory)
$outputDir = ".\ZippedProjects"

# Folders to exclude from zip
$excludeFolders = @("bin", "obj", ".vs", ".git")

# Create output directory if it doesn't exist
if (-not (Test-Path $outputDir)) {
    New-Item -Path $outputDir -ItemType Directory | Out-Null
    Write-Host "Created output directory: $outputDir" -ForegroundColor Green
}

# Function to zip a project with exclusions
function Zip-ProjectWithExclusions {
    param (
        [string]$SourcePath,
        [string]$DestinationPath,
        [string[]]$ExcludeFolders
    )
    
    Write-Host "`nProcessing: $SourcePath" -ForegroundColor Cyan
    
    # Check if source path exists
    if (-not (Test-Path $SourcePath)) {
        Write-Host "ERROR: Source path does not exist: $SourcePath" -ForegroundColor Red
        return
    }
    
    # Create a temporary directory for filtered content
    $tempDir = Join-Path $env:TEMP ("TempZip_" + [System.IO.Path]::GetRandomFileName())
    New-Item -Path $tempDir -ItemType Directory | Out-Null
    
    try {
        # Copy files and folders, excluding specified directories
        $projectName = Split-Path $SourcePath -Leaf
        $tempProjectDir = Join-Path $tempDir $projectName
        New-Item -Path $tempProjectDir -ItemType Directory | Out-Null
        
        Write-Host "Copying files (excluding: $($ExcludeFolders -join ', '))..." -ForegroundColor Yellow
        
        # Get all items recursively
        Get-ChildItem -Path $SourcePath -Recurse -Force | ForEach-Object {
            $relativePath = $_.FullName.Substring($SourcePath.Length + 1)
            $pathParts = $relativePath -split '\\'
            
            # Check if any part of the path contains excluded folders
            $shouldExclude = $false
            foreach ($excludeFolder in $ExcludeFolders) {
                if ($pathParts -contains $excludeFolder) {
                    $shouldExclude = $true
                    break
                }
            }
            
            if (-not $shouldExclude) {
                $destination = Join-Path $tempProjectDir $relativePath
                
                if ($_.PSIsContainer) {
                    # Create directory
                    if (-not (Test-Path $destination)) {
                        New-Item -Path $destination -ItemType Directory -Force | Out-Null
                    }
                } else {
                    # Copy file
                    $destDir = Split-Path $destination -Parent
                    if (-not (Test-Path $destDir)) {
                        New-Item -Path $destDir -ItemType Directory -Force | Out-Null
                    }
                    Copy-Item -Path $_.FullName -Destination $destination -Force
                }
            }
        }
        
        Write-Host "Creating zip file: $DestinationPath" -ForegroundColor Yellow
        
        # Remove existing zip file if it exists
        if (Test-Path $DestinationPath) {
            Remove-Item $DestinationPath -Force
        }
        
        # Create zip file
        Compress-Archive -Path "$tempProjectDir\*" -DestinationPath $DestinationPath -CompressionLevel Optimal
        
        $zipSize = (Get-Item $DestinationPath).Length / 1MB
        Write-Host "SUCCESS: Created $DestinationPath ($([math]::Round($zipSize, 2)) MB)" -ForegroundColor Green
        
    } catch {
        Write-Host "ERROR: Failed to create zip for $SourcePath - $($_.Exception.Message)" -ForegroundColor Red
    } finally {
        # Clean up temporary directory
        if (Test-Path $tempDir) {
            Remove-Item $tempDir -Recurse -Force
        }
    }
}

# Main execution
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ".NET Project Zipper" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta

$outputDirFull = (Resolve-Path $outputDir -ErrorAction SilentlyContinue).Path
if (-not $outputDirFull) {
    $outputDirFull = (Get-Item $outputDir).FullName
}

Write-Host "`nOutput directory: $outputDirFull"
Write-Host "Excluding folders: $($excludeFolders -join ', ')"
Write-Host "Projects to zip: $($projectPaths.Count)"

# Process each project
foreach ($projectPath in $projectPaths) {
    $projectName = Split-Path $projectPath -Leaf
    $zipFileName = "$projectName.zip"
    $zipFilePath = Join-Path $outputDir $zipFileName
    
    Zip-ProjectWithExclusions -SourcePath $projectPath -DestinationPath $zipFilePath -ExcludeFolders $excludeFolders
}

Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "Zipping complete!" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
