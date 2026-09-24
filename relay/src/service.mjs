import { hash, token, randomInt, resolve, SIDES } from './dice.mjs';

export const reply = (status, body) => new Response(JSON.stringify(body), {status, headers:{'Content-Type':'application/json','Cache-Control':'no-store'}});
class Fault extends Error { constructor(status) { super(); this.status = status; } }
const need = (ok, status = 400) => { if (!ok) throw new Fault(status); };
const exact = (body, keys) => need(body && typeof body === 'object' && !Array.isArray(body) && Object.keys(body).every(k => keys.includes(k)) && keys.every(k => Object.hasOwn(body, k)));
const validToken = s => typeof s === 'string' && /^[a-f0-9]{64}$/.test(s);
const SESSION = 15 * 60000, LEASE = 10 * 60000;
export const SKINS = ['aether-teal','royal-amethyst','obsidian-gold','ivory-brass','ember-copper','moonstone-marble','elderwood','astral-glass','obsidian-relic','frostbound','crimson-velvet','emerald-marble','rose-quartz','sapphire-marble','amethyst-ice','jade-frost','honey-amber','rainbow-opal','prismatic-night'];

export class Service {
  constructor(state, adminKey, now = () => Date.now(), connected = new Set()) {
    this.connected = connected;
    this.state = state ?? {rooms:{}, credentials:{}, sessions:{}, audit:[], rates:{}};
    this.adminKey = adminKey;
    this.now = now;
  }
  audit(action, fields = {}) {
    this.state.audit.push({at:this.now(), action, ...fields});
    this.state.audit = this.state.audit.slice(-1000);
  }
  cleanup() {
    const now = this.now();
    for (const [code, room] of Object.entries(this.state.rooms)) {
      let removed = 0;
      for (const [id, p] of Object.entries(room.people)) {
        const checkpoint = room.socketPresence?.ids.includes(id) ? room.socketPresence.at : 0;
        if (Math.max(p.seen,checkpoint) + LEASE < now && !this.connected.has(id)) {
          delete room.people[id]; delete room.modifiers[id]; removed++;
        }
      }
      if (removed) this.audit('memberships-expired', {room:code, count:removed});
      if (!Object.keys(room.people).length) {
        delete this.state.rooms[code]; this.audit('room-expired', {room:code, reason:'no-participants'});
      }
    }
    for (const [key, s] of Object.entries(this.state.sessions)) if (s.expires <= now || !this.state.rooms[s.room]?.people[s.person]) delete this.state.sessions[key];
    for (const [key, r] of Object.entries(this.state.rates)) if (r.until <= now) delete this.state.rates[key];
  }
  checkpointPresence() {
    // One room-row checkpoint per existing minute alarm, not per client poll.
    for (const room of Object.values(this.state.rooms)) {
      const ids=Object.keys(room.people).filter(id=>this.connected.has(id)).sort();
      if (ids.length) room.socketPresence={at:this.now(),ids};
    }
  }
  rate(key, limit, ms) {
    const r = this.state.rates[key] ??= {count:0, until:this.now() + ms};
    need(++r.count <= limit, 429);
  }
  async fetch(req) {
    try {
      this.cleanup();
      const url = new URL(req.url), path = url.pathname;
      need(req.method === 'POST', 405);
      const reader = req.body?.getReader();
      let size = 0, chunks = [];
      if (reader) for (;;) {
        const {done, value} = await reader.read(); if (done) break;
        size += value.length; if (size > 4096) { await reader.cancel(); throw new Fault(413); }
        chunks.push(value);
      }
      const bytes = new Uint8Array(size); let offset = 0;
      for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
      let body; try { body = JSON.parse(new TextDecoder().decode(bytes)); } catch { throw new Fault(400); }
      const bearer = req.headers.get('Authorization')?.replace(/^Bearer /, '') ?? '';
      if (path.startsWith('/v1/admin/')) {
        need(this.adminKey && this.adminKey.length >= 32 && validToken(bearer) && await hash(bearer) === await hash(this.adminKey), 403);
        if (path === '/v1/admin/issue') {
          exact(body, ['label']); need(typeof body.label === 'string' && body.label.length > 0 && body.label.length <= 80);
          need(Object.keys(this.state.credentials).length < 100, 409);
          const secret = token(), id = crypto.randomUUID();
          this.state.credentials[id] = {label:body.label, digest:await hash(secret), revoked:false};
          this.audit('credential-issued', {credential:id});
          return reply(200, {id, credential:`${id}.${secret}`});
        }
        if (path === '/v1/admin/revoke') {
          exact(body, ['id']); need(Object.hasOwn(this.state.credentials, body.id), 404);
          this.state.credentials[body.id].revoked = true;
          for (const [key,s] of Object.entries(this.state.sessions)) if (s.credential === body.id) delete this.state.sessions[key];
          this.audit('credential-revoked', {credential:body.id}); return reply(200, {ok:true});
        }
        if (path === '/v1/admin/audit') { exact(body, []); return reply(200, {events:this.state.audit}); }
        throw new Fault(404);
      }
      if (path === '/v1/rooms') {
        exact(body, []);
        this.rate(`browse:${await hash(req.headers.get('CF-Connecting-IP') ?? 'local')}`, 120, 60000);
        const rooms = Object.entries(this.state.rooms).filter(([,r]) => r.listed === true)
          .map(([code,r]) => ({code, title:r.title, participants:Object.keys(r.people).length, capacity:32}))
          .sort((a,b) => a.title.localeCompare(b.title) || a.code.localeCompare(b.code));
        return reply(200, {rooms});
      }
      if (path === '/v1/create' || path === '/v1/join') {
        const options = path.endsWith('create') && body != null && (Object.hasOwn(body,'listed') || Object.hasOwn(body,'title'));
        exact(body, path.endsWith('create') ? (options ? ['name','listed','title'] : ['name']) : ['name','room']);
        if (options) {
          need(typeof body.listed === 'boolean');
          need(typeof body.title === 'string' && body.title.trim().length > 0 && body.title.length <= 64 && !/[\x00-\x1f\x7f]/.test(body.title));
        }
        need(typeof body.name === 'string' && body.name.trim().length > 0 && body.name.length <= 48 && !/[\x00-\x1f\x7f]/.test(body.name));
        // IP comes from Cloudflare, never an arbitrary forwarding header.
        this.rate(`join:${await hash(req.headers.get('CF-Connecting-IP') ?? 'local')}`, 20, 60000);
        let code = body.room, room;
        if (path.endsWith('create')) {
          need(Object.keys(this.state.rooms).length < 50, 503);
          do { code = Array.from({length:10}, () => 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'[randomInt(31)]).join(''); } while (Object.hasOwn(this.state.rooms, code));
          room = this.state.rooms[code] = {listed:options ? body.listed : false, title:options ? body.title.trim() : 'Dice table', people:{}, modifiers:{}, rolls:[], seq:0, next:0, createdAt:this.now()};
        } else { need(typeof code === 'string' && /^[A-Z2-9]{10}$/.test(code)); room = this.state.rooms[code]; need(room, 404); }
        need(Object.keys(room.people).length < 32, 409);
        const id = crypto.randomUUID(), secret = token();
        room.people[id] = {id, name:body.name.trim(), digest:await hash(secret), seen:this.now(), requests:{}};
        this.audit('joined', {room:code, person:id});
        return reply(200, {room:code, participant:id, token:secret, serverTime:this.now()});
      }
      exactRoom(body);
      const room = this.state.rooms[body.room]; need(room, 401);
      need(validToken(bearer), 401);
      const digest = await hash(bearer);
      const person = Object.values(room.people).find(p => p.digest === digest); need(person, 401);
      // A 90-second lease does not need a disk write on every poll.
      if (this.now() - person.seen >= 15000) person.seen = this.now();
      this.rate(`person:${person.id}`, 360, 60000);
      const gmToken = req.headers.get('X-GM-Session') ?? '';
      const session = validToken(gmToken) ? this.state.sessions[await hash(gmToken)] : null;
      const gm = session && session.expires > this.now() && session.person === person.id && session.room === body.room && this.state.credentials[session.credential] && !this.state.credentials[session.credential].revoked;
      if (path === '/v1/auth') {
        exact(body, ['room','credential']);
        this.rate(`auth:${person.id}`, 5, 60000);
        this.rate(`auth-ip:${await hash(req.headers.get('CF-Connecting-IP') ?? 'local')}`, 20, 60000);
        need(typeof body.credential === 'string' && body.credential.length <= 110);
        const [id, secret, extra] = body.credential.split('.');
        const cred = Object.hasOwn(this.state.credentials, id) ? this.state.credentials[id] : null;
        if (!cred || cred.revoked || extra || !validToken(secret) || await hash(secret) !== cred.digest) { this.audit('auth-denied', {person:person.id}); throw new Fault(403); }
        for (const [key,s] of Object.entries(this.state.sessions)) if (s.person === person.id) delete this.state.sessions[key];
        const secretSession = token(), expires = this.now() + SESSION;
        this.state.sessions[await hash(secretSession)] = {credential:id, person:person.id, room:body.room, expires};
        this.audit('auth-success', {credential:id, person:person.id});
        return reply(200, {token:secretSession, expires, canManage:true});
      }
      if (path === '/v1/poll') {
        exact(body, ['room','after']); need(Number.isInteger(body.after) && body.after >= 0 && body.after <= room.seq);
        return reply(200, {serverTime:this.now(), sequence:room.seq, participants:Object.values(room.people).map(p => ({id:p.id,name:p.name})), rolls:room.rolls.filter(r => r.sequence > body.after), canManage:!!gm, gmExpires:gm ? session.expires : 0});
      }
      if (path === '/v1/leave') {
        exact(body, ['room']); delete room.people[person.id]; delete room.modifiers[person.id]; this.cleanup(); return reply(200, {ok:true});
      }
      if (path.startsWith('/v1/gm/')) {
        need(gm, 403);
        if (path === '/v1/gm/modifiers') {
          exact(body, ['room']); return reply(200, {modifiers:room.modifiers});
        }
        if (path === '/v1/gm/set') {
          exact(body, ['room','participant','value']);
          need(typeof body.participant === 'string' && Object.hasOwn(room.people, body.participant), 404);
          need(Number.isInteger(body.value) && Math.abs(body.value) <= 2000);
          if (body.value === 0) delete room.modifiers[body.participant]; else room.modifiers[body.participant] = body.value;
          this.audit('modifier-set', {credential:session.credential, room:body.room, person:body.participant, value:body.value}); return reply(200, {ok:true});
        }
        if (path === '/v1/gm/clear') {
          exact(body, ['room']); room.modifiers = {}; this.audit('modifiers-cleared', {credential:session.credential, room:body.room}); return reply(200, {ok:true});
        }
        if (path === '/v1/gm/logout') {
          exact(body, ['room']); delete this.state.sessions[await hash(gmToken)]; return reply(200, {ok:true});
        }
        throw new Fault(404);
      }
      if (path === '/v1/roll') {
        exact(body, Object.hasOwn(body,'skin') ? ['room','count','sides','requestId','skin'] : ['room','count','sides','requestId']);
        const skin = body.skin === undefined ? 'aether-teal' : body.skin;
        need(SKINS.includes(skin));
        need(Number.isInteger(body.count) && body.count >= 1 && body.count <= 20 && SIDES.includes(body.sides));
        need(typeof body.requestId === 'string' && /^[a-zA-Z0-9-]{16,64}$/.test(body.requestId));
        if (Object.hasOwn(person.requests, body.requestId)) {
          const old = person.requests[body.requestId]; need(old.count === body.count && old.sides === body.sides && (old.skin ?? 'aether-teal') === skin, 409); return reply(200, old);
        }
        this.rate(`roll:${person.id}`, 20, 60000);
        need(room.next < this.now() + 15000, 429);
        const result = resolve(body.count, body.sides, room.modifiers[person.id] ?? 0);
        const startsAt = Math.max(this.now() + 1800, room.next);
        // Explicit public DTO: never spread server state or natural rolls here.
        const event = {id:crypto.randomUUID(), sequence:++room.seq, participant:person.id, name:person.name, skin, count:body.count, sides:body.sides, faces:result.faces, total:result.total, animationSeed:randomInt(0x100000000), startsAt, durationMs:2400+randomInt(2601)};
        room.next = startsAt + event.durationMs + 500;
        room.rolls.push(event); room.rolls = room.rolls.slice(-100);
        person.requests[body.requestId] = event;
        const keys = Object.keys(person.requests); if (keys.length > 64) delete person.requests[keys[0]];
        this.audit('roll', {room:body.room, person:person.id, roll:event.id});
        return reply(200, event);
      }
      throw new Fault(404);
    } catch (error) {
      // Never serialize exceptions, submitted secrets, or internal state.
      return reply(error instanceof Fault ? error.status : 500, {error:error instanceof Fault ? 'Request rejected' : 'Service unavailable'});
    }
  }
}
function exactRoom(body) { need(body && typeof body === 'object' && typeof body.room === 'string' && /^[A-Z2-9]{10}$/.test(body.room)); }

