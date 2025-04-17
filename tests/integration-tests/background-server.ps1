param (
    [Parameter(Mandatory=$true)]
    [ValidateSet("start", "stop")]
    [string]$Action,
    
    [string]$ServerDir,
    [string]$ServerArgs,
    [string]$PidFile
)

$ErrorActionPreference = "Stop"

# Resolve paths
$ServerDir = Resolve-Path $ServerDir

function Start-TestServer {
    Write-Host "Starting test server in $ServerDir with args: $ServerArgs"
    
    # Start the server process
    $process = Start-Process -FilePath "task" `
                            -ArgumentList "$ServerArgs" `
                            -WorkingDirectory $ServerDir `
                            -PassThru `
                            -NoNewWindow
    
    # Store the PID
    $process.Id | Out-File -FilePath $PidFile -Encoding ASCII
    Write-Host "Server started with PID: $($process.Id)"
    Write-Host "PID stored in $PidFile"
}

function Stop-TestServer {
    if (Test-Path $PidFile) {
        $pidd = Get-Content -Path $PidFile -Encoding ASCII
        Write-Host "Stopping server with PID: $pidd"
        
        try {
            Stop-Process -Id $pidd -Force
            Write-Host "Server process stopped"
        } catch {
            Write-Host "Failed to stop process: $_"
        }
        
        Remove-Item $PidFile -Force
    } else {
        Write-Host "No PID file found at $PidFile. Server may not be running."
    }
}

# Execute the requested action
switch ($Action) {
    "start" { Start-TestServer }
    "stop" { Stop-TestServer }
}