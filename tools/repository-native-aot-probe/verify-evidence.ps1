param(
    [Parameter(Mandatory = $true)][string]$Evidence,
    [Parameter(Mandatory = $true)][ValidateSet('X64', 'Arm64')][string]$Architecture
)
$ErrorActionPreference = 'Stop'
# Independent expected byte witnesses; never infer expectations from probe output.
function Get-Digest([byte[]]$Bytes) {
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Bytes))
}
$payload = [byte[]]::new(65543)
for ($i = 0; $i -lt $payload.Length; $i++) {
    $payload[$i] = [byte](($i * 31 + 7) -band 255)
}
$first = Get-Digest $payload
$second = Get-Digest ([byte[]]@(1, 2, 3))
$winner = Get-Digest ([byte[]]@(7, 8))
$expected = @(
    "file|prepared|$first"
    "pack-sha256|prepared|$first|$second"
    "pack-blake3|prepared|$first|$second"
    "collision|reconciliation|$winner"
    'wrong-descriptor|reconciliation|retained'
    'same-byte-replacement|reconciliation|distinct-object'
    'cancel-before-parent-flush|reconciliation|retained'
    'native-open-missing|rejected|no-ownership'
)
foreach ($mode in @('jit', 'aot')) {
    $actual = @(Get-Content -LiteralPath (Join-Path $Evidence "$mode.txt"))
    if ($actual.Count -ne $expected.Count) { throw "$mode case count differs" }
    for ($i = 0; $i -lt $expected.Count; $i++) {
        if ($actual[$i] -cne $expected[$i]) { throw "$mode case $i differs" }
    }
    $log = @(Get-Content -LiteralPath (Join-Path $Evidence "$mode.log"))
    $dynamic = if ($mode -eq 'jit') { 'True' } else { 'False' }
    $header = "mode=$mode; architecture=$Architecture; dynamic=$dynamic"
    if (@($log | Where-Object { $_ -ceq $header }).Count -ne 1) { throw "$mode runtime mode or ABI differs" }
    foreach ($suite in @('sha256', 'blake3')) {
        if (@($log | Where-Object { $_ -cmatch "^pack-$suite; physical-sha256=[0-9A-F]{64}$" }).Count -ne 1) {
            throw "$mode physical digest witness missing for $suite"
        }
    }
    if (@($log | Where-Object { $_ -cmatch '^native-open; operation=(open|CreateFileW); domain=(errno|Win32); error=[23]$' }).Count -ne 1) {
        throw "$mode native error witness missing"
    }
}
if ((Get-FileHash -LiteralPath (Join-Path $Evidence 'jit.txt')).Hash -cne
    (Get-FileHash -LiteralPath (Join-Path $Evidence 'aot.txt')).Hash) {
    throw 'JIT/AOT normalized evidence bytes differ'
}
Write-Host "PASS: 8/8 exact expected cases in each runtime; identical normalized evidence on $Architecture."
