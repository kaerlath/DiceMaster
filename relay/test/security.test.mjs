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

test('public directory is opt-in, sanitized, persistent, and expires with rooms', async () => {
  const h = harness();
  const legacy = await h.create();
  const privateRoom = (await h.call('create',{name:'Private',listed:false,title:'Hidden'})).body;
  const owner = (await h.call('create',{name:'Owner',listed:true,title:'  Rainbow table  '})).body;
  const gm = await h.authenticate(owner);
  await h.call('gm/set',{room:owner.room,participant:owner.participant,value:4},owner,gm);
  const guest = await h.join(owner.room);
  let listing = await h.call('rooms',{});
  assert.equal(listing.status,200);
  assert.deepEqual(listing.body,{rooms:[{code:owner.room,title:'Rainbow table',participants:2,capacity:32}]});
  h.restart();
  assert.deepEqual((await h.call('rooms',{})).body,listing.body);
  assert.equal((await h.call('gm/modifiers',{room:owner.room},guest)).status,403);
  await h.call('leave',{room:owner.room},guest);
  assert.equal((await h.call('rooms',{})).body.rooms[0].participants,1);
  h.advance(90001);
  assert.deepEqual((await h.call('rooms',{})).body,{rooms:[]});
});

test('public directory validates input and limits anonymous browsing', async () => {
  const h = harness();
  for (const body of [null, {name:'x',listed:true}, {name:'x',listed:'true',title:'t'}, {name:'x',listed:true,title:''}, {name:'x',listed:true,title:'x'.repeat(65)}, {name:'x',listed:true,title:'line\nbreak'}, {name:'x',listed:true,title:'t',canManage:true}])
    assert.equal((await h.call('create',body)).status,400);
  assert.equal((await h.call('rooms',{includeModifiers:true})).status,400);
  for (let i=0;i<120;i++) assert.equal((await h.call('rooms',{})).status,200);
  assert.equal((await h.call('rooms',{})).status,429);
  h.advance(60001);
  assert.equal((await h.call('rooms',{})).status,200);
});

 test('socket identity is server-authenticated and capability is checked on every snapshot',async()=>{
  const {socketIdentity,socketSnapshot}=await import('../src/sockets.mjs');
  const h=harness(), p=await h.create(), other=await h.create(), gm=await h.authenticate(p);
  // Harness clock is injected; align expiry for the handshake's real clock.
  h.state().rooms[p.room].expires=Date.now()+60000;
  const req=(room,token)=>new Request(`https://relay.test/v1/connect?room=${room}`,{headers:{Authorization:`Bearer ${token}`,'X-GM-Session':gm.token}});
  assert.equal(await socketIdentity(h.state(),req(p.room,'x')),null);
  assert.equal(await socketIdentity(h.state(),req(other.room,p.token)),null);
  const identity=await socketIdentity(h.state(),req(p.room,p.token));
  assert(identity && !JSON.stringify(identity).includes(gm.token));
  assert.equal(socketSnapshot(h.state(),identity,1000000).canManage,true);
  const ordinary={...identity,gmDigest:''};
  assert.equal(socketSnapshot(h.state(),ordinary,1000000).canManage,false);
  assert(!JSON.stringify(socketSnapshot(h.state(),ordinary,1000000)).includes('modifiers'));
  await h.call('admin/revoke',{id:gm.id},null,null,true);
  assert.equal(socketSnapshot(h.state(),identity,1000000).canManage,false);
});

test('connected sockets retain membership without heartbeat writes, but room expiry still applies',async()=>{
  const h=harness(), p=await h.create();
  h.advance(120000);
  const service=new Service(h.state(),token(),()=>1120000,new Set([p.participant]));
  service.cleanup();
  assert(service.state.rooms[p.room].people[p.participant]);
  assert.equal(service.state.rooms[p.room].people[p.participant].seen,1000000);
  service.connected.clear(); service.cleanup();
  assert.equal(service.state.rooms[p.room],undefined);
});

test('socket broadcasts are room-scoped, deduplicated, and revoke capability immediately',async()=>{
  const {Authority}=await import('../src/worker.mjs');
  const h=harness(), p=await h.create(), other=await h.create();
  const gm=await h.authenticate(p);
  const {hash}=await import('../src/dice.mjs');
  for(const room of Object.values(h.state().rooms))room.expires=Date.now()+60000;
  for(const session of Object.values(h.state().sessions))session.expires=Date.now()+60000;
  const make=identity=>({deserializeAttachment:()=>identity,messages:[],send(s){this.messages.push(JSON.parse(s));},close(){this.closed=true;}});
  const a=make({room:p.room,id:p.participant,gmDigest:await hash(gm.token)});
  const b=make({room:other.room,id:other.participant,gmDigest:''});
  const authority=new Authority({storage:{sql:{exec(){}}},getWebSockets:()=>[a,b]},{});
  authority.broadcast(h.state()); authority.broadcast(h.state());
  assert.equal(a.messages.length,1); assert.equal(b.messages.length,1);
  assert.equal(a.messages[0].canManage,true);
  assert.equal(b.messages[0].participants[0].id,other.participant);
  await h.call('admin/revoke',{id:gm.id},null,null,true);
  authority.broadcast(h.state());
  assert.equal(a.messages.length,2); assert.equal(a.messages[1].canManage,false);
  assert.equal(b.messages.length,1);
  delete h.state().rooms[p.room]; authority.broadcast(h.state()); assert(a.closed);
});

test('operator pause rejects traffic before accessing the shared Durable Object',async()=>{
  const worker=(await import('../src/worker.mjs')).default;
  const response=await worker.fetch(new Request('https://relay.test/v1/poll',{method:'POST',headers:{'X-DiceMaster-Protocol':'2'}}),{RELAY_PAUSED:'true',AUTHORITY:{idFromName(){throw new Error('Storage must not be accessed');}}});
  assert.equal(response.status,503);assert.equal(response.headers.get('Retry-After'),'300');
});


test('legacy clients are rejected before Durable Object storage or websocket upgrade',async()=>{
  const worker=(await import('../src/worker.mjs')).default;
  for(const path of ['rooms','create','join','poll','connect','roll','auth','gm/set']) {
    for(const version of ['', '1','3']) {
      const response=await worker.fetch(new Request(`https://relay.test/v1/${path}`,{headers:{'X-DiceMaster-Protocol':version}}),{AUTHORITY:{idFromName(){throw new Error('Old clients must not reach storage');}}});
      assert.equal(response.status,426);
    }
  }
});
