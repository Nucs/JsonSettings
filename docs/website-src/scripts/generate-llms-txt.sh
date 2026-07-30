#!/bin/bash
# Auto-generate llms.txt and llms-full.txt from actual documentation.
# Parses toc.yml, markdown files, and API metadata - no hardcoded content.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="${1:-$SRC_DIR/../website}"
SITE_URL="${2:-https://nucs.github.io/JsonSettings}"

echo "Generating AI-friendly documentation files..."
echo "Source: $SRC_DIR"
echo "Output: $OUTPUT_DIR"

# ============================================================================
# Helper: Extract first paragraph from markdown (description)
# ============================================================================
extract_description() {
    local file="$1"
    awk '
        BEGIN { in_frontmatter=0; found=0 }
        /^---$/ {
            if (NR==1) { in_frontmatter=1; next }
            else { in_frontmatter=0; next }
        }
        in_frontmatter { next }
        /^[ \t]*#/ { next }
        /^[ \t]*$/ { if (found) exit; next }
        /^[^-\*\|>]/ {
            gsub(/\*\*/, ""); gsub(/\*/, ""); gsub(/`/, "")
            gsub(/^[ \t]+/, "")
            print; found=1
        }
    ' "$file" | head -2 | tr '\n' ' ' | sed 's/  */ /g' | head -c 200
}

# ============================================================================
# Helper: Extract title from markdown (first H1)
# ============================================================================
extract_title() {
    local file="$1"
    grep -m1 "^[ \t]*# " "$file" 2>/dev/null | sed 's/^[ \t]*# //' | sed 's/[ \t]*$//' || basename "$file" .md
}

# ============================================================================
# Generate llms.txt
# ============================================================================
generate_llms_txt() {
    local output="$OUTPUT_DIR/llms.txt"

    echo "# Nucs.JsonSettings" > "$output"
    echo "" >> "$output"

    # The homepage (index.md) is authored as HTML, so a fixed tagline reads better here than
    # scraping its first line.
    echo "> The easiest way to write settings for your .NET app - cross-platform, modular, one-liner, built on Json.NET." >> "$output"
    echo "" >> "$output"

    cat >> "$output" << 'QUICKSTART'
## Installation

```bash
dotnet add package Nucs.JsonSettings
dotnet add package Nucs.JsonSettings.Autosave
```

## Quick Start

```csharp
using Nucs.JsonSettings;

class MySettings : JsonSettings {
    public override string FileName { get; set; } = "config.json";
    public string Name { get; set; } = "default";
}

var settings = JsonSettings.Load<MySettings>("config.json");
settings.Name = "ok";
settings.Save();
```

QUICKSTART

    echo "## Documentation" >> "$output"
    echo "" >> "$output"

    if [ -d "$SRC_DIR/docs" ]; then
        for md_file in "$SRC_DIR/docs/"*.md; do
            if [ -f "$md_file" ]; then
                filename=$(basename "$md_file" .md)
                title=$(extract_title "$md_file")
                desc=$(extract_description "$md_file")
                echo "- [$title](${SITE_URL}/docs/${filename}.html): $desc" >> "$output"
            fi
        done
    fi
    echo "" >> "$output"

    echo "## API Reference" >> "$output"
    echo "" >> "$output"

    if [ -d "$SRC_DIR/api" ]; then
        for yml_file in "$SRC_DIR/api/"*.yml; do
            if [ -f "$yml_file" ]; then
                uid=$(grep -m1 "^uid:" "$yml_file" 2>/dev/null | sed 's/uid: *//' || true)
                summary=$(grep -m1 "summary:" "$yml_file" 2>/dev/null | sed 's/summary: *//' | sed 's/^"//' | sed 's/"$//' | head -c 120 || true)

                if [ -n "$uid" ] && [[ "$uid" == Nucs.JsonSettings* ]]; then
                    if [[ "$uid" =~ ^Nucs\.JsonSettings\.[A-Za-z0-9]+$ ]] || [[ "$uid" == "Nucs.JsonSettings" ]]; then
                        if [ -n "$summary" ]; then
                            echo "- [@$uid](${SITE_URL}/api/${uid}.html): $summary" >> "$output"
                        else
                            echo "- [@$uid](${SITE_URL}/api/${uid}.html)" >> "$output"
                        fi
                    fi
                fi
            fi
        done 2>/dev/null | head -20
    fi

    if ! grep -q "api/Nucs.JsonSettings" "$output" 2>/dev/null; then
        cat >> "$output" << 'API_FALLBACK'
- [Nucs.JsonSettings.JsonSettings](https://nucs.github.io/JsonSettings/api/Nucs.JsonSettings.JsonSettings.html): Abstract base class for hardcoded settings
- [Nucs.JsonSettings.SettingsBag](https://nucs.github.io/JsonSettings/api/Nucs.JsonSettings.SettingsBag.html): Dynamic key/value settings
- [Nucs.JsonSettings.Fluent.FluentJsonSettings](https://nucs.github.io/JsonSettings/api/Nucs.JsonSettings.Fluent.FluentJsonSettings.html): Fluent configuration extensions
API_FALLBACK
    fi
    echo "" >> "$output"

    cat >> "$output" << 'CONCEPTS'
## Key Concepts

- **JsonSettings**: Inherit this for a typed, hardcoded settings POCO.
- **SettingsBag**: A dynamic key/value settings object; no class to define.
- **Modules**: Encryption, Base64, Versioning, Recovery and Autosave attach per object.
- **Load / Construct / Configure**: Read a file, build an empty instance, or configure fluently.
- **Autosave**: Persist automatically on change (requires Nucs.JsonSettings.Autosave).

CONCEPTS

    echo "## Optional" >> "$output"
    echo "" >> "$output"
    echo "- [Full API Reference](${SITE_URL}/api/): Complete class and method documentation" >> "$output"
    echo "- [GitHub Repository](https://github.com/Nucs/JsonSettings): Source code and issues" >> "$output"
    echo "- [NuGet Package](https://www.nuget.org/packages/Nucs.JsonSettings): Latest releases" >> "$output"

    echo "Generated: $output ($(wc -l < "$output") lines)"
}

# ============================================================================
# Generate llms-full.txt
# ============================================================================
generate_llms_full_txt() {
    local output="$OUTPUT_DIR/llms-full.txt"

    echo "# Nucs.JsonSettings - Complete Documentation" > "$output"
    echo "" >> "$output"
    echo "> This file contains the complete Nucs.JsonSettings documentation for AI/LLM ingestion." >> "$output"
    echo "> Auto-generated from source markdown files." >> "$output"
    echo "" >> "$output"
    echo "---" >> "$output"
    echo "" >> "$output"

    echo "## Table of Contents" >> "$output"
    echo "" >> "$output"

    local toc_num=1
    local docs_dir="$SRC_DIR/docs"

    if [ -d "$docs_dir" ]; then
        for md_file in "$docs_dir/"*.md; do
            if [ -f "$md_file" ] && [[ "$(basename "$md_file")" != "toc.yml" ]]; then
                title=$(extract_title "$md_file")
                echo "$toc_num. $title" >> "$output"
                ((toc_num++))
            fi
        done
    fi
    echo "" >> "$output"
    echo "---" >> "$output"

    if [ -d "$docs_dir" ]; then
        for md_file in "$docs_dir/"*.md; do
            if [ -f "$md_file" ] && [[ "$(basename "$md_file")" != "toc.yml" ]]; then
                echo "" >> "$output"
                awk '
                    BEGIN { in_frontmatter=0 }
                    /^---$/ {
                        if (NR==1) { in_frontmatter=1; next }
                        else { in_frontmatter=0; next }
                    }
                    in_frontmatter { next }
                    { print }
                ' "$md_file" >> "$output"
                echo "" >> "$output"
                echo "---" >> "$output"
            fi
        done
    fi

    echo "" >> "$output"
    echo "*Auto-generated from Nucs.JsonSettings documentation source files*" >> "$output"

    echo "Generated: $output ($(wc -l < "$output") lines)"
}

# ============================================================================
# Generate robots.txt
# ============================================================================
generate_robots_txt() {
    local output="$OUTPUT_DIR/robots.txt"

    cat > "$output" << ROBOTS
# Nucs.JsonSettings Documentation - robots.txt
# Auto-generated - Allow all crawlers including AI

User-agent: *
Allow: /

User-agent: GPTBot
Allow: /

User-agent: ChatGPT-User
Allow: /

User-agent: Claude-Web
Allow: /

User-agent: ClaudeBot
Allow: /

User-agent: Anthropic-AI
Allow: /

User-agent: Google-Extended
Allow: /

User-agent: PerplexityBot
Allow: /

User-agent: CCBot
Allow: /

# AI-friendly documentation files
# ${SITE_URL}/llms.txt - Curated summary
# ${SITE_URL}/llms-full.txt - Complete documentation

Sitemap: ${SITE_URL}/sitemap.xml
ROBOTS

    echo "Generated: $output"
}

# ============================================================================
# Main
# ============================================================================

mkdir -p "$OUTPUT_DIR"

generate_llms_txt
generate_llms_full_txt
generate_robots_txt

echo ""
echo "AI-friendly documentation generation complete!"
