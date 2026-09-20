// Explicit integration check; requires installed Wrangler/Miniflare dependencies.
import { createRequire } from 'node:module';
import { realpathSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
const require = createRequire(realpathSync(fileURLToPath(new URL('../node_modules/wrangler/package.json',import.meta.url))));
const {Miniflare} = require('miniflare');
const mf = new Miniflare({
  modules:true,
  scriptPath:fileURLToPath(new URL('../src/worker.mjs',import.meta.url)),
  modulesRules:[{type:'ESModule',include:['**/*.mjs']}],
  compatibilityDate:'2025-08-13',
  durableObjects:{AUTHORITY:{className:'Authority',useSQLite:true}},
  bindings:{ADMIN_KEY:'a'.repeat(64)}
});
try {
  async function call(path,body,bearer,gm) {
    const headers={'Content-Type':'application/json','X-DiceMaster-Protocol':'2'};
    if(bearer)headers.Authorization=`Bearer ${bearer}`;
    if(gm)headers['X-GM-Session']=gm;
    const res=await mf.dispatchFetch(`https://relay.test/v1/${path}`,{method:'POST',headers,body:JSON.stringify(body)});
    return {status:res.status,body:await res.json()};
  }
  const p=(await call('create',{name:'Runtime player'})).body;
  assert(p.token);
  const observer=(await call('join',{name:'Observer',room:p.room})).body;
  const issued=(await call('admin/issue',{label:'Test GM'},'a'.repeat(64))).body;
  const gm=(await call('auth',{room:p.room,credential:issued.credential},p.token)).body;
  assert(gm.token);
  assert.equal((await call('gm/set',{room:p.room,participant:p.participant,value:2000},p.token,gm.token)).status,200);
  const request={room:p.room,count:3,sides:8,requestId:crypto.randomUUID()};
  const concurrent=await Promise.all([call('roll',request,p.token),call('roll',request,p.token)]);
  assert.deepEqual(concurrent[0],concurrent[1]);
  const roll=concurrent[0].body;
  assert.deepEqual(roll.faces,[8,8,8]);
  const poll=(await call('poll',{room:p.room,after:0},p.token)).body;
  assert.deepEqual(poll.rolls,[roll]);assert.equal(poll.canManage,false);
  const watched=(await call('poll',{room:p.room,after:0},observer.token)).body;
  assert.deepEqual(watched.rolls,poll.rolls);
  async function connect(user,auth) {
    const headers={Upgrade:'websocket','X-DiceMaster-Protocol':'2',Authorization:`Bearer ${user.token}`};
    if(auth)headers['X-GM-Session']=auth.token;
    const response=await mf.dispatchFetch(`https://relay.test/v1/connect?room=${user.room}`,{headers});
    assert.equal(response.status,101);
    const ws=response.webSocket, messages=[];
    ws.addEventListener('message',e=>messages.push(JSON.parse(e.data)));
    ws.accept();
    async function wait(predicate) {
      const end=Date.now()+3000;
      while(Date.now()<end) { const found=messages.find(predicate); if(found)return found; await new Promise(r=>setTimeout(r,10)); }
      throw new Error('Expected stream update did not arrive');
    }
    return {ws,messages,wait};
  }
  const denied=await mf.dispatchFetch(`https://relay.test/v1/connect?room=${p.room}`,{headers:{Upgrade:'websocket','X-DiceMaster-Protocol':'2'}});
  assert.equal(denied.status,401);
  const normal=await connect(observer), privileged=await connect(p,gm);
  assert.equal((await normal.wait(x=>x.sequence===1)).canManage,false);
  assert.equal((await privileged.wait(x=>x.canManage)).canManage,true);
  const extra=(await call('join',{name:'Late guest',room:p.room})).body;
  await normal.wait(x=>x.participants.length===3);
  const pushed=(await call('roll',{room:p.room,count:1,sides:20,skin:'rainbow-opal',requestId:crypto.randomUUID()},p.token)).body;
  const event=await normal.wait(x=>x.sequence===2);
  assert.equal(event.rolls.at(-1).id,pushed.id);
  assert(!JSON.stringify(normal.messages).includes('modifiers'));
  for(const secret of [p.token,observer.token,gm.token,issued.credential]) assert(!JSON.stringify(normal.messages).includes(secret));
  normal.ws.close();
  const resumed=await connect(observer);
  await resumed.wait(x=>x.sequence===2);
  await call('admin/revoke',{id:issued.id},'a'.repeat(64));
  assert.equal((await call('gm/clear',{room:p.room},p.token,gm.token)).status,403);
  await privileged.wait(x=>!x.canManage);
  resumed.ws.close(); privileged.ws.close();
  console.log('WebSocket join, roll push, reconnect, private payload and revocation passed.');
  console.log('Cloudflare local runtime: room, auth, private modifier, roll, poll and revocation passed.');
} finally {await mf.dispose();}


