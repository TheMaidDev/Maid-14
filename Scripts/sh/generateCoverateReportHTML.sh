#!/bin/env -S sh -e  # Run script with POSIX shell, exit immediately on error

SCRIPT_DIRECTORY=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &>/dev/null && pwd) # Get working directory of script location
cd "$SCRIPT_DIRECTORY/../../"                                                    # Change working directory to project root location

is_installed() {
    command -v "$1" >/dev/null 2>&1
}

if ! is_installed reportgenerator; then                                                     # Check if reportgenerator exists in PATH
    echo "reportgenerator not installed" >&2                                                # Print error message to stderr
    echo "run 'dotnet tool install -g dotnet-reportgenerator-globaltool' to install it" >&2 # Print help message to stderr
    exit 1                                                                                  # Exit script with failure status
fi

REPORTS='**/coverage.cobertura.xml' # Define generated coverage reports path
TARGET_DIRECTORY='coverage-report'  # Define generated coverage report output directory
FILE_FILTERS='-*.g.cs;-*.xaml'      # Define file filters to ignore files generated during tests run
REPORT_TYPES='Html'                 # Define type of generated coverage report

if is_installed gum; then                                                                               # Check if gum exists in PATH
    PROMPT="Generated coverage report output directory (leave empty for default '$TARGET_DIRECTORY'): " # Prompt text shown to user

    ANSWER=$(gum input --value "$PROMPT") # Read user input via gum, defaulting to PROMPT value

    if [[ "$ANSWER" != "$PROMPT" ]]; then # Check if user changed the value (not default)
        TARGET_DIRECTORY="$ANSWER"        # Update TARGET_DIRECTORY only if input differs
    fi
fi # End gum availability check

# Run ReportGenerator CLI
reportgenerator \
    -reports:"$REPORTS" \
    -targetdir:"$TARGET_DIRECTORY" \
    -filefilters:"$FILE_FILTERS" \
    -reporttypes:"$REPORT_TYPES"
