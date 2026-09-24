import { Service, reply } from './service.mjs';
import { socketIdentity, socketSnapshot } from './sockets.mjs';

export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    if (!path.startsWith('/v1/admin/') && request.headers.get('X-DiceMaster-Protocol') !== '2')
      return new Response(JSON.stringify({error:'Please update DiceMaster to the latest version.',minimumVersion:'0.5.1'}),{status:426,headers:{'Content-Type':'application/json','Cache-Control':'no-store'}});
    // Operator kill switch: block before touching Durable Object storage.
    if (env.RELAY_PAUSED === 'true') return new Response(JSON.stringify({error:'Relay paused'}),{status:503,headers:{'Content-Type':'application/json','Cache-Control':'no-store','Retry-After':'300'}});
    const url = new URL(request.url);
    if (url.protocol !== 'https:' && !['localhost', '127.0.0.1'].includes(url.hostname)) return reply(400, {error:'HTTPS required'});
    return env.AUTHORITY.get(env.AUTHORITY.idFromName('v1')).fetch(request);
  }
};

// A single authority deliberately serializes auth + revocation + room writes in v1.
// No authorization cache or eventually-consistent KV sits on the permission path.
export class Authority {
  constructor(ctx, env) {
    this.ctx = ctx; this.env = env;
    this.cached = null;
    this.sent = new WeakMap();
    ctx.storage.sql.exec('CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value TEXT NOT NULL)');
  }
  load() {
    const state = {rooms:{}, credentials:{}, sessions:{}, audit:[], rates:{}};
    const rows = new Map();
    for (const row of this.ctx.storage.sql.exec('SELECT key, value FROM state')) rows.set(row.key, row.value);
    for (const [key,value] of rows) {
      const [kind,id] = key.split('|');
      if (kind === 'room') state.rooms[id] = {...JSON.parse(value),people:{}};
      if (kind === 'credentials' || kind === 'sessions' || kind === 'rates') state[kind][id] = JSON.parse(value);
      if (kind === 'audit') state.audit.push(...JSON.parse(value));
    }
    for (const [key,value] of rows) if (key.startsWith('person|')) {
      const [,room,id] = key.split('|'); if (state.rooms[room]) state.rooms[room].people[id] = JSON.parse(value);
    }
    state.audit.sort((a,b) => a.at-b.at);
    return {state,rows};
  }
  save(state, old) {
    const rows = new Map();
    for (const [code,room] of Object.entries(state.rooms)) {
      const {people,...rest} = room; rows.set(`room|${code}`,JSON.stringify(rest));
      for (const [id,person] of Object.entries(people)) rows.set(`person|${code}|${id}`,JSON.stringify(person));
    }
    for (const kind of ['credentials','sessions','rates']) for (const [id,value] of Object.entries(state[kind])) {
      // General per-participant traffic counters are instance-local. Credential
      // throttles, revocation, sessions and all game state remain durable.
      if (kind === 'rates' && id.startsWith('person:')) continue;
      rows.set(`${kind}|${id}`,JSON.stringify(value));
    }
    for (let i=0;i<state.audit.length;i+=100) rows.set(`audit|${i}`,JSON.stringify(state.audit.slice(i,i+100)));
    this.ctx.storage.transactionSync(() => {
      for (const [key,value] of rows) if (old.get(key) !== value) this.ctx.storage.sql.exec('INSERT OR REPLACE INTO state(key,value) VALUES (?,?)',key,value);
      for (const key of old.keys()) if (!rows.has(key)) this.ctx.storage.sql.exec('DELETE FROM state WHERE key = ?',key);
    });
    return rows;
  }
  fetch(request) {
    return this.ctx.blockConcurrencyWhile(async () => {
      const {state,rows} = this.cached ?? this.load();
      const service = new Service(state, this.env.ADMIN_KEY, Date.now, this.connected());
      try {
        if (new URL(request.url).pathname === '/v1/connect') {
          if (request.method !== 'GET' || request.headers.get('Upgrade')?.toLowerCase() !== 'websocket') return reply(400,{error:'WebSocket required'});
          service.cleanup();
          const identity = await socketIdentity(state,request);
          if (!identity) return reply(401,{error:'Session expired'});
          service.rate(`socket:${identity.id}`,20,60000);
          state.rooms[identity.room].people[identity.id].seen=Date.now();
          service.audit('socket-connected',{room:identity.room});
          const saved=this.save(state,rows);
          const [client,server]=Object.values(new WebSocketPair());
          for (const ws of this.sockets()) if (ws.deserializeAttachment()?.id===identity.id) ws.close(4000,'Connection replaced');
          this.ctx.acceptWebSocket(server);
          server.serializeAttachment(identity);
          this.cached={state,rows:saved};
          this.broadcast(state);
          return new Response(null,{status:101,webSocket:client});
        }
        const response = await service.fetch(request);
        const saved = this.save(service.state,rows);
        if (!await this.ctx.storage.getAlarm()) await this.ctx.storage.setAlarm(Date.now() + 60000);
        this.cached = {state:service.state,rows:saved};
        this.broadcast(service.state);
        return response;
      } catch (error) { this.cached = null; throw error; }
    });
  }
  async alarm() {
    return this.ctx.blockConcurrencyWhile(async () => {
      const {state,rows} = this.cached ?? this.load();
      const service = new Service(state, this.env.ADMIN_KEY, Date.now, this.connected());
      try {
        service.checkpointPresence();
        service.cleanup();
        const saved = this.save(service.state,rows);
        if (Object.keys(service.state.rooms).length) await this.ctx.storage.setAlarm(Date.now() + 60000);
        this.cached = {state:service.state,rows:saved};
        this.broadcast(service.state);
      } catch (error) { this.cached = null; throw error; }
    });
  }
  sockets() { return this.ctx.getWebSockets?.() ?? []; }
  connected() { return new Set(this.sockets().filter(ws=>ws.readyState===1).map(ws=>ws.deserializeAttachment()?.id)); }
  broadcast(state) {
    for (const ws of this.sockets()) {
      try {
        const snapshot=socketSnapshot(state,ws.deserializeAttachment());
        if (!snapshot) { ws.close(4001,'Session ended'); continue; }
        const key=JSON.stringify(snapshot);
        if (this.sent.get(ws)===key) continue;
        ws.send(JSON.stringify({...snapshot,serverTime:Date.now()}));
        this.sent.set(ws,key);
      } catch { try { ws.close(1011,'Reconnect'); } catch {} }
    }
  }
  webSocketMessage(ws) { ws.close(1008,'Use HTTPS for commands'); }
  webSocketClose(ws,code) { return this.disconnected(ws,code,'close'); }
  webSocketError(ws) { try { ws.close(1011,'Reconnect'); } catch {} return this.disconnected(ws,1011,'error'); }
  disconnected(ws,code,kind) {
    return this.ctx.blockConcurrencyWhile(async()=>{
      const identity=ws.deserializeAttachment();
      const {state,rows}=this.cached ?? this.load();
      const person=state.rooms[identity?.room]?.people[identity?.id];
      if (person) person.seen=Date.now(); // bounded grace for reconnecting the same participant
      if (person) new Service(state,this.env.ADMIN_KEY).audit('socket-disconnected',{room:identity.room,code:Number.isInteger(code)?code:0,kind});
      try { this.cached={state,rows:this.save(state,rows)}; }
      catch(error) { this.cached=null; throw error; }
    });
  }
}

