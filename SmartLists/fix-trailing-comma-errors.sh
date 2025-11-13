#!/bin/bash
# Script to fix incorrectly added trailing commas from the previous script

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "🔧 Fixing incorrectly added trailing commas..."

# Find all .cs files and remove trailing commas after semicolons and closing braces
find . -type f -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" | while read file; do
    # Remove trailing comma after semicolon: "; ," -> ";"
    sed -i '' 's/; ,/;/g' "$file"
    
    # Remove trailing comma after closing brace on same line: "} ," -> "}"
    sed -i '' 's/} ,/}/g' "$file"
    
    # Remove trailing comma after throw: "throw; ," -> "throw;"
    sed -i '' 's/throw; ,/throw;/g' "$file"
    
    # Remove trailing comma after return statement: "return ...; ," -> "return ...;"
    sed -i '' 's/return \([^;]*\); ,/return \1;/g' "$file"
done

echo "✅ Fixed trailing comma errors"

