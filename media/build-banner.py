#!/usr/bin/env python3
"""Builds the Nexus page banner, media/banner.svg and media/banner.png (1300x372).

The prompt cards use the game's UI font, Erika Ormig, which is not part of this repository. Point GAME_FONT_DIR at a
folder holding ErikaOrmig.ttf (extracted from your own game install); it is only used while rendering and is not
embedded in the SVG.

    GAME_FONT_DIR=/path/to/font-folder python3 media/build-banner.py
"""
import math
import os
from pathlib import Path
import struct
import subprocess
import tempfile
from html import escape

P = Path(__file__).resolve().parent
INK, MUTED = '#e2ded0', '#c8c5b8'  # as in-game (PromptUI)

def rect(x, y, w, h, fill, stroke='none', sw=1, rx=0):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" rx="{rx}"/>'

def text(x, y, value, size=20, fill=INK, font='Fira Sans Condensed', weight=400, anchor='start', spacing=0):
    return f'<text x="{x}" y="{y}" font-family="{font}" font-size="{size}" font-weight="{weight}" fill="{fill}" text-anchor="{anchor}" letter-spacing="{spacing}">{escape(value)}</text>'

def line(x, y, x2, y2, color, width=1):
    return f'<path d="M{x} {y}L{x2} {y2}" fill="none" stroke="{color}" stroke-width="{width}"/>'

def circle(x, y, r, fill='none', stroke='none', sw=1):
    return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>'

def label(x, y, value, color):
    return text(x,y,value,14,color,'Liberation Mono',spacing=1.8)

def title(x,y,size=88,color=INK):
    return text(x,y,'No-Fuss Locks',size,color,'Fira Sans Condensed',700)

def game_text(x,y,value,size,fill=INK,anchor='start'):
    # xml:space keeps the run of spaces in the key hint (SVG collapses whitespace by default).
    return text(x,y,value,size,fill,'Erika Ormig',anchor=anchor).replace('<text ','<text xml:space="preserve" ',1)

def button(x,y,label,yes=False):
    w,h=236,40
    return (rect(x,y,w,h,'#825112' if yes else '#53380d','#68675f')
            +rect(x+3,y+3,w-6,h-6,'none','#d9cda044' if yes else '#6b4a16')
            +game_text(x+w/2,y+27,label,20,anchor='middle'))

def dialog(x,y,scale=1,title='Pick this lock?',content=('Lockpick — Easy.','Anyone who sees you will report a burglary.'),angle=0):
    """A prompt card: the same 540×224 geometry and game palette as prompt-game (build-prompts.py)."""
    s=rect(-5,-5,550,234,'#090b0a','#383b34',2)+rect(0,0,540,224,'#121313','#53564c')+rect(0,0,4,224,'#825112')
    s+=game_text(26,44,title,26)
    s+=game_text(26,76,content[0],17,MUTED)+game_text(26,99,content[1],17,MUTED)
    s+=button(26,140,'Yes',True)+button(278,140,'No')
    s+=game_text(270,204,'Y/Enter)YES   N/ESC)NO',16,MUTED,'middle')
    return f'<g transform="translate({x} {y}) scale({scale}) rotate({angle} 270 112)" filter="url(#cardshadow)">{s}</g>'

# Card fan placement: scaled up for readability, right edge kept inside the 40 px margin.
FAN_SCALE, FAN_X, FAN_Y = .86, 212, 14

DEFS='''<defs>
 <linearGradient id="wall" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#35423c"/><stop offset=".54" stop-color="#202e2a"/><stop offset="1" stop-color="#111c1d"/></linearGradient>
 <radialGradient id="light"><stop stop-color="#9aab7c" stop-opacity=".10"/><stop offset="1" stop-color="#8b9c72" stop-opacity="0"/></radialGradient>
 <linearGradient id="metal" x1="0" y1="0" x2=".8" y2="1"><stop stop-color="#7c8471"/><stop offset=".35" stop-color="#5c6b59"/><stop offset="1" stop-color="#2c3e37"/></linearGradient>
 <linearGradient id="shackle" x1="0" y1="0" x2="1" y2=".6"><stop stop-color="#737f70"/><stop offset=".45" stop-color="#c3c8b1"/><stop offset="1" stop-color="#7f8b78"/></linearGradient>
 <pattern id="rain" width="61" height="91" patternUnits="userSpaceOnUse" patternTransform="rotate(17)"><path d="M12 0v36M43 55v22" stroke="#c6d2c6" stroke-opacity=".047"/></pattern>
 <pattern id="grain" width="137" height="73" patternUnits="userSpaceOnUse"><path d="M4 7h17m49 42h29m13-26h7M31 68h12M87 4h3" stroke="#dae1ca" opacity=".06"/><path d="M0 38h37m64 32h35" stroke="#000" opacity=".19"/></pattern>
 <filter id="cardshadow" x="-20%" y="-40%" width="140%" height="190%"><feDropShadow dx="0" dy="7" stdDeviation="7" flood-color="#000" flood-opacity=".42"/></filter>
 <filter id="lockshadow" x="-40%" y="-40%" width="190%" height="200%"><feDropShadow dx="3" dy="10" stdDeviation="9" flood-color="#030b08" flood-opacity=".5"/></filter>
</defs>'''

def wall(seam):
    s=DEFS+rect(0,0,1300,372,'url(#wall)')+rect(0,0,1300,372,'url(#light)')
    for x in [24,seam,1276]:
        s+=line(x,0,x,372,'#0c1715',3)+line(x+3,0,x+3,372,'#b1bea021')
        for y in [24,348]:s+=circle(x,y,3,'#717d6666')+line(x-1,y,x+1,y,'#16241b')
    for y in [24,348]:s+=line(0,y,1300,y,'#0a15144d',3)+line(0,y+3,1300,y+3,'#a1af8d15')
    return s+rect(0,0,1300,372,'url(#rain)')+rect(0,0,1300,372,'url(#grain)')

def dial(x,y,r=120,angle=0,opacity=.2):
    s=''
    for radius,width in [(108,3),(90,1),(64,14),(23,3)]:s+=circle(0,0,radius,'#1b2922' if radius==23 else 'none','#a4af99',width)
    for i in range(40):
        a=i*math.pi/20;inner=92 if i%5 else 84
        s+=line(math.sin(a)*inner,-math.cos(a)*inner,math.sin(a)*102,-math.cos(a)*102,'#a4af99',1 if i%5 else 3)
    s+=line(0,-42,0,-25,'#b1b99c',5)
    return f'<g transform="translate({x} {y}) rotate({angle}) scale({r/108})" opacity="{opacity}">{s}</g>'

def rich_lock(x,y,scale=1,angle=-10):
    s='<path d="M-42 34v-58c0-63 90-63 90-5" fill="none" stroke="#111b1b" stroke-width="25"/>'
    s+='<path d="M-42 34v-58c0-63 90-63 90-5" fill="none" stroke="url(#shackle)" stroke-width="17"/>'
    s+='<path d="M-45 20v-42c0-27 18-42 37-43" fill="none" stroke="#e1e3c9" stroke-width="3" opacity=".62"/>'
    s+=rect(-68,22,136,120,'url(#metal)','#929e81',2,9)
    s+='<path d="M-55 34H55M-57 129h51" fill="none" stroke="#e1dec2" opacity=".3"/>'
    s+=circle(0,74,13,'#15231e')+'<path d="M-5 80l-5 28h20L5 80" fill="#15231e"/>'
    s+='<path d="M-60 117l23-5m62-64 28-5m-12 82 19-3M-53 67l19-5M21 112l27-7M-54 100l8-2" fill="none" stroke="#c4c9a7" opacity=".3"/>'
    s+='<path d="M-64 43v67M61 40v78" stroke="#0e2418" opacity=".24"/>'
    return f'<g transform="translate({x} {y}) rotate({angle}) scale({scale})" filter="url(#lockshadow)">{s}</g>'

def brand(x,y,size=90):
    return title(x,y,size,'#eeeedd')

def eyebrow(x,y):
    return line(x,y-5,x+30,y-5,'#d4ad5c',4)+text(x+44,y,'FOR OBENSEUER',14,'#acb7a3','DejaVu Sans Mono',spacing=2)

def strap(x,y):
    # Same type as the hero image's strap line, on one line: Fira Sans Condensed, "One click." bold in the title ochre.
    return text(x,y,'One click. No minigame.',26,'#cbd3bf','Fira Sans Condensed').replace('>One click.','><tspan fill="#d4ad5c" font-weight="700">One click.</tspan>')

def footer(x,y):return label(x,y,'LOCKS / SAFES / KEYPADS','#95a28b')

def layout():
    s=wall(665)+dial(94,368,153,9,.15)
    s+=eyebrow(59,75)+brand(53,164,94)+strap(59,212)
    s+=line(60,248,437,248,'#8c9a7d44')+footer(60,286)
    # Open padlock; its whole shackle stays left of the card fan, only the body's right edge is overlapped.
    s+=rich_lock(748,126,1,-9)
    fan=dialog(723,49,.9,title='Crack this safe?',content=('The safe opens without the combination.',''),angle=1.2)
    # Only the title shows above the front card; the description line would peek through the gap under it.
    fan+=dialog(710,101,.9,title='Bypass this keypad?',content=('',''),angle=-1.2)
    # Front card the same scale as the two behind it, so neither peeks out past it, and low enough to leave a gap
    # under the keypad card's title.
    fan+=dialog(728,165,.9,angle=.8)
    s+=f'<g transform="translate({FAN_X} {FAN_Y}) scale({FAN_SCALE})">{fan}</g>'
    return s

def render(name,w,h,body,env):
    (P/f'{name}.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{body}</svg>')
    subprocess.run(['rsvg-convert','-o',str(P/f'{name}.png'),str(P/f'{name}.svg')],env=env,check=True)
    assert struct.unpack('>II',(P/f'{name}.png').read_bytes()[16:24])==(w,h)

def main():
    font_dir=os.environ.get('GAME_FONT_DIR')
    if not font_dir or not Path(font_dir).is_dir():raise SystemExit('Set GAME_FONT_DIR to the folder containing the extracted Erika Ormig font.')
    with tempfile.TemporaryDirectory(prefix='nofuss-banner-fonts-') as tmp:
        conf=Path(tmp)/'fonts.conf'
        conf.write_text('<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig><include>/etc/fonts/fonts.conf</include><dir>'+escape(str(Path(font_dir).resolve()))+'</dir><cachedir>'+tmp+'/cache</cachedir></fontconfig>')
        env=dict(os.environ,FONTCONFIG_FILE=str(conf))
        if 'Erika Ormig' not in subprocess.check_output(['fc-match','-f','%{family}','Erika Ormig'],env=env,text=True):raise SystemExit('Erika Ormig is not available through the temporary font configuration.')
        render('banner',1300,372,layout(),env)
    print('Rendered media/banner.png at 1300x372.')

if __name__=='__main__':main()
