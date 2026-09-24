import { hash } from './dice.mjs';

export async function socketIdentity(state, request) {
  const url=new URL(request.url), room=url.searchParams.get('room');
  const raw=request.headers.get('Authorization')?.replace(/^Bearer /,'') ?? '';
  if (!/^[a-f0-9]{64}$/.test(raw)) return null;
  const digest=await hash(raw);
  const person=Object.values(state.rooms[room]?.people ?? {}).find(p=>p.digest===digest);
  if (!person) return null;
  const gm=request.headers.get('X-GM-Session') ?? '';
  return {room, id:person.id, gmDigest:/^[a-f0-9]{64}$/.test(gm) ? await hash(gm) : ''};
}
export function socketSnapshot(state, identity, now=Date.now()) {
  const room=state.rooms[identity.room];
  if (!room?.people[identity.id]) return null;
  const s=state.sessions[identity.gmDigest];
  const canManage=!!(s && s.expires>now && s.person===identity.id && s.room===identity.room && state.credentials[s.credential] && !state.credentials[s.credential].revoked);
  // Explicit public fields only. Modifiers are fetched separately via authenticated HTTPS.
  return {sequence:room.seq, participants:Object.values(room.people).map(p=>({id:p.id,name:p.name})).sort((a,b)=>a.id.localeCompare(b.id)), rolls:room.rolls,
    canManage, gmExpires:canManage?s.expires:0};
}

