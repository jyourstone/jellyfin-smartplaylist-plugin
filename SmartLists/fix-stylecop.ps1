# PowerShell script to automatically fix StyleCop errors in SmartLists plugin
# This script fixes common formatting issues that can be automated

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "🔧 Starting StyleCop auto-fix process..." -ForegroundColor Cyan
Write-Host ""

# Step 1: Run dotnet format to fix many issues automatically
Write-Host "📝 Step 1: Running 'dotnet format' to fix code style issues..." -ForegroundColor Yellow
try {
    dotnet format --severity warn --include-generated 2>&1 | Out-Null
    Write-Host "✅ dotnet format completed" -ForegroundColor Green
} catch {
    Write-Host "⚠️  dotnet format completed with warnings (this is expected)" -ForegroundColor Yellow
}
Write-Host ""

# Step 2: Fix trailing whitespace (SA1028)
Write-Host "📝 Step 2: Removing trailing whitespace (SA1028)..." -ForegroundColor Yellow
$csFiles = Get-ChildItem -Path . -Filter "*.cs" -Recurse | 
    Where-Object { $_.FullName -notmatch "\\bin\\" -and $_.FullName -notmatch "\\obj\\" }

$fixedCount = 0
foreach ($file in $csFiles) {
    $content = Get-Content $file.FullName -Raw
    $newContent = $content -replace '[ \t]+(\r?\n)', '$1'
    if ($content -ne $newContent) {
        Set-Content -Path $file.FullName -Value $newContent -NoNewline
        $fixedCount++
    }
}
Write-Host "✅ Trailing whitespace removed from $fixedCount files" -ForegroundColor Green
Write-Host ""

# Step 3: Fix string.Empty issues (SA1122)
Write-Host "📝 Step 3: Replacing empty string literals with string.Empty (SA1122)..." -ForegroundColor Yellow
$fixedCount = 0
foreach ($file in $csFiles) {
    $content = Get-Content $file.FullName -Raw
    $original = $content
    
    # Replace = "" with = string.Empty (but be careful)
    $content = $content -replace '=\s*""(?=\s*[;,)])', '= string.Empty'
    # Replace == "" with == string.Empty
    $content = $content -replace '==\s*""(?=\s*[;,)])', '== string.Empty'
    # Replace != "" with != string.Empty
    $content = $content -replace '!=\s*""(?=\s*[;,)])', '!= string.Empty'
    
    if ($content -ne $original) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        $fixedCount++
    }
}
Write-Host "✅ Fixed string.Empty issues in $fixedCount files" -ForegroundColor Green
Write-Host ""

# Step 4: Run dotnet format again to finalize
Write-Host "📝 Step 4: Running 'dotnet format' again to finalize formatting..." -ForegroundColor Yellow
try {
    dotnet format --severity warn --include-generated 2>&1 | Out-Null
    Write-Host "✅ Final formatting completed" -ForegroundColor Green
} catch {
    Write-Host "⚠️  dotnet format completed with warnings (this is expected)" -ForegroundColor Yellow
}
Write-Host ""

Write-Host "✨ StyleCop auto-fix process completed!" -ForegroundColor Green
Write-Host ""
Write-Host "Note: Some issues may require manual fixes:" -ForegroundColor Yellow
Write-Host "  - SA1101: Prefix local calls with 'this' (1,608 errors) - requires semantic analysis"
Write-Host "  - SA1629: Documentation text should end with a period (528 errors) - requires manual review"
Write-Host "  - SA1117/SA1116: Parameter formatting (690 errors) - may be fixed by dotnet format"
Write-Host "  - SA1503: Braces (218 errors) - may be fixed by dotnet format"
Write-Host "  - SA1309: Field names with underscore (152 errors) - requires manual review"
Write-Host ""
Write-Host "Run 'dotnet build' to see remaining errors."

