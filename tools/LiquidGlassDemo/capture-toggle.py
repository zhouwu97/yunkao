"""Real pointer/keyboard/UIA tests for the glass switch. No product settings are used."""
import ctypes,json,sys,time,win32gui
from pathlib import Path
from pywinauto import Application
from PIL import ImageGrab
w=Application(backend='uia').connect(process=int(sys.argv[1])).top_window();w.set_focus();time.sleep(.15)
t=w.child_window(auto_id='GlassToggle').wrapper_object()
output=Path(sys.argv[2]);output.mkdir(parents=True,exist_ok=True)
u=ctypes.windll.user32
r=t.rectangle();dpi=u.GetDpiForWindow(w.handle)/96
left=int(r.left+13*dpi);right=int(left+22*dpi);cy=(r.top+r.bottom)//2
rows=[]
def move(x,y,delay=.16):
 u.mouse_event(0x8001,round(x*65535/(u.GetSystemMetrics(0)-1)),round(y*65535/(u.GetSystemMetrics(1)-1)),0,0);time.sleep(delay)
 if max(abs(a-b) for a,b in zip(win32gui.GetCursorPos(),(x,y)))>6: raise RuntimeError('Input interrupted')
def down():u.mouse_event(2,0,0,0,0)
def up():u.mouse_event(4,0,0,0,0)
def state():return t.iface_toggle.CurrentToggleState
def capture(phase,expected):
 if win32gui.GetForegroundWindow()!=w.handle:raise RuntimeError('Not foreground')
 actual=state()
 if actual!=expected:raise RuntimeError(f'{phase}: {actual} vs {expected}')
 x,y=win32gui.ClientToScreen(w.handle,(0,0));cr=win32gui.GetClientRect(w.handle)
 ImageGrab.grab(bbox=(x,y,x+cr[2],y+cr[3]),all_screens=True).save(output/(phase+'.png'))
 rows.append({'phase':phase,'state':actual})
if state():t.iface_toggle.Toggle()
time.sleep(.5)
capture('off',0)
try:
 move(left,cy);down();time.sleep(.2);capture('pressed',0)
 move(right,cy,.3);capture('preview-right',0)
 move(left,cy,.3);capture('preview-left',0)
 move(right,cy,.15)
finally:up()
time.sleep(.6);capture('on',1)
try:
 move(right,cy);down();move(left,cy,.3);capture('preview-off',1)
finally:up()
time.sleep(.6);capture('off-again',0)
try:
 move(left,cy);down();move(right,cy,.2);t.type_keys('{ESC}')
finally:up()
time.sleep(.5);capture('cancelled',0)
t.set_focus();t.type_keys('{SPACE}');time.sleep(.5);capture('keyboard-on',1)
t.iface_toggle.Toggle();time.sleep(.5);capture('automation-off',0)
(output/'toggle.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
print(json.dumps(rows))
