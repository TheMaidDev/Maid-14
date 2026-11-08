#!/usr/bin/env python3
"""
STT companion for Maid-14.

Records the microphone while "recording" is on, recognizes speech locally
(faster-whisper) and drops the text into <user data>/stt/inbox/ where the game
client (Content.Client/_Maid/Stt/SttSystem.cs) picks it up.

Recording is toggled by either:
  * the global hotkey (--hotkey, default f9), or
  * the in-game console command `stt_record`, which writes stt/control.txt.

Usage: python stt_companion.py [--data-dir DIR] [--hotkey KEY] [--model small] [--language ru] [--fake]
"""
import argparse
import os
import sys
import threading
import time
from pathlib import Path

SAMPLE_RATE = 16000


def default_data_dir() -> Path:
    if sys.platform.startswith("win"):
        return Path(os.environ.get("APPDATA", "~")).expanduser() / "Space Station 14" / "data"
    if sys.platform == "darwin":
        return Path("~/Library/Application Support/Space Station 14/data").expanduser()
    base = Path(os.environ.get("XDG_DATA_HOME", "~/.local/share")).expanduser()
    return base / "Space Station 14" / "data"


class Companion:
    def __init__(self, data_dir: Path, model_name: str, language: str | None, fake: bool):
        self.root = data_dir / "stt"
        self.inbox = self.root / "inbox"
        self.control = self.root / "control.txt"
        self.inbox.mkdir(parents=True, exist_ok=True)
        self.language = language
        self.fake = fake
        self.recording = False
        self.chunks: list = []
        self.lock = threading.Lock()
        self.stream = None
        self.model = None
        if not fake:
            from faster_whisper import WhisperModel
            self.model = WhisperModel(model_name, compute_type="int8")

    # --- recording -------------------------------------------------------
    def start(self):
        with self.lock:
            if self.recording:
                return
            self.recording = True
            self.chunks = []
        if not self.fake:
            import sounddevice as sd
            self.stream = sd.InputStream(samplerate=SAMPLE_RATE, channels=1, dtype="float32",
                                         callback=lambda data, *_: self.chunks.append(data.copy()))
            self.stream.start()
        print("[stt] recording...")

    def stop(self):
        with self.lock:
            if not self.recording:
                return
            self.recording = False
        if self.stream is not None:
            self.stream.stop()
            self.stream.close()
            self.stream = None
        print("[stt] stopped, recognizing...")
        threading.Thread(target=self._recognize, args=(self.chunks,), daemon=True).start()

    def toggle(self):
        self.stop() if self.recording else self.start()

    # --- recognition and delivery ---------------------------------------
    def _recognize(self, chunks):
        if self.fake:
            text = "тестовое сообщение"
        else:
            import numpy as np
            if not chunks:
                return
            audio = np.concatenate(chunks).flatten()
            segments, _ = self.model.transcribe(audio, language=self.language, vad_filter=True)
            text = " ".join(s.text.strip() for s in segments).strip()
        if text:
            self.deliver(text)

    def deliver(self, text: str):
        name = f"{time.time_ns():020d}.txt"
        tmp = self.inbox / (name + ".tmp")
        tmp.write_text(text, encoding="utf-8")
        # Atomic rename so the game never reads a half-written file.
        tmp.replace(self.inbox / name)
        print(f"[stt] sent: {text}")

    # --- control file ----------------------------------------------------
    def watch_control(self):
        last = None
        while True:
            try:
                value = self.control.read_text(encoding="utf-8").strip()
            except FileNotFoundError:
                value = None
            if value != last:
                last = value
                if value == "start":
                    self.start()
                elif value == "stop":
                    self.stop()
            time.sleep(0.2)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data-dir", type=Path, default=default_data_dir(), help="game user data directory")
    ap.add_argument("--hotkey", default="f9", help="global toggle key, empty to disable")
    ap.add_argument("--model", default="small", help="faster-whisper model name")
    ap.add_argument("--language", default=None, help="language code, e.g. ru (auto-detect if omitted)")
    ap.add_argument("--fake", action="store_true", help="no microphone/model, sends a test phrase on toggle")
    args = ap.parse_args()

    comp = Companion(args.data_dir, args.model, args.language, args.fake)
    print(f"[stt] inbox: {comp.inbox}")

    if args.hotkey:
        from pynput import keyboard
        listener = keyboard.GlobalHotKeys({f"<{args.hotkey}>": comp.toggle})
        listener.start()
        print(f"[stt] hotkey: {args.hotkey}")

    try:
        comp.watch_control()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
