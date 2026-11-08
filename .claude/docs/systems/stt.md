# Speech-to-text chat (STT)

Lets a player speak into a microphone and have the recognized text appear in the game chat.

## Why a separate program
The engine exposes no microphone capture to content, and content runs in a sandbox (no processes, sockets or native libraries). Recognition therefore happens in an external companion program the player runs next to the game. It is purely client-side: the server never sees audio, and recognized text goes through the normal chat path, so all usual chat rules apply.

## Concept
- The companion records while "recording" is on, recognizes the speech locally and hands the text to the game through the shared user data directory (a drop folder, one file per phrase, written then renamed so the game never reads a partial phrase).
- The client polls the folder, consumes each phrase and puts it into the chat input.
- Recording is toggled either by a global hotkey in the companion or from the game via a console command, which writes a control file the companion watches. The command can be bound to a key.

## Review before sending
A client setting decides what happens to a phrase: with review on (default) it is placed in the chat input for the player to edit and send with Enter; with review off it is submitted immediately in the currently selected channel. The setting is toggled with a console command and persists.

## Limits and settings
Accepting STT can be disabled entirely, and phrase length is capped. All settings are client-only and archived.

## Where
Maid-owned client code and a companion script under the Maid tools folder; localization in the Maid locale folders. The companion's README has install and run instructions.
