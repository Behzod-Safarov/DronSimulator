# Proves a virtual (or wired) serial pair before the simulator and the trainer are involved:
# opens both ends, sends a CRSF-shaped test frame one way, and reports what came back.
#
#   .\check-pair.ps1 COM4 COM5
#
# COM4 is the end RemoteSim will transmit on, COM5 the end DroneTrainer will listen on.
param(
    [Parameter(Mandatory = $true)][string]$TxPort,
    [Parameter(Mandatory = $true)][string]$RxPort,
    [int]$Baud = 420000
)

$present = [System.IO.Ports.SerialPort]::GetPortNames()
Write-Host ("Ports present: " + ($present -join ", "))
foreach ($p in @($TxPort, $RxPort)) {
    if ($present -notcontains $p) {
        Write-Host "$p is not present - nothing is behind that name yet." -ForegroundColor Red
        exit 1
    }
}

$tx = New-Object System.IO.Ports.SerialPort($TxPort, $Baud, "None", 8, "One")
$rx = New-Object System.IO.Ports.SerialPort($RxPort, $Baud, "None", 8, "One")
$tx.WriteTimeout = 500
$rx.ReadTimeout = 1000

try {
    $rx.Open()
    $tx.Open()
} catch {
    Write-Host ("Could not open both ends: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host "A port that opens alone but not as a pair usually means the two names are not linked."
    exit 2
}

# A plausible CRSF RC frame: sync, length, type, 22 payload bytes, crc. Content does not matter
# here - this is a wire test, the decoders are checked by SerialRcSelfTest.
$frame = New-Object byte[] 26
$frame[0] = 0xC8; $frame[1] = 24; $frame[2] = 0x16
for ($i = 3; $i -lt 26; $i++) { $frame[$i] = [byte]($i * 7 % 256) }

$tx.Write($frame, 0, $frame.Length)
Start-Sleep -Milliseconds 300

$n = $rx.BytesToRead
$got = New-Object byte[] $n
if ($n -gt 0) { $rx.Read($got, 0, $n) | Out-Null }
$tx.Close(); $rx.Close()

Write-Host "sent 26 bytes on $TxPort, read $n bytes on $RxPort"
if ($n -eq 0) {
    Write-Host "The pair is not carrying data - check the wiring (TX to RX, grounds joined) or the com0com pairing." -ForegroundColor Red
    exit 3
}
$same = ($n -eq 26)
for ($i = 0; $i -lt [Math]::Min($n, 26); $i++) { if ($got[$i] -ne $frame[$i]) { $same = $false } }
if ($same) {
    Write-Host "PAIR OK - the bytes arrived unchanged. RemoteSim on $TxPort, trainer on $RxPort." -ForegroundColor Green
    exit 0
}
Write-Host ("Bytes arrived but differ - likely a baud mismatch. Got: " +
            (($got | Select-Object -First 8 | ForEach-Object { $_.ToString('X2') }) -join " ")) -ForegroundColor Yellow
exit 4
