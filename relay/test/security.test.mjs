import test from 'node:test';
import assert from 'node:assert/strict';
import { Service, SKINS } from '../src/service.mjs';
import { token, resolve, SIDES } from '../src/dice.mjs';

function harness() {
  let now = 1000000;
  const admin = token();
  let service = new Service(undefined, admin, () => now);
  async function call(path, body, user, gm, adminCall = false) {
    const headers = {'content-type':'application/json'};
    if (user || adminCall) headers.Authorization = `Bearer ${adminCall ? admin : user.token}`;
    if (gm) headers['X-GM-Session'] = gm.token;
    const response = await service.fetch(new Request(`https://relay.test/v1/${path}`, {method:'POST',headers,body:JSON.stringify(body)}));
    return {status:response.status, body:await response.json()};
  }
  async function create() { return (await call('create',{name:'Owner'})).body; }
  async function join(room,name='Player') { return (await call('join',{room,name})).body; }
  async function authenticate(user) {
    const issued = (await call('admin/issue',{label:'GM'},null,null,true)).body;
    const auth = await call('auth',{room:user.room,credential:issued.credential},user);
    assert.equal(auth.status,200);
    return {...auth.body, id:issued.id, credential:issued.credential};
  }
  return {call,create,join,authenticate,advance:n=>now+=n,state:()=>service.state,restart:()=>{service=new Service(structuredClone(service.state),admin,()=>now);}};
}
test('all dice counts and large modifiers produce legal tuples with matching totals', () => {
  for (const sides of SIDES) for (const count of [1,2,3,10,20]) for (const modifier of [-2000,-6,0,4,2000]) {
    const roll = resolve(count,sides,modifier);
    assert.equal(roll.faces.length,count);
    assert.equal(roll.total,roll.faces.reduce((a,b)=>a+b,0));
    assert(roll.faces.every(x=>x>=1 && x<=sides));
    if (modifier === 2000) assert.equal(roll.total,count*sides);
    if (modifier === -2000) assert.equal(roll.total,count);
  }
});
test('3d8 plus 4 per die clamps each die at eight', () => {
  const natural = [7,7,5]; // zero-based RNG: faces 8,8,6
  assert.deepEqual(resolve(3,8,4,max=>natural.length ? natural.shift() : 0),{faces:[8,8,8],total:24});
  assert.throws(()=>resolve(0,20,0)); assert.throws(()=>resolve(21,20,0)); assert.throws(()=>resolve(1,7,0));
});
test('full modifier applies to every die, with no overflow transferred to another face', () => {
  function roll(faces,sides,modifier) {
    const input=[...faces];
    const result=resolve(input.length,sides,modifier,()=>input.shift()-1);
    assert.equal(input.length,0);
    return result;
  }
  assert.deepEqual(roll([1,2,3,4],20,4),{faces:[5,6,7,8],total:26});
  assert.deepEqual(roll([1,2,3,4],20,0),{faces:[1,2,3,4],total:10});
  assert.deepEqual(roll([1,8,2,7],8,4),{faces:[5,8,6,8],total:27});
  assert.deepEqual(roll([1,8,2,7],8,-4),{faces:[1,4,1,3],total:9});
  assert.deepEqual(roll([40,80],100,4),{faces:[44,84],total:128});
  for (const sides of SIDES) for (const modifier of [-4,0,4]) for (let face=1;face<=sides;face++) {
    const expected=Math.max(1,Math.min(sides,face+modifier));
    assert.deepEqual(roll([face],sides,modifier),{faces:[expected],total:expected});
    assert.deepEqual(roll([face,face,face,face],sides,modifier),{faces:[expected,expected,expected,expected],total:4*expected});
  }
});
test('creator has no GM authority, forged roles and client-provided outcomes rejected', async () => {
  const h=harness(), owner=await h.create();
  assert.equal((await h.call('poll',{room:owner.room,after:0},owner)).body.canManage,false);
  for (const path of ['gm/set','gm/modifiers','gm/clear']) assert.equal((await h.call(path,{room:owner.room,participant:owner.participant,value:5},owner)).status,403);
  const request={room:owner.room,count:1,sides:20,requestId:crypto.randomUUID()};
  for (const extra of [{role:'GM'},{modifier:4},{faces:[20]},{participant:'someone'},{total:20}]) assert.equal((await h.call('roll',{...request,...extra},owner)).status,400);
});
test('per-person modifiers, strict public payloads, independent animation and idempotency', async () => {
  const h=harness(), owner=await h.create(), player=await h.join(owner.room), gm=await h.authenticate(owner);
  assert.equal((await h.call('gm/set',{room:owner.room,participant:player.participant,value:2000},owner,gm)).status,200);
  assert.equal((await h.call('gm/set',{room:owner.room,participant:owner.participant,value:-2000},owner,gm)).status,200);
  const request={room:player.room,count:3,sides:8,requestId:crypto.randomUUID()};
  const result=await h.call('roll',request,player);
  assert.equal(result.status,200); assert.equal(result.body.total,24);
  assert.deepEqual(result.body.faces,[8,8,8]);
  assert.deepEqual(Object.keys(result.body).sort(),['id','sequence','participant','name','skin','count','sides','faces','total','animationSeed','startsAt','durationMs'].sort());
  assert.deepEqual((await h.call('roll',request,player)).body,result.body);
  assert.equal((await h.call('roll',{...request,count:1},player)).status,409);
  const low=await h.call('roll',{...request,requestId:crypto.randomUUID()},owner);
  assert.equal(low.body.total,3);
  assert.notEqual(low.body.animationSeed,result.body.animationSeed);
  assert(result.body.durationMs>=2400 && result.body.durationMs<=5000);
  assert(low.body.startsAt>=result.body.startsAt+result.body.durationMs+500);
  const poll=(await h.call('poll',{room:player.room,after:0},player)).body;
  assert.deepEqual(Object.keys(poll).sort(),['serverTime','sequence','participants','rolls','canManage','gmExpires'].sort());
  assert.equal(poll.canManage,false);
  assert.deepEqual(poll.rolls,[result.body,low.body]);
  for (const p of poll.participants) assert.deepEqual(Object.keys(p).sort(),['id','name']);
  assert(!/modifier|credential|digest|natural|audit/.test(JSON.stringify(poll)));
  assert.equal((await h.call('gm/modifiers',{room:player.room},player,gm)).status,403);
});
test('individual revocation invalidates live sessions immediately and survives restart',async()=>{
  const h=harness(), a=await h.create(), b=await h.join(a.room), ga=await h.authenticate(a), gb=await h.authenticate(b);
  h.restart();
  assert.equal((await h.call('gm/modifiers',{room:a.room},a,ga)).status,200);
  await h.call('admin/revoke',{id:ga.id},null,null,true);
  assert.equal((await h.call('gm/clear',{room:a.room},a,ga)).status,403);
  assert.equal((await h.call('poll',{room:a.room,after:0},a,ga)).body.canManage,false);
  assert.equal((await h.call('gm/clear',{room:a.room},b,gb)).status,200);
  assert.equal((await h.call('auth',{room:a.room,credential:ga.credential},a)).status,403);
  assert(!JSON.stringify(h.state()).includes(ga.credential));
  assert(!JSON.stringify(h.state()).includes(ga.token));
  assert(!JSON.stringify(h.state()).includes(a.token));
});
test('expired GM session loses privilege even with ongoing membership',async()=>{
  const h=harness(), a=await h.create(), gm=await h.authenticate(a);
  for (let i=0;i<16;i++) { h.advance(60000); await h.call('poll',{room:a.room,after:0},a,gm); }
  assert.equal((await h.call('gm/clear',{room:a.room},a,gm)).status,403);
});
test('room boundaries, lease expiration, departure and empty-room cleanup',async()=>{
  const h=harness(), a=await h.create(), b=await h.join(a.room), c=await h.create(), gm=await h.authenticate(a);
  assert.equal((await h.call('gm/set',{room:c.room,participant:c.participant,value:6},c,gm)).status,403);
  await h.call('gm/set',{room:a.room,participant:b.participant,value:6},a,gm);
  await h.call('leave',{room:a.room},b);
  assert.equal(Object.keys(h.state().rooms[a.room].modifiers).length,0);
  const fresh=await h.join(a.room); assert.notEqual(fresh.participant,b.participant);
  h.advance(60000); await h.call('poll',{room:a.room,after:0},a);
  h.advance(31000); await h.call('poll',{room:a.room,after:0},a);
  assert(!h.state().rooms[a.room].people[fresh.participant]);
  await h.call('leave',{room:a.room},a);
  assert(!h.state().rooms[a.room]);
});
test('authentication throttling and operator-only audit routes',async()=>{
  const h=harness(), a=await h.create();
  for(let i=0;i<5;i++) assert.equal((await h.call('auth',{room:a.room,credential:'wrong'},a)).status,403);
  assert.equal((await h.call('auth',{room:a.room,credential:'wrong'},a)).status,429);
  assert.equal((await h.call('admin/audit',{},a)).status,403);
  assert.equal((await h.call('admin/audit',{},null,null,true)).status,200);
  assert.equal((await h.call('roll',{room:a.room,count:10000,sides:20,requestId:crypto.randomUUID()},a)).status,400);
});

test('each roll broadcasts its own immutable skin to every participant', async () => {
  const h=harness(), joe=await h.create(), john=await h.join(joe.room,'John');
  const base={room:joe.room,count:2,sides:20};
  const request={...base,skin:'crimson-velvet',requestId:crypto.randomUUID()};
  const a=await h.call('roll',request,joe);
  const b=await h.call('roll',{...base,skin:'obsidian-relic',requestId:crypto.randomUUID()},john);
  assert.equal(a.status,200); assert.equal(b.status,200);
  assert.equal(a.body.skin,'crimson-velvet'); assert.equal(b.body.skin,'obsidian-relic');
  for (const person of [joe,john]) {
    const poll=await h.call('poll',{room:joe.room,after:0},person);
    assert.deepEqual(poll.body.rolls.map(r=>r.skin),['crimson-velvet','obsidian-relic']);
  }
  assert.deepEqual((await h.call('roll',request,joe)).body,a.body);
  assert.equal((await h.call('roll',{...request,skin:'elderwood'},joe)).status,409);
  h.restart();
  assert.equal((await h.call('poll',{room:joe.room,after:0},john)).body.rolls[0].skin,'crimson-velvet');
  for (const skin of ['../../secret','https://example.com/texture.png','unknown',null,4,{}])
    assert.equal((await h.call('roll',{...base,skin,requestId:crypto.randomUUID()},joe)).status,400);
});
test('every bundled skin is accepted; old clients default to aether teal', async () => {
  for (const skin of [...SKINS,undefined]) {
    const h=harness(), p=await h.create();
    const result=await h.call('roll',{room:p.room,count:1,sides:20,skin,requestId:crypto.randomUUID()},p);
    assert.equal(result.status,200); assert.equal(result.body.skin,skin??'aether-teal');
  }
});
