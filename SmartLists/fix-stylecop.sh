#!/bin/bash
# Script to automatically fix StyleCop errors in SmartLists plugin
# This script fixes common formatting issues that can be automated

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "🔧 Starting StyleCop auto-fix process..."
echo ""

# Step 1: Run dotnet format to fix many issues automatically
echo "📝 Step 1: Running 'dotnet format' to fix code style issues..."
dotnet format --severity warn --include-generated || {
    echo "⚠️  dotnet format completed with warnings (this is expected)"
}
echo "✅ dotnet format completed"
echo ""

# Step 2: Fix trailing whitespace (SA1028) - 2,912 errors
echo "📝 Step 2: Removing trailing whitespace (SA1028)..."
find . -type f -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" -exec sed -i '' 's/[[:space:]]*$//' {} \;
echo "✅ Trailing whitespace removed"
echo ""

# Step 3: Fix string.Empty issues (SA1122) - 184 errors
echo "📝 Step 3: Replacing empty string literals with string.Empty (SA1122)..."
# This is more complex - we'll use a Python script for this
python3 << 'PYTHON_SCRIPT'
import re
import os
import sys

def fix_string_empty(filepath):
    """Replace empty string literals with string.Empty"""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        original = content
        
        # Pattern to match empty string literals in assignments and comparisons
        # Match: var x = ""; or if (x == "") or x = "";
        # But avoid: var x = "something"; or comments
        
        # Replace = "" with = string.Empty (but be careful with string interpolation)
        content = re.sub(r'=\s*""(?=\s*[;,)])', '= string.Empty', content)
        # Replace == "" with == string.Empty
        content = re.sub(r'==\s*""(?=\s*[;,)])', '== string.Empty', content)
        # Replace != "" with != string.Empty
        content = re.sub(r'!=\s*""(?=\s*[;,)])', '!= string.Empty', content)
        
        if content != original:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)
            return True
        return False
    except Exception as e:
        print(f"Error processing {filepath}: {e}", file=sys.stderr)
        return False

# Find all .cs files
fixed_count = 0
for root, dirs, files in os.walk('.'):
    # Skip bin and obj directories
    dirs[:] = [d for d in dirs if d not in ['bin', 'obj']]
    
    for file in files:
        if file.endswith('.cs'):
            filepath = os.path.join(root, file)
            if fix_string_empty(filepath):
                fixed_count += 1

print(f"✅ Fixed string.Empty issues in {fixed_count} files")
PYTHON_SCRIPT
echo ""

# Step 4: Add trailing commas in multi-line initializers (SA1413) - DISABLED
# This step is disabled because it's too error-prone and SA1413 is already suppressed in .editorconfig
echo "📝 Step 4: Skipping trailing comma fixes (SA1413 is suppressed in .editorconfig)..."
# The trailing comma script is commented out to avoid breaking code
: '
python3 << 'PYTHON_SCRIPT_DISABLED'
import re
import os

def fix_trailing_commas(filepath):
    """Add trailing commas in multi-line initializers"""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        modified = False
        new_lines = []
        
        for i, line in enumerate(lines):
            # Check if this is a multi-line initializer that needs a trailing comma
            # Pattern: lines ending with a value/item but not a comma, followed by a closing brace
            if i < len(lines) - 1:
                next_line = lines[i + 1].strip()
                current_line = line.rstrip()
                
                # If current line has content, doesn't end with comma, and next line starts with }
                if (current_line and 
                    not current_line.endswith(',') and 
                    not current_line.endswith('{') and
                    not current_line.endswith('(') and
                    not current_line.endswith('[') and
                    next_line.startswith('}') and
                    not current_line.strip().startswith('//') and
                    not current_line.strip().startswith('*')):
                    # Add trailing comma
                    line = line.rstrip() + ',\n'
                    modified = True
            
            new_lines.append(line)
        
        if modified:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.writelines(new_lines)
            return True
        return False
    except Exception as e:
        return False

fixed_count = 0
for root, dirs, files in os.walk('.'):
    dirs[:] = [d for d in dirs if d not in ['bin', 'obj']]
    for file in files:
        if file.endswith('.cs'):
            filepath = os.path.join(root, file)
            if fix_trailing_commas(filepath):
                fixed_count += 1

print(f"✅ Fixed trailing comma issues in {fixed_count} files")
PYTHON_SCRIPT
echo ""

# Step 5: Run dotnet format again to clean up any formatting issues
echo "📝 Step 5: Running 'dotnet format' again to finalize formatting..."
dotnet format --severity warn --include-generated || {
    echo "⚠️  dotnet format completed with warnings (this is expected)"
}
echo "✅ Final formatting completed"
echo ""

echo "✨ StyleCop auto-fix process completed!"
echo ""
echo "Note: Some issues may require manual fixes:"
echo "  - SA1101: Prefix local calls with 'this' (1,608 errors) - requires semantic analysis"
echo "  - SA1629: Documentation text should end with a period (528 errors) - requires manual review"
echo "  - SA1117/SA1116: Parameter formatting (690 errors) - may be fixed by dotnet format"
echo "  - SA1503: Braces (218 errors) - may be fixed by dotnet format"
echo "  - SA1309: Field names with underscore (152 errors) - requires manual review"
echo ""
echo "Run 'dotnet build' to see remaining errors."

