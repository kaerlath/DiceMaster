import { Service, reply } from './service.mjs';

export default {
  async fetch(request, env) {
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
    for (const kind of ['credentials','sessions','rates']) for (const [id,value] of Object.entries(state[kind])) rows.set(`${kind}|${id}`,JSON.stringify(value));
    for (let i=0;i<state.audit.length;i+=100) rows.set(`audit|${i}`,JSON.stringify(state.audit.slice(i,i+100)));
    this.ctx.storage.transactionSync(() => {
      for (const [key,value] of rows) if (old.get(key) !== value) this.ctx.storage.sql.exec('INSERT OR REPLACE INTO state(key,value) VALUES (?,?)',key,value);
      for (const key of old.keys()) if (!rows.has(key)) this.ctx.storage.sql.exec('DELETE FROM state WHERE key = ?',key);
    });
  }
  fetch(request) {
    return this.ctx.blockConcurrencyWhile(async () => {
      const {state,rows} = this.load();
      const service = new Service(state, this.env.ADMIN_KEY);
      const response = await service.fetch(request);
      this.save(service.state,rows);
      if (!await this.ctx.storage.getAlarm()) await this.ctx.storage.setAlarm(Date.now() + 60000);
      return response;
    });
  }
  async alarm() {
    return this.ctx.blockConcurrencyWhile(async () => {
      const {state,rows} = this.load();
      const service = new Service(state, this.env.ADMIN_KEY);
      service.cleanup();
      this.save(service.state,rows);
      if (Object.keys(service.state.rooms).length) await this.ctx.storage.setAlarm(Date.now() + 60000);
    });
  }
}
