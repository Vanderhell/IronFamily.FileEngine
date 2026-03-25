# Read the original ICFG file
$inputFile = "C:\Users\vande\Desktop\Bjv\bjv\.\_crc_audit\run_test\min.icfg"
$outputFile = "C:\Users\vande\Desktop\Bjv\bjv\.\_crc_audit\run_test\min_corrupt.icfg"

$bytes = [System.IO.File]::ReadAllBytes($inputFile)
Write-Host "Original file size: $($bytes.Length) bytes"
Write-Host "Original first 20 bytes (hex): $(($bytes[0..19] | ForEach-Object { $_.ToString('X2') }) -join ' ')"

# Corrupt byte at position len/2 (XOR with 0xFF to flip all bits)
$corruptPos = [int]($bytes.Length / 2)
Write-Host "Corrupting byte at position $corruptPos"
Write-Host "Original byte at position $corruptPos : 0x$($bytes[$corruptPos].ToString('X2'))"

$bytes[$corruptPos] = $bytes[$corruptPos] -bxor 0xFF
Write-Host "Corrupted byte at position $corruptPos : 0x$($bytes[$corruptPos].ToString('X2'))"

# Write corrupted file
[System.IO.File]::WriteAllBytes($outputFile, $bytes)
Write-Host "Corrupted file written to: $outputFile"
