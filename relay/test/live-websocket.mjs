// Live deployment check: temporary code-only room, no operator or GM credentials.
import {createRequire} from 'node:module';
import {realpathSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const require=createRequire(realpathSync(fileURLToPath(new URL('../node_modules/wrangler/package.json',import.meta.url))));
const WebSocket=require('ws');
const origin='https://dicemaster-relay.kaerlath.workers.dev';
async function call(path,body,person) {
  const headers={'Content-Type':'application/json','X-DiceMaster-Protocol':'2'};
  if(person)headers.Authorization=`Bearer ${person.token}`;
  const r=await fetch(`${origin}/v1/${path}`,{method:'POST',headers,body:JSON.stringify(body),signal:AbortSignal.timeout(10000)});
  if(!r.ok)throw new Error(`Relay check failed: HTTP ${r.status}`);
  return r.json();
}
const people=[],sockets=[];
async function connect(p) {
  const ws=new WebSocket(`${origin.replace('https:','wss:')}/v1/connect?room=${p.room}`,{headers:{'X-DiceMaster-Protocol':'2',Authorization:`Bearer ${p.token}`},handshakeTimeout:10000});
  sockets.push(ws);
  const messages=[]; let error;
  ws.on('message',data=>{try{messages.push(JSON.parse(data.toString()));}catch(e){error=e;}});
  ws.on('error',e=>{error=e;});
  return async predicate=>{
    const end=Date.now()+12000;
    while(Date.now()<end) { if(error)throw error; const result=messages.find(predicate);if(result)return result;await new Promise(r=>setTimeout(r,25)); }
    throw new Error('Timed out waiting for a pushed room update');
  };
}
try {
  const p=await call('create',{name:'WebSocket check'});people.push(p);
  const wait=await connect(p);
  const initial=await wait(x=>x.sequence===0);
  assert.equal(initial.canManage,false);
  const guest=await call('join',{room:p.room,name:'Observer check'});people.push(guest);
  await wait(x=>x.participants.length===2);
  const observer=await connect(guest);
  const roll=await call('roll',{room:p.room,count:3,sides:8,skin:'rainbow-opal',requestId:crypto.randomUUID()},p);
  for(const receive of [wait,observer]) {
    const snapshot=await receive(x=>x.sequence===1);
    assert.equal(snapshot.rolls[0].id,roll.id);
    assert.equal(snapshot.rolls[0].skin,'rainbow-opal');
    assert(!JSON.stringify(snapshot).includes('modifiers'));
  }
  sockets[1].terminate();
  const resumed=await connect(guest);
  assert.equal((await resumed(x=>x.sequence===1)).participants.length,2);
  console.log('Live WebSocket check passed: join, two-viewer roll push, private payload and reconnect.');
} finally {
  for(const ws of sockets)ws.terminate();
  for(const person of people)await call('leave',{room:person.room},person).catch(()=>{});
}

