// Operator-only CLI. Secrets are never embedded in command-line arguments.
// DM_ADMIN_KEY is read from the operator process environment; output is sensitive.
const [action, value] = process.argv.slice(2);
if (!['issue','revoke','audit'].includes(action)) throw Error('Usage: node tools/admin.mjs issue "GM name" | revoke <id> | audit');
const origin = new URL(process.env.DM_RELAY_URL);
if (origin.protocol !== 'https:' || origin.username || origin.password) throw Error('HTTPS relay origin required');
const key = process.env.DM_ADMIN_KEY;
if (!/^[a-f0-9]{64}$/.test(key ?? '')) throw Error('Set DM_ADMIN_KEY to the 256-bit operator secret.');
const response = await fetch(new URL(`/v1/admin/${action}`,origin), {
  method:'POST', redirect:'error', headers:{'Content-Type':'application/json',Authorization:`Bearer ${key}`},
  body:JSON.stringify(action === 'issue' ? {label:value} : action === 'revoke' ? {id:value} : {})
});
if (!response.ok) throw Error(`Operator request rejected (${response.status})`);
console.log(JSON.stringify(await response.json(),null,2));
