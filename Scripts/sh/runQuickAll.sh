# SPDX-FileCopyrightText: 2024 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
# SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
#
# SPDX-License-Identifier: AGPL-3.0-or-later

#!/usr/bin/env sh

command -v tmux >/dev/null 2>&1 || { # Check if tmux exists in PATH
    echo "tmux not installed" >&2    # Print error message to stderr
    exit 1                           # Exit script with failure status
}

# make sure to start from script dir
if [ "$(dirname $0)" != "." ]; then
    cd "$(dirname $0)"
fi

SESSION="quick" # Define tmux session name

if tmux has-session -t "$SESSION" 2>/dev/null; then # Check if session already exists
    :                                               # Do nothing if session exists
else                                                # If session does not exist
    tmux new-session -d -s "$SESSION" -n "$SESSION" # Create detached tmux session and initial window

    tmux split-window -h -t "$SESSION":0 # Split first window horizontally into two panes

    tmux send-keys -t "$SESSION":0.0 'sh -e runQuickServer.sh' C-m # Start server script in left pane
    tmux send-keys -t "$SESSION":0.1 'sh -e runQuickClient.sh' C-m # Start client script in right pane

    tmux set-option -p -t "$SESSION":0.0 @title "Server" # Set custom pane metadata title for left pane
    tmux set-option -p -t "$SESSION":0.1 @title "Client" # Set custom pane metadata title for right pane

    tmux set-option -t "$SESSION" pane-border-status top         # Enable pane border status display
    tmux set-option -t "$SESSION" pane-border-format "#{@title}" # Display custom @title in pane border

    tmux rename-window -t "$SESSION":0 "Server | Client # (Ctrl-b: ←/→ switch | d detach)" # Set window title with hint
fi

if [ -n "$TMUX" ]; then              # Check if already inside a tmux session
    tmux switch-client -t "$SESSION" # Switch current client to target session
else                                 # If not inside tmux
    tmux attach -t "$SESSION"        # Attach to session in new tmux client
fi
