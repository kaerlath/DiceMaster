# Protocol v1

All public and operator requests are POST JSON over HTTPS. Responses are `Cache-Control: no-store`. Bodies are limited to 4096 bytes and keys must exactly match the documented schema. Errors are generic; internal exception text and submitted values are never reflected.

| Route | Body | Authorization |
|---|---|---|
| `/v1/create` | `{name}` | None; creates ordinary participant |
| `/v1/join` | `{name,room}` | Room code |
| `/v1/poll` | `{room,after}` | Participant bearer; optional `X-GM-Session` |
| `/v1/roll` | `{room,count,sides,requestId,skin?}` | Participant bearer |
| `/v1/leave` | `{room}` | Participant bearer |
| `/v1/auth` | `{room,credential}` | Participant bearer |
| `/v1/gm/modifiers` | `{room}` | Participant bearer + GM session |
| `/v1/gm/set` | `{room,participant,value}` | Participant bearer + GM session |
| `/v1/gm/clear` | `{room}` | Participant bearer + GM session |
| `/v1/gm/logout` | `{room}` | Participant bearer + GM session |
| `/v1/admin/issue` | `{label}` | Operator bearer |
| `/v1/admin/revoke` | `{id}` | Operator bearer |
| `/v1/admin/audit` | `{}` | Operator bearer |

Join/create return `{room,participant,token,serverTime}`. No owner role exists. A participant bearer is bound to one server-generated participant ID in one room. Room codes are random 10-character invitations, not GM credentials.

Poll returns `{serverTime,sequence,participants,rolls,canManage,gmExpires}`. Each roster entry is exactly `{id,name}`; no other participant's capability is exposed. The capability fields describe only the requesting session. Each public roll is exactly:

```json
{
  "id": "uuid", "sequence": 1, "participant": "uuid", "name": "Player", "skin": "crimson-velvet",
  "count": 3, "sides": 8, "faces": [8,8,8], "total": 24,
  "animationSeed": 12345, "startsAt": 1789800000000, "durationMs": 2600
}
```

`startsAt` and `serverTime` are Unix milliseconds. `sequence` is room-local. Poll after the last received sequence, deduplicate by roll ID, and recover from history after interruptions; only the latest 100 events are retained. The client uses the latest available event if earlier events have aged out.

Roll retries with the same `requestId` return the same result while retained in that participant's last 64 requests. Changing dice under the same key yields 409. Beyond the bounded window the same ID can be reused, so clients must generate new UUIDs and must not retry arbitrarily old requests. The shipped client does not retry ambiguous roll submissions; polling discovers accepted events.

Modifiers are integers -2000..2000, applied in full to each logical die. Zero clears the assignment. The server rolls each natural die with a cryptographic uniform integer generator, adds the modifier to each natural face and clamps each face independently to `[1,sides]`, then sums the final faces (no total-level redistribution). No natural RNG state or seed is retained or sent. Independent cryptographic randomness supplies the public animation seed.

GM credentials are `UUID.256-bit-hex-secret`. Only SHA-256 of the random secret is stored; these are high-entropy generated credentials, not user passwords. Opaque GM session tokens and participant tokens are also hashed at rest. Auth success returns a token, expiry and capability privately. Session authorization is bound to the participant, room, expiry and unrevoked issuing credential on every request.

Authentication is throttled by participant and Cloudflare source IP; joins by source IP; rolls by participant and room queue capacity; general participant traffic is limited. Privileged changes never enter public history. Audit events are a server-only ring of 1000 entries including issuance, revocation, authentication, membership, assignment changes and roll IDs. They do not include raw credentials, session tokens or natural rolls.

