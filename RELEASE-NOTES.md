DiceMaster v0.4.0 adds a public table browser with one-click joining and an optional code-only setting when creating rooms. Rooms created by older clients remain unlisted. Empty and expired tables disappear automatically. Listings contain only the table name, room code and seat count; GM credentials, modifiers and audit information remain private.

Your Display Name is now explicitly labeled and initially uses the logged-in character name when no custom name is saved. Joining and creation are disabled until a name is entered. A blank relay address uses the default relay.

Eight new finishes join the collection: Emerald Marble, Rose Quartz, Sapphire Marble, Amethyst Ice, Jade Frost, Honey Amber, Rainbow Opal and Prismatic Night. The first six use color treatments of existing material textures; the two rainbow finishes include new texture artwork. All viewers see the rolling player's chosen finish.

Deploy the updated relay before installing this plugin release. Everyone should update to see the new finishes. Existing choices, remembered Windows-encrypted authentication and GM credentials are preserved. Existing rooms remain code-only until recreated as public.

Validation: 15 relay/security/storage tests pass, geometry/motion checks pass, and the Release build completes against Dalamud API 15. In-game layout and visual appearance still need a live check. The update script verifies public listing and a rainbow roll on the live relay before publishing.

Polling now uses one request per second. The relay caches committed state inside its single authority and persists participant heartbeats at most every 15 seconds. High-frequency general traffic counters stay in memory; authentication throttles, credentials, revocations and gameplay state remain durable. A four-player test performs 960 polls with no repeated database reads and at most 24 row mutations. This fixes excessive writes that exhausted the free-tier allowance. An already exhausted quota must still reset before service resumes.

