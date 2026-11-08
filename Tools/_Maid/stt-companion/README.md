# STT companion

Local speech recognition for the in-game chat. See `.claude/docs/systems/stt.md` for the design.

## Run
    pip install -r requirements.txt
    python stt_companion.py --language ru

Options: `--data-dir` (game user data dir; default is the standard SS14 location), `--hotkey` (default `f9`, global toggle), `--model` (faster-whisper model, default `small`), `--fake` (test without mic/model).

Press the hotkey to start recording and again to stop; the recognized text appears in the game chat input.
You can also bind `stt_record` in-game to a key (`stt_record` toggles, `stt_record true|false` sets).

## In-game
- `stt_confirm` — toggle review before sending (on: text goes to the chat input, press Enter; off: sent immediately).
- CVars: `stt.enabled`, `stt.confirm`, `stt.max_length`.
