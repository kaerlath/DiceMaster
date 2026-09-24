import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { Authority } from '../src/worker.mjs';
import { token } from '../src/dice.mjs';

test('Worker storage persists rooms, permissions and audit atomically across instances',async()=>{
  const db=new DatabaseSync(':memory:');
  let alarm=null;
  const ctx={
    blockConcurrencyWhile:fn=>fn(),
    storage:{
      sql:{exec:(sql,...args)=>db.prepare(sql).all(...args)},
      transactionSync:fn=>{db.exec('BEGIN');try{fn();db.exec('COMMIT');}catch(e){db.exec('ROLLBACK');throw e;}},
      getAlarm:async()=>alarm,
      setAlarm:async n=>{alarm=n;}
    }
  };
  const env={ADMIN_KEY:token()};
  let worker=new Authority(ctx,env);
  async function call(path,body,bearer,gm) {
    const headers={'Content-Type':'application/json'};
    if(bearer)headers.Authorization=`Bearer ${bearer}`;
    if(gm)headers['X-GM-Session']=gm;
    const res=await worker.fetch(new Request(`https://relay.test/v1/${path}`,{method:'POST',headers,body:JSON.stringify(body)}));
    return {status:res.status,body:await res.json()};
  }
  const person=(await call('create',{name:'Player'})).body;
  const issued=(await call('admin/issue',{label:'Keeper'},env.ADMIN_KEY)).body;
  const gm=(await call('auth',{room:person.room,credential:issued.credential},person.token)).body;
  assert.equal((await call('gm/set',{room:person.room,participant:person.participant,value:2000},person.token,gm.token)).status,200);
  worker=new Authority(ctx,env);
  const roll=await call('roll',{room:person.room,count:3,sides:8,requestId:crypto.randomUUID()},person.token);
  assert.equal(roll.body.total,24);
  assert(alarm>0);
  await call('admin/revoke',{id:issued.id},env.ADMIN_KEY);
  worker=new Authority(ctx,env);
  assert.equal((await call('gm/clear',{room:person.room},person.token,gm.token)).status,403);
  const rows=db.prepare('SELECT value FROM state').all().map(r=>r.value).join('');
  for(const secret of [person.token,gm.token,issued.credential])assert(!rows.includes(secret));
  await call('leave',{room:person.room},person.token);
  assert.equal(db.prepare("SELECT * FROM state WHERE key LIKE 'person|%' OR key LIKE 'room|%'").all().length,0);
  db.close();
});

test('idle polling avoids repeated disk reads and writes while leases remain durable', async t => {
  let now=1000000, writes=0, reads=0, alarm=null;
  t.mock.method(Date,'now',()=>now);
  const db=new DatabaseSync(':memory:');
  const ctx={blockConcurrencyWhile:fn=>fn(),storage:{
    sql:{exec:(sql,...args)=>{if(sql.startsWith('SELECT'))reads++; if(sql.startsWith('INSERT')||sql.startsWith('DELETE'))writes++; return db.prepare(sql).all(...args);}},
    transactionSync:fn=>{db.exec('BEGIN');try{fn();db.exec('COMMIT');}catch(e){db.exec('ROLLBACK');throw e;}},
    getAlarm:async()=>alarm,setAlarm:async n=>{alarm=n;}
  }};
  const env={ADMIN_KEY:token()}; let worker=new Authority(ctx,env);
  async function call(path,body,user) {
    const headers={'Content-Type':'application/json'};
    if(user)headers.Authorization=`Bearer ${user.token}`;
    const r=await worker.fetch(new Request(`https://relay.test/v1/${path}`,{method:'POST',headers,body:JSON.stringify(body)}));
    assert.equal(r.status,200); return r.json();
  }
  const people=[await call('create',{name:'First',listed:true,title:'Public'})];
  for(let i=0;i<3;i++)people.push(await call('join',{name:'Guest',room:people[0].room}));
  const baseline=writes; reads=0;
  for(let tick=0;tick<240;tick++) {
    now+=250;
    for(const p of people)await call('poll',{room:p.room,after:0},p);
  }
  assert(writes-baseline<=24,`960 polls caused ${writes-baseline} row mutations`);
  assert.equal(reads,0,'live instance should not reload the database for every poll');
  worker=new Authority(ctx,env);
  assert.equal((await call('rooms',{})).rooms[0].participants,4);
  now+=600001; await worker.alarm();
  assert.deepEqual((await call('rooms',{})).rooms,[]);
  db.close();
});


test('alarm checkpoints idle sockets to SQLite before a process restart without close callbacks',async t=>{
  let now=1000000,alarm=null; t.mock.method(Date,'now',()=>now);
  const db=new DatabaseSync(':memory:'); let sockets=[];
  const ctx={blockConcurrencyWhile:fn=>fn(),getWebSockets:()=>sockets,storage:{
    sql:{exec:(sql,...args)=>db.prepare(sql).all(...args)},
    transactionSync:fn=>{db.exec('BEGIN');try{fn();db.exec('COMMIT');}catch(e){db.exec('ROLLBACK');throw e;}},
    getAlarm:async()=>alarm,setAlarm:async n=>{alarm=n;}
  }};
  const env={ADMIN_KEY:token()};let worker=new Authority(ctx,env);
  const created=await worker.fetch(new Request('https://relay.test/v1/create',{method:'POST',body:JSON.stringify({name:'Player'})}));
  const p=await created.json();
  sockets=[{readyState:1,deserializeAttachment:()=>({room:p.room,id:p.participant,gmDigest:''}),send(){},close(){}}];
  now+=2*3600000;await worker.alarm();
  const roomRow=JSON.parse(db.prepare('SELECT value FROM state WHERE key=?').get(`room|${p.room}`).value);
  assert.equal(roomRow.socketPresence.at,now);
  sockets=[];worker=new Authority(ctx,env);now+=5*60000;
  const poll=await worker.fetch(new Request('https://relay.test/v1/poll',{method:'POST',headers:{Authorization:`Bearer ${p.token}`},body:JSON.stringify({room:p.room,after:0})}));
  assert.equal(poll.status,200);
  now+=600001;await worker.alarm();
  assert.equal(db.prepare('SELECT value FROM state WHERE key=?').get(`room|${p.room}`),undefined);
  db.close();
});
