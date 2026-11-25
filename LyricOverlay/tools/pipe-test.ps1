param([string]$Request = '{"cmd":"state"}')

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'LyricOverlayControl', [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(2500)
} catch {
    Write-Output "connect-failed: $($_.Exception.Message)"
    exit 1
}

$writer = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
$writer.AutoFlush = $true
$writer.WriteLine($Request)

$reader = New-Object System.IO.StreamReader($pipe)
$response = $reader.ReadLine()
$pipe.Dispose()
Write-Output $response
