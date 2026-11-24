// Execute the actual upstream controller at the pinned revision; never duplicate its solver.
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
const checkout = resolve(process.argv[2]);
const revision = execFileSync('git', ['-C', checkout, 'rev-parse', 'HEAD'], {encoding:'utf8'}).trim();
if (revision !== '7a424c93b9be13cf835b91ea67495fec2ec0584e') throw Error('Unexpected reference revision');
const { JellyController } = await import(pathToFileURL(resolve(checkout,'lens/lab/design-system/internal/jelly.js')));
const { Spring } = await import(pathToFileURL(resolve(checkout,'lens/lab/design-system/internal/jelly-physics.js')));
const b = {mode:'tether',held:false,angle:0, x:new Spring(),y:new Spring(),press:new Spring(),
  stretch:new Spring(),bendX:new Spring(),bendY:new Spring(),
  element:{getClientRects:()=>[{}],style:{setProperty(){}},classList:{toggle(){}}}};
const state = {frame:0,last:0,intensity:.7,reduced:false,bodies:new Map([[0,b]]),wake(){}};
const events = new Map([[0,['press',0,0]],[1,['pull',65,-40]],[16,['pull',-70,60]],
  [27,['release',0,0]],[33,['press',20,10]],[34,['pull',58,32]],[51,['release',0,0]]]);
let baseX=0,baseY=0,startX=0,startY=0;
const frames=[];
for(let i=0;i<210;i++) {
  const [action,x,y]=events.get(i)||['none',0,0];
  if(action==='press') {startX=x;startY=y;baseX=b.x.target;baseY=b.y.target;b.held=true;b.press.target=1;}
  if(action==='pull') {b.x.target=Math.max(-75,Math.min(75,baseX+x-startX));b.y.target=Math.max(-75,Math.min(75,baseY+y-startY));}
  if(action==='release') {b.held=false;b.press.target=b.x.target=b.y.target=0;}
  JellyController.prototype.tick.call(state,(i+1)*1000/60);
  frames.push({action,x,y,positionX:b.x.value,positionY:b.y.value,pressure:b.optical.pressure,
    stretch:b.stretch.value,angle:b.angle,bendX:b.bendX.value,bendY:b.bendY.value,energy:b.optical.energy});
}
writeFileSync(new URL('../../src/YunKao.Tests/Fixtures/reference-tether.json',import.meta.url),JSON.stringify({revision,frames}));
