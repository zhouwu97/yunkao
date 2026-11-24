"""Real native mouse/keyboard regression for the reference tether motion.
Usage: python scripts/capture-pointer.py PID OUTPUT_DIRECTORY
"""
import ctypes
import json
import sys
import time
from pathlib import Path
import win32gui
from PIL import ImageGrab
from pywinauto import Application

pid, output = int(sys.argv[1]), Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
window = Application(backend='uia').connect(process=pid).top_window()
window.set_focus()
time.sleep(.4)
heading = window.child_window(auto_id='AnchoredHeading')
initial = heading.rectangle()
start = ((initial.left+initial.right)//2, initial.top-12)
records=[]
user32=ctypes.windll.user32

def move(x,y,delay=.25):
    width,height=user32.GetSystemMetrics(0),user32.GetSystemMetrics(1)
    user32.mouse_event(0x8001,round(x*65535/(width-1)),round(y*65535/(height-1)),0,0)
    actual=win32gui.GetCursorPos()
    if max(abs(actual[0]-x),abs(actual[1]-y))>6: raise RuntimeError(f"Move event mismatch: {actual} vs {(x,y)}")
    time.sleep(delay)
    actual=win32gui.GetCursorPos()
    if max(abs(actual[0]-x),abs(actual[1]-y))>6: raise RuntimeError(f"Input interrupted: {actual} vs {(x,y)}")

def press(): user32.mouse_event(2,0,0,0,0)
def release(): user32.mouse_event(4,0,0,0,0)
def capture(phase,held=False):
    if win32gui.GetForegroundWindow()!=window.handle: raise RuntimeError('Test window is not foreground')
    if bool(user32.GetAsyncKeyState(1)&0x8000)!=held: raise RuntimeError('Unexpected held state')
    h=heading.rectangle()
    left,top=win32gui.ClientToScreen(window.handle,(0,0))
    r=win32gui.GetClientRect(window.handle)
    ImageGrab.grab(bbox=(left,top,left+r[2],top+r[3]),all_screens=True).save(output/f'{phase}.png')
    row={'phase':phase,'held':held,'dx':h.left-initial.left,'dy':h.top-initial.top,'width':h.width(),'pointer':win32gui.GetCursorPos()}
    records.append(row)
    return row

move(*start)
capture('rest')
try:
    press()
    move(start[0]+65,start[1]-100,.45)
    row=capture('held',True)
    if not (4 <= row['dx'] <= 20 and -20 <= row['dy'] <= -4): raise RuntimeError(f'Foreground did not follow with restrained travel: {row}')
    move(start[0]-60,start[1]+85,.45)
    row=capture('reverse',True)
    if not (-20 <= row['dx'] <= -4 and 4 <= row['dy'] <= 20): raise RuntimeError(f'Reversal did not follow with restrained travel: {row}')
finally: release()
for i in range(5):
    time.sleep(.04)
    capture(f'release-{i}')
move(initial.left-60,initial.top-60,1.1)
row=capture('settled')
if abs(row['dx'])+abs(row['dy'])>1: raise RuntimeError('Tether did not return to rest')
button=window.child_window(auto_id='AnchoredButton')
def count(): return int(button.window_text().rsplit(' ',1)[-1])
before=count()
r=button.rectangle(); bx,by=(r.left+r.right)//2,(r.top+r.bottom)//2
try:
    move(bx,by); press(); move(bx+65,by-20,.35); capture('button-drag',True)
finally: release()
time.sleep(.5)
if count()!=before: raise RuntimeError('Dragging a button activated its click')
r=button.rectangle(); move((r.left+r.right)//2,(r.top+r.bottom)//2)
press(); time.sleep(.06); release(); time.sleep(.25)
if count()!=before+1: raise RuntimeError('Native tap did not activate exactly once')
button.set_focus(); button.type_keys('{SPACE}'); time.sleep(.2)
if count()!=before+2: raise RuntimeError('Native keyboard activation failed')
entry=window.child_window(auto_id='AnchoredEntry')
entry.set_focus(); entry.type_keys('native input editable',with_spaces=True)
if entry.get_value()!='native input editable': raise RuntimeError('Text input intercepted')
entry.type_keys('^a'); entry.type_keys('replacement')
if entry.get_value()!='replacement': raise RuntimeError('Text selection intercepted')
entry.type_keys('^a{BACKSPACE}')
capture('native-controls')
(output/'pointer.json').write_text(json.dumps({'states':records,'tap':True,'drag_suppressed':True,'keyboard':True,'editing_selection':True,'dpi':user32.GetDpiForWindow(window.handle)},indent=2),encoding='utf-8')
print(json.dumps(records,ensure_ascii=False))

