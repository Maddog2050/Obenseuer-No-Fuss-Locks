#!/usr/bin/env python3
"""Builds the two in-game prompt mock-ups, each a 1920x1080 frame with the prompt centred:

    prompt-game   Yes and No in the game's tab colours (selected tab #825112, other tabs #53380d)
    prompt-ochre  bright ochre Yes (#ad8433, dark text) with the game's dark orange No (#53380d)

Text uses the game's UI font, Erika Ormig, which is not part of this repository. Point GAME_FONT_DIR at a folder
holding ErikaOrmig.ttf (extracted from your own game install); otherwise librsvg falls back to another font.

    GAME_FONT_DIR=/path/to/font-folder python3 media/build-prompts.py
"""
import os, subprocess, tempfile
from html import escape
from pathlib import Path

P = Path(__file__).resolve().parent
FONT = 'Erika Ormig'
INK, MUTED = '#e2ded0', '#c8c5b8'  # as in-game (PromptUI)
PALETTES = {
    'prompt-game': dict(yes='#825112', yes_ink=INK, no='#53380d', no_line='#6b4a16', rail='#825112'),
    'prompt-ochre': dict(yes='#ad8433', yes_ink='#131714', no='#53380d', no_line='#5e430f', rail='#ad8433'),
}
W, H = 540, 224  # panel size


def rect(x, y, w, h, fill, stroke='none', sw=1):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>'


def text(x, y, s, size, fill=INK, anchor='start'):
    return (f'<text x="{x}" y="{y}" font-family="{FONT}" font-size="{size}" fill="{fill}" '
            f'text-anchor="{anchor}" xml:space="preserve">{escape(s)}</text>')


def button(pal, x, y, w, h, label, active):
    return (rect(x, y, w, h, pal['yes'] if active else pal['no'], '#68675f')
            + rect(x + 3, y + 3, w - 6, h - 6, 'none', '#d9cda044' if active else pal['no_line'])
            + text(x + w / 2, y + h / 2 + 7, label, 20, pal['yes_ink'] if active else INK, 'middle'))


def prompt(pal, x, y):
    """The asking state: dark double frame, accent rail on the left, title, content, Yes/No and the key hint."""
    s = rect(x - 5, y - 5, W + 10, H + 10, '#090b0a', '#383b34', 2)  # outer frame
    s += rect(x, y, W, H, '#121313', '#53564c')                    # inner panel
    s += rect(x, y, 4, H, pal['rail'])
    left = x + 26
    by, bw = y + H - 84, (W - 68) / 2
    s += text(left, y + 44, 'Pick this lock?', 26)
    s += text(left, y + 76, 'Lockpick — Easy.', 17, MUTED)
    s += text(left, y + 99, 'Anyone who sees you will report a burglary.', 17, MUTED)
    s += button(pal, left, by, bw, 40, 'Yes', True) + button(pal, x + 42 + bw, by, bw, 40, 'No', False)
    s += text(x + W / 2, y + H - 20, 'Y/Enter)YES   N/ESC)NO', 16, MUTED, 'middle')
    return s


def backdrop(w, h):
    # Abstract stand-in for the game scene, dimmed; not game pixels.
    s = rect(0, 0, w, h, '#1a1d1a')
    for i in range(0, w, 160):
        s += rect(i, 0, 70, h, '#1f231e')
    s += ''.join(f'<path d="M{(i * 137) % w} {(i * 79) % h}h{12 + i % 31}" stroke="#262b24"/>' for i in range(300))
    return s + rect(0, 0, w, h, '#00000066')


def render(name, body, env):
    (P / f'{name}.svg').write_text(
        f'<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080" viewBox="0 0 1920 1080">{body}</svg>')
    subprocess.run(['rsvg-convert', '-o', str(P / f'{name}.png'), str(P / f'{name}.svg')], check=True, env=env)


def main():
    env = dict(os.environ)
    font_dir = os.environ.get('GAME_FONT_DIR')
    if font_dir:
        conf = Path(tempfile.mkdtemp()) / 'fonts.conf'
        conf.write_text(f'<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig>'
                        f'<include ignore_missing="yes">/etc/fonts/fonts.conf</include><dir>{font_dir}</dir></fontconfig>')
        env['FONTCONFIG_FILE'] = str(conf)
    for name, pal in PALETTES.items():
        render(name, backdrop(1920, 1080) + prompt(pal, (1920 - W) / 2, (1080 - H) / 2), env)


main()
