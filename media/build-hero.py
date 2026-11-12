#!/usr/bin/env python3
"""Builds the Nexus gallery's first image, media/hero.svg and media/hero.png (1920x1080).

Reuses build-banner.py's artwork and prompt helpers. The prompt cards use the game's UI font, Erika Ormig, which is
not part of this repository: point GAME_FONT_DIR at a folder holding ErikaOrmig.ttf (extracted from your own game
install). It is only used while rendering and is not embedded in the SVG.

    GAME_FONT_DIR=/path/to/font-folder python3 media/build-hero.py
"""
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from html import escape

P = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('banner', P / 'build-banner.py')
b = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(b)
GOLD = '#d4ad5c'
CREAM = '#eeeedd'


def txt(x, y, value, size, color=CREAM, weight=400, anchor='start'):
    return b.text(x, y, value, size, color, 'Fira Sans Condensed', weight, anchor)



def background():
    s = b.DEFS + b.rect(0, 0, 1920, 1080, 'url(#wall)')
    s += b.rect(0, 0, 1920, 1080, 'url(#light)')
    for x in (34, 1886):
        s += b.line(x, 0, x, 1080, '#0c1715', 4)
        s += b.line(x+4, 0, x+4, 1080, '#b1bea021')
        for y in (34, 1046):
            s += b.circle(x, y, 5, '#717d6666') + b.line(x-2, y, x+2, y, '#16241b', 2)
    for y in (34, 1046):
        s += b.line(0, y, 1920, y, '#0a1514', 3)
        s += b.line(0, y+4, 1920, y+4, '#a1af8d15')
    return s + b.rect(0, 0, 1920, 1080, 'url(#rain)') + b.rect(0, 0, 1920, 1080, 'url(#grain)')



def eyebrow(x, y):
    # build-banner.py's eyebrow (ochre dash, then the label, same colours) scaled from its 14 px text to 25 px.
    return b.line(x, y-9, x+54, y-9, GOLD, 7) + b.text(x+79, y, 'FOR OBENSEUER', 25, '#acb7a3', 'DejaVu Sans Mono', spacing=3.5)



def cursor(x, y, scale=1):
    # Small graphic cue only: the actual Yes/No button design stays unchanged.
    return f'<path transform="translate({x} {y}) scale({scale})" d="M0 0L0 56 15 43 26 67 39 61 28 38 48 37Z" fill="{CREAM}" stroke="#111b18" stroke-width="4"/>'



def back_card(x, y, scale, angle, title, content):
    # A card partly covered by the next one: same panel as build-banner.py's dialog(), but without the buttons and
    # key hint, whose top edges would otherwise peek out where the oppositely rotated cards' edges diverge.
    s = b.rect(-5, -5, 550, 234, '#090b0a', '#383b34', 2) + b.rect(0, 0, 540, 224, '#121313', '#53564c')
    s += b.rect(0, 0, 4, 224, '#825112')
    s += b.game_text(26, 44, title, 26) + b.game_text(26, 76, content, 17, b.MUTED)
    return f'<g transform="translate({x} {y}) scale({scale}) rotate({angle} 270 112)" filter="url(#cardshadow)">{s}</g>'



def card(kind, x, y, scale=1, angle=0):
    if kind == 'safe':
        return back_card(x, y, scale, angle, 'Crack this safe?', 'The safe opens without the combination.')
    if kind == 'keypad':
        return back_card(x, y, scale, angle, 'Bypass this keypad?', 'The keypad opens without the code.')
    return b.dialog(x, y, scale, angle=angle)



def layout():
    # Keep the plate uninterrupted behind the title and prompt cards.
    s = background() + b.rect(34, 34, 839, 1012, '#0b151633')
    s += eyebrow(110, 136)
    s += txt(98, 353, 'No-Fuss', 210, weight=700)
    s += txt(98, 548, 'Locks', 240, weight=700)
    s += b.line(110, 607, 717, 607, '#a0ae8b55', 3)
    s += txt(110, 718, 'One click.', 91, GOLD, 700)
    s += txt(110, 808, 'No minigame.', 78, '#cbd3bf')
    s += b.dial(1660, 850, 330, 8, .18)
    s += b.rich_lock(1035, 280, 2.15, -10)
    # Locked group: preserve relative offsets, individual scales and banner angles.
    stack = card('safe', 20, 0, 1.04, 1.2)
    stack += card('keypad', 5, 145, 1.08, -1.2)
    stack += card('lock', 0, 295, 1.08, .8)
    stack += cursor(1.08*205, 295+1.08*139, .7)
    s += f'<g id="locked-card-stack" transform="translate(1115 235) scale(1.2)">{stack}</g>'
    s += b.text(110, 981, 'LOCKS / SAFES / KEYPADS', 24, '#95a28b', 'Liberation Mono', spacing=3)
    return s


def main():
    font_dir = os.environ.get('GAME_FONT_DIR')
    if not font_dir or not (Path(font_dir) / 'ErikaOrmig.ttf').is_file():
        raise SystemExit('Set GAME_FONT_DIR to the folder containing ErikaOrmig.ttf.')
    with tempfile.TemporaryDirectory(prefix='nofuss-hero-fonts-') as tmp:
        conf = Path(tmp) / 'fonts.conf'
        conf.write_text('<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig><include>/etc/fonts/fonts.conf</include><dir>' + escape(str(Path(font_dir).resolve())) + '</dir><cachedir>' + tmp + '/cache</cachedir></fontconfig>')
        env = dict(os.environ, FONTCONFIG_FILE=str(conf))
        if 'Erika Ormig' not in subprocess.check_output(['fc-match', '-f', '%{family}', 'Erika Ormig'], env=env, text=True):
            raise SystemExit('Erika Ormig is unavailable through the temporary font configuration.')
        b.render('hero', 1920, 1080, layout(), env)
    print('Rendered media/hero.png at 1920x1080.')


if __name__ == '__main__':
    main()
