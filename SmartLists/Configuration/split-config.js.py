#!/usr/bin/env python3
"""
Script to help split config.js into multiple files.
This script analyzes the file structure and provides guidance on function placement.
"""

import re
import sys

def analyze_file(filepath):
    """Analyze the config.js file and categorize functions."""
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Find all function definitions
    function_pattern = r'^\s*(async\s+)?function\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\([^)]*\)\s*\{'
    functions = []
    
    lines = content.split('\n')
    for i, line in enumerate(lines, 1):
        match = re.match(function_pattern, line)
        if match:
            is_async = bool(match.group(1))
            func_name = match.group(2)
            functions.append({
                'name': func_name,
                'line': i,
                'async': is_async,
                'line_content': line.strip()
            })
    
    # Categorize functions
    categories = {
        'core': [],
        'formatters': [],
        'schedules': [],
        'sorts': [],
        'rules': [],
        'playlists': [],
        'filters': [],
        'bulk_actions': [],
        'api': [],
        'init': []
    }
    
    for func in functions:
        name = func['name'].lower()
        
        # Categorize based on function name patterns
        if any(x in name for x in ['format', 'generate', 'escape', 'convert']):
            categories['formatters'].append(func)
        elif 'schedule' in name:
            categories['schedules'].append(func)
        elif 'sort' in name:
            categories['sorts'].append(func)
        elif any(x in name for x in ['rule', 'field', 'operator', 'value', 'input']):
            categories['rules'].append(func)
        elif any(x in name for x in ['playlist', 'create', 'edit', 'clone', 'delete', 'refresh', 'enable', 'disable']):
            categories['playlists'].append(func)
        elif any(x in name for x in ['filter', 'search', 'apply']):
            categories['filters'].append(func)
        elif 'bulk' in name:
            categories['bulk_actions'].append(func)
        elif any(x in name for x in ['api', 'load', 'get', 'post', 'put', 'fetch']):
            categories['api'].append(func)
        elif any(x in name for x in ['init', 'setup', 'event']):
            categories['init'].append(func)
        else:
            categories['core'].append(func)
    
    return categories, functions

def print_analysis(categories, functions):
    """Print analysis results."""
    print("=" * 80)
    print("FUNCTION ANALYSIS")
    print("=" * 80)
    print(f"\nTotal functions found: {len(functions)}\n")
    
    for category, funcs in categories.items():
        if funcs:
            print(f"\n{category.upper()} ({len(funcs)} functions):")
            for func in funcs:
                async_str = "async " if func['async'] else ""
                print(f"  {async_str}{func['name']} (line {func['line']})")

if __name__ == '__main__':
    filepath = 'config.js'
    if len(sys.argv) > 1:
        filepath = sys.argv[1]
    
    categories, functions = analyze_file(filepath)
    print_analysis(categories, functions)

